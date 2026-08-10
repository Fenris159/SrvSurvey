using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is process-scoped and its semaphore may still have in-flight waiters.")]
public sealed class FollowRouteStore
{
    private const string RouteFileExtension = ".json";
    private const string WorkspaceFileName = ".workspace.json";
    private const string NotePropertyName = "notes";
    private const string SavedRouteMissingMessage = "The saved route no longer exists.";
    private const string SavedRouteNameAlreadyExistsMessage =
        "A saved route named '{0}' already exists.";
    private const string SavedRouteMustBeJsonFileMessage =
        "The selected route must be a JSON file.";
    private const string SelectedRouteMissingMessage =
        "The selected route file no longer exists:";
    private const string SavedRouteLoadErrorMessage = "The saved route could not be loaded.";
    private const string FavoriteRouteReloadErrorMessage = "The favorite route could not be reloaded.";
    private const string RenamedRouteReloadErrorMessage = "The renamed route could not be reloaded.";
    private const string SavedRouteExportLoadErrorMessage =
        "The saved route could not be loaded for export.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string dataDirectory;
    private readonly FollowRouteKind routeKind;
    private readonly SemaphoreSlim saveLock = new(1, 1);

    public FollowRouteStore(
        string dataDirectory,
        FollowRouteKind routeKind = FollowRouteKind.Standard)
    {
        this.dataDirectory = GetFullPath(dataDirectory);
        this.routeKind = routeKind;
    }

    public async Task<FollowRouteLoadResult> LoadAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        var selection = await ReadSelectionAsync(frontierId, cancellationToken)
            .ConfigureAwait(false);
        if (selection.Error is not null)
        {
            return new FollowRouteLoadResult(
                GetPath(frontierId),
                true,
                null,
                selection.Error);
        }

        if (selection.Exists)
        {
            if (selection.FileName is null)
            {
                return new FollowRouteLoadResult(
                    GetPath(frontierId),
                    false,
                    CreateDefault(frontierId, GetPath(frontierId)),
                    null);
            }

            var selectedPath = ResolveCatalogPath(
                frontierId,
                selection.FileName,
                selection.IsLegacy);
            if (!File.Exists(selectedPath))
            {
                return new FollowRouteLoadResult(
                    selectedPath,
                    true,
                    null,
                    $"{SelectedRouteMissingMessage} {selectedPath}");
            }

            return await LoadFromPathAsync(
                    frontierId,
                    selectedPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var path = GetLegacyPaths(frontierId).FirstOrDefault(File.Exists)
            ?? GetPath(frontierId);
        if (!File.Exists(path))
        {
            return new FollowRouteLoadResult(
                path,
                false,
                CreateDefault(frontierId, path),
                null);
        }

        return await LoadFromPathAsync(frontierId, path, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FollowRouteCatalogEntry>> ListAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        var paths = new List<(string Path, bool IsLegacy)>();
        foreach (var legacyPath in GetLegacyPaths(frontierId).Where(File.Exists))
        {
            paths.Add((legacyPath, true));
        }

        var namedDirectory = GetNamedDirectory(frontierId);
        if (Directory.Exists(namedDirectory))
        {
            paths.AddRange(Directory
                .EnumerateFiles(namedDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    RouteFileExtension,
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    WorkspaceFileName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => (path, false)));
        }

        var result = new List<FollowRouteCatalogEntry>();
        foreach (var candidate in paths)
        {
            var loaded = await LoadFromPathAsync(
                    frontierId,
                    candidate.Path,
                    cancellationToken)
                .ConfigureAwait(false);
            if (loaded.Route is not { } route)
            {
                continue;
            }

            var routeName = route.Name?.Trim();
            if (string.IsNullOrWhiteSpace(routeName))
            {
                routeName = candidate.IsLegacy
                    ? $"Commander route ({frontierId})"
                    : Path.GetFileNameWithoutExtension(candidate.Path);
            }
            result.Add(new FollowRouteCatalogEntry(
                routeName,
                Path.GetFileName(candidate.Path),
                candidate.Path,
                candidate.IsLegacy,
                new DateTimeOffset(
                    File.GetLastWriteTimeUtc(candidate.Path),
                    TimeSpan.Zero),
                new DateTimeOffset(
                    File.GetCreationTimeUtc(candidate.Path),
                    TimeSpan.Zero),
                route.Notes,
                route.IsFavorite));
        }

        return result
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<FollowRouteLoadResult> LoadNamedAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveCatalogPath(frontierId, fileName, isLegacy);
        var result = await LoadFromPathAsync(frontierId, path, cancellationToken)
            .ConfigureAwait(false);
        if (result.Exists && result.Route is not null)
        {
            await WriteSelectionAsync(
                    frontierId,
                    fileName,
                    isLegacy,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public Task<FollowRouteLoadResult> ReloadAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var path = ResolveWritablePath(route);
        return LoadFromPathAsync(route.FrontierId, path, cancellationToken);
    }

    public async Task<FollowRouteDocument> CreateNewAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        await WriteSelectionAsync(
                frontierId,
                fileName: null,
                isLegacy: false,
                cancellationToken)
            .ConfigureAwait(false);
        return CreateDefault(frontierId, GetPath(frontierId));
    }

    public async Task<FollowRouteDocument> SaveAsAsync(
        FollowRouteDocument route,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var normalizedName = NormalizeRouteName(name);
        var path = GetNamedPath(route.FrontierId, normalizedName);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                throw new IOException(
                    string.Format(
                        SavedRouteNameAlreadyExistsMessage,
                        normalizedName));
            }

            var saved = route with
            {
                FilePath = path,
                Name = normalizedName,
            };
            await SaveRouteObjectAsync(saved, path, cancellationToken)
                .ConfigureAwait(false);
            await WriteSelectionObjectAsync(
                    route.FrontierId,
                    Path.GetFileName(path),
                    isLegacy: false,
                    cancellationToken)
                .ConfigureAwait(false);
            return saved;
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task SaveAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var path = ResolveWritablePath(route);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveRouteObjectAsync(route, path, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task SaveProgressAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var path = ResolveWritablePath(route);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadRequiredObjectAsync(path, cancellationToken)
                .ConfigureAwait(false);
            root["active"] = route.IsActive;
            root["autoCopy"] = route.AutoCopy;
            root["last"] = route.LastReachedIndex;
            MergeBioProgress(root["hops"] as JsonArray, route.Hops);
            await WriteObjectAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<FollowRouteDocument> SaveNotesAsync(
        FollowRouteDocument route,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var path = ResolveWritablePath(route);
        var normalizedNotes = NormalizeNotes(notes);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadRequiredObjectAsync(path, cancellationToken)
                .ConfigureAwait(false);
            WriteOptional(root, NotePropertyName, normalizedNotes);
            await WriteObjectAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return route with { Notes = normalizedNotes };
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<FollowRouteDocument> SaveNotesAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadNamedWithoutSelectionAsync(
                frontierId,
                fileName,
                isLegacy,
                cancellationToken)
            .ConfigureAwait(false);
        if (loaded.Route is null)
        {
            throw new InvalidDataException(
                loaded.Error ?? SavedRouteLoadErrorMessage);
        }

        return await SaveNotesAsync(loaded.Route, notes, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FollowRouteDocument> SetFavoriteAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveCatalogPath(frontierId, fileName, isLegacy);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadRequiredObjectAsync(path, cancellationToken)
                .ConfigureAwait(false);
            WriteTrue(root, "favorite", isFavorite);
            await WriteObjectAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }

        var loaded = await LoadFromPathAsync(
                frontierId,
                path,
                cancellationToken)
            .ConfigureAwait(false);
        return loaded.Route ?? throw new InvalidDataException(
            loaded.Error ?? FavoriteRouteReloadErrorMessage);
    }

    public async Task<FollowRouteDocument> ImportAsync(
        string frontierId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!string.Equals(
                Path.GetExtension(fullSourcePath),
                RouteFileExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The selected file is not a JSON route: {fullSourcePath}");
        }

        var loaded = await LoadFromPathAsync(
                frontierId,
                fullSourcePath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.Exists || loaded.Route is null)
        {
            throw new InvalidDataException(
                loaded.Error ?? $"The selected route does not exist: {fullSourcePath}");
        }

        var requestedName = string.IsNullOrWhiteSpace(loaded.Route.Name)
            ? Path.GetFileNameWithoutExtension(fullSourcePath)
            : loaded.Route.Name.Trim();
        var normalizedName = NormalizeRouteName(requestedName);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetAvailableNamedPath(frontierId, normalizedName);
            var imported = loaded.Route with
            {
                FrontierId = frontierId,
                FilePath = path,
                Name = Path.GetFileNameWithoutExtension(path),
            };
            await SaveRouteObjectAsync(imported, path, cancellationToken)
                .ConfigureAwait(false);
            return imported;
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ExportAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var fullDestination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestination);
        var exported = new List<string>(routes.Count);
        foreach (var route in routes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveCatalogPath(
                frontierId,
                route.FileName,
                route.IsLegacy);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    SavedRouteMissingMessage,
                    source);
            }

            var destination = GetAvailableExportPath(
                fullDestination,
                Path.GetFileName(source));
            await using var sourceStream = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destinationStream = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken)
                .ConfigureAwait(false);
            await destinationStream.FlushAsync(cancellationToken)
                .ConfigureAwait(false);
            exported.Add(destination);
        }

        return exported;
    }

    public Task<IReadOnlyList<string>> ExportSpanshAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        return ExportFormattedAsync(
            frontierId,
            routes,
            destinationDirectory,
            route => route.FileName,
            FollowRouteExportWriter.WriteSpanshAsync,
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> ExportCsvAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        return ExportFormattedAsync(
            frontierId,
            routes,
            destinationDirectory,
            route => Path.ChangeExtension(route.FileName, ".csv"),
            FollowRouteExportWriter.WriteCsvAsync,
            cancellationToken);
    }

    public async Task<FollowRouteRenameResult> RenameAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        string name,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveCatalogPath(frontierId, fileName, isLegacy);
        var normalizedName = NormalizeRouteName(name);
        var destination = GetNamedPath(frontierId, normalizedName);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var samePath = string.Equals(source, destination, comparison);

        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    SavedRouteMissingMessage,
                    source);
            }

            if (!samePath && File.Exists(destination))
            {
                throw new IOException(
                    string.Format(
                        SavedRouteNameAlreadyExistsMessage,
                        normalizedName));
            }

            var root = await ReadRequiredObjectAsync(source, cancellationToken)
                .ConfigureAwait(false);
            root["name"] = normalizedName;
            var createdAt = File.GetCreationTimeUtc(source);
            await WriteObjectAsync(
                    samePath ? source : destination,
                    root,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!samePath)
            {
                try
                {
                    File.SetCreationTimeUtc(destination, createdAt);
                    File.Delete(source);
                }
                catch
                {
                    if (File.Exists(source) && File.Exists(destination))
                    {
                        File.Delete(destination);
                    }

                    throw;
                }
            }

            var selection = await ReadSelectionAsync(frontierId, cancellationToken)
                .ConfigureAwait(false);
            if (selection.Error is not null)
            {
                throw new InvalidDataException(selection.Error);
            }

            if (selection.FileName is not null
                && selection.IsLegacy == isLegacy
                && string.Equals(
                    selection.FileName,
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteSelectionObjectAsync(
                        frontierId,
                        Path.GetFileName(destination),
                        isLegacy: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var loaded = await LoadFromPathAsync(
                    frontierId,
                    destination,
                    cancellationToken)
                .ConfigureAwait(false);
            var renamed = loaded.Route ?? throw new InvalidDataException(
                loaded.Error ?? RenamedRouteReloadErrorMessage);
            var entry = new FollowRouteCatalogEntry(
                normalizedName,
                Path.GetFileName(destination),
                destination,
                IsLegacy: false,
                new DateTimeOffset(
                    File.GetLastWriteTimeUtc(destination),
                    TimeSpan.Zero),
                new DateTimeOffset(
                    File.GetCreationTimeUtc(destination),
                    TimeSpan.Zero),
                renamed.Notes,
                renamed.IsFavorite);
            return new FollowRouteRenameResult(source, renamed, entry);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<string> DeleteNamedAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveCatalogPath(frontierId, fileName, isLegacy);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    SavedRouteMissingMessage,
                    path);
            }

            var trashDirectory = Path.Combine(
                GetNamedDirectory(frontierId),
                ".trash");
            Directory.CreateDirectory(trashDirectory);
            var trashPath = Path.Combine(
                trashDirectory,
                $"{Path.GetFileNameWithoutExtension(path)}-"
                    + $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-"
                    + $"{Guid.NewGuid():N}{RouteFileExtension}");
            File.Move(path, trashPath);

            var selection = await ReadSelectionAsync(
                    frontierId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (selection.FileName is not null
                && selection.IsLegacy == isLegacy
                && string.Equals(
                    selection.FileName,
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteSelectionObjectAsync(
                        frontierId,
                        fileName: null,
                        isLegacy: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return trashPath;
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<string> DeleteAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var path = ResolveWritablePath(route);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    SavedRouteMissingMessage,
                    path);
            }

            var trashDirectory = Path.Combine(
                GetNamedDirectory(route.FrontierId),
                ".trash");
            Directory.CreateDirectory(trashDirectory);
            var trashPath = Path.Combine(
                trashDirectory,
                $"{Path.GetFileNameWithoutExtension(path)}-"
                    + $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{RouteFileExtension}");
            File.Move(path, trashPath);
            await WriteSelectionObjectAsync(
                    route.FrontierId,
                    fileName: null,
                    isLegacy: false,
                    cancellationToken)
                .ConfigureAwait(false);
            return trashPath;
        }
        finally
        {
            saveLock.Release();
        }
    }

    public string GetPath(string frontierId)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        return Path.Combine(GetRoutesDirectory(), frontierId + RouteFileExtension);
    }

    private string GetRoutesDirectory()
    {
        var routesDirectory = Path.Combine(dataDirectory, "Routes");
        return routeKind == FollowRouteKind.FleetCarrier
            ? Path.Combine(routesDirectory, "FleetCarrier")
            : routesDirectory;
    }

    private string GetNamedDirectory(string frontierId)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        return Path.Combine(GetRoutesDirectory(), frontierId);
    }

    private string GetNamedPath(string frontierId, string name)
    {
        return Path.Combine(
            GetNamedDirectory(frontierId),
            CreateRouteFileName(name));
    }

    private string GetAvailableNamedPath(string frontierId, string name)
    {
        var path = GetNamedPath(frontierId, name);
        if (!File.Exists(path))
        {
            return path;
        }

        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            path = GetNamedPath(frontierId, $"{name} ({suffix:N0})");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        throw new IOException(
            $"No available file name could be created for route '{name}'.");
    }

    private static string GetAvailableExportPath(
        string destinationDirectory,
        string fileName)
    {
        var path = Path.Combine(destinationDirectory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            path = Path.Combine(
                destinationDirectory,
                $"{stem} ({suffix:N0}){extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        throw new IOException(
            $"No available export name could be created for '{fileName}'.");
    }

    private string GetWorkspacePath(string frontierId)
    {
        return Path.Combine(GetNamedDirectory(frontierId), WorkspaceFileName);
    }

    private IEnumerable<string> GetLegacyPaths(string frontierId)
    {
        if (routeKind != FollowRouteKind.Standard)
        {
            return [];
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return new[]
            {
                GetPath(frontierId),
                Path.Combine(dataDirectory, "routes", frontierId + RouteFileExtension),
            }
            .Select(Path.GetFullPath)
            .Distinct(comparison);
    }

    private string ResolveCatalogPath(
        string frontierId,
        string fileName,
        bool isLegacy)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        ValidateFileName(fileName, nameof(fileName));
        if (!string.Equals(
                Path.GetExtension(fileName),
                RouteFileExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                SavedRouteMustBeJsonFileMessage,
                nameof(fileName));
        }

        if (!isLegacy)
        {
            return Path.Combine(GetNamedDirectory(frontierId), fileName);
        }

        if (routeKind != FollowRouteKind.Standard)
        {
            throw new InvalidOperationException(
                "Fleet-carrier routes do not use the standard legacy route location.");
        }

        var path = GetLegacyPaths(frontierId)
            .FirstOrDefault(path => File.Exists(path)
                && string.Equals(
                    Path.GetFileName(path),
                    fileName,
                    StringComparison.OrdinalIgnoreCase));
        return path ?? throw new FileNotFoundException(
            "The selected legacy route no longer exists.",
            fileName);
    }

    private string ResolveWritablePath(FollowRouteDocument route)
    {
        ValidateFileName(route.FrontierId, nameof(route.FrontierId));
        var path = Path.GetFullPath(route.FilePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (GetLegacyPaths(route.FrontierId)
            .Any(candidate => string.Equals(candidate, path, comparison)))
        {
            return path;
        }

        var namedDirectory = Path.GetFullPath(GetNamedDirectory(route.FrontierId));
        var relativePath = Path.GetRelativePath(namedDirectory, path);
        if (relativePath == "."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, comparison)
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(Path.DirectorySeparatorChar)
            || relativePath.Contains(Path.AltDirectorySeparatorChar)
            || string.Equals(relativePath, WorkspaceFileName, comparison)
            || !string.Equals(
                Path.GetExtension(relativePath),
                RouteFileExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The route file is outside this commander's Routes folder.");
        }

        return path;
    }

    private async Task<FollowRouteLoadResult> LoadFromPathAsync(
        string frontierId,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new FollowRouteLoadResult(
                path,
                false,
                CreateDefault(frontierId, path),
                null);
        }

        var read = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (read.Root is null)
        {
            return new FollowRouteLoadResult(path, true, null, read.Error);
        }

        try
        {
            return new FollowRouteLoadResult(
                path,
                true,
                Parse(frontierId, path, read.Root),
                null);
        }
        catch (InvalidDataException exception)
        {
            return new FollowRouteLoadResult(path, true, null, exception.Message);
        }
    }

    private Task<FollowRouteLoadResult> LoadNamedWithoutSelectionAsync(
        string frontierId,
        string fileName,
        bool isLegacy,
        CancellationToken cancellationToken)
    {
        var path = ResolveCatalogPath(frontierId, fileName, isLegacy);
        return LoadFromPathAsync(frontierId, path, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ExportFormattedAsync(
        string frontierId,
        IReadOnlyList<FollowRouteCatalogEntry> routes,
        string destinationDirectory,
        Func<FollowRouteCatalogEntry, string> getFileName,
        Func<FollowRouteDocument, string, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var fullDestination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestination);
        var exported = new List<string>(routes.Count);
        foreach (var route in routes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await LoadNamedWithoutSelectionAsync(
                    frontierId,
                    route.FileName,
                    route.IsLegacy,
                    cancellationToken)
                .ConfigureAwait(false);
            var document = loaded.Route ?? throw new InvalidDataException(
                loaded.Error ?? SavedRouteExportLoadErrorMessage);
            var destination = GetAvailableExportPath(
                fullDestination,
                getFileName(route));
            await write(document, destination, cancellationToken)
                .ConfigureAwait(false);
            exported.Add(destination);
        }

        return exported;
    }

    private async Task SaveRouteObjectAsync(
        FollowRouteDocument route,
        string path,
        CancellationToken cancellationToken)
    {
        if (route.Kind != routeKind)
        {
            throw new InvalidOperationException(
                "The route belongs to a different route library.");
        }

        JsonObject root;
        if (File.Exists(path))
        {
            root = await ReadRequiredObjectAsync(path, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            root = [];
        }

        WriteOptional(root, "name", NormalizeOptionalText(route.Name));
        WriteOptional(root, NotePropertyName, NormalizeNotes(route.Notes));
        WriteOptional(
            root,
            "spanshRouteKind",
            route.SourceSpanshKind?.ToString());
        if (route.Kind == FollowRouteKind.FleetCarrier)
        {
            root["routeType"] = "fleetCarrier";
        }
        else
        {
            root.Remove("routeType");
        }
        WriteTrue(root, "favorite", route.IsFavorite);
        root["active"] = route.IsActive;
        root["autoCopy"] = route.AutoCopy;
        root["last"] = route.LastReachedIndex;
        root["hops"] = MergeHops(root["hops"] as JsonArray, route.Hops);
        await WriteObjectAsync(path, root, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonObject> ReadRequiredObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                SavedRouteMissingMessage,
                path);
        }

        var read = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        return read.Root ?? throw new InvalidDataException(
            read.Error ?? $"The route {path} could not be read.");
    }

    private async Task<WorkspaceSelectionReadResult> ReadSelectionAsync(
        string frontierId,
        CancellationToken cancellationToken)
    {
        var path = GetWorkspacePath(frontierId);
        if (!File.Exists(path))
        {
            return new WorkspaceSelectionReadResult(false, null, false, null);
        }

        var read = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (read.Root is null)
        {
            return new WorkspaceSelectionReadResult(
                true,
                null,
                false,
                read.Error);
        }

        var fileName = GetString(read.Root, "selectedFile");
        if (fileName is not null)
        {
            try
            {
                ValidateFileName(fileName, "selectedFile");
            }
            catch (ArgumentException exception)
            {
                return new WorkspaceSelectionReadResult(
                    true,
                    null,
                    false,
                    $"Could not read {path}: {exception.Message}");
            }
        }

        return new WorkspaceSelectionReadResult(
            true,
            fileName,
            GetBoolean(read.Root, "legacy") ?? false,
            null);
    }

    private async Task WriteSelectionAsync(
        string frontierId,
        string? fileName,
        bool isLegacy,
        CancellationToken cancellationToken)
    {
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteSelectionObjectAsync(
                    frontierId,
                    fileName,
                    isLegacy,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task WriteSelectionObjectAsync(
        string frontierId,
        string? fileName,
        bool isLegacy,
        CancellationToken cancellationToken)
    {
        var root = new JsonObject();
        WriteOptional(root, "selectedFile", fileName);
        if (fileName is not null)
        {
            root["legacy"] = isLegacy;
        }

        await WriteObjectAsync(
                GetWorkspacePath(frontierId),
                root,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeRouteName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 80)
        {
            throw new ArgumentException(
                "The route name cannot be longer than 80 characters.",
                nameof(name));
        }

        if (normalized is "." or "..")
        {
            throw new ArgumentException("Enter a descriptive route name.", nameof(name));
        }

        return normalized;
    }

    private static string CreateRouteFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*'])
            .ToHashSet();
        var characters = name
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray();
        var stem = new string(characters).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(stem))
        {
            throw new ArgumentException(
                "The route name must contain at least one file-safe character.",
                nameof(name));
        }

        return stem + RouteFileExtension;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeNotes(string? notes)
    {
        return string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private FollowRouteDocument CreateDefault(
        string frontierId,
        string path)
    {
        return new FollowRouteDocument(
            frontierId,
            path,
            true,
            true,
            -1,
            [],
            Kind: routeKind);
    }

    private FollowRouteDocument Parse(
        string frontierId,
        string path,
        JsonObject root)
    {
        var hops = new List<FollowRouteHop>();
        if (root["hops"] is JsonArray hopArray)
        {
            for (var index = 0; index < hopArray.Count; index++)
            {
                if (hopArray[index] is not JsonObject hop)
                {
                    throw InvalidRoute(path, $"hops[{index}] is not an object");
                }

                hops.Add(ParseHop(path, index, hop));
            }
        }
        else if (root.ContainsKey("hops"))
        {
            throw InvalidRoute(path, "hops is not an array");
        }

        var parsedKind = GetString(root, "routeType") switch
        {
            null or "" or "standard" => FollowRouteKind.Standard,
            "fleetCarrier" => FollowRouteKind.FleetCarrier,
            var value => throw InvalidRoute(
                path,
                $"routeType '{value}' is not supported"),
        };
        if (parsedKind != routeKind)
        {
            throw InvalidRoute(
                path,
                parsedKind == FollowRouteKind.FleetCarrier
                    ? "the file belongs in FC Routes"
                    : "the file is not marked as a fleet-carrier route");
        }

        return new FollowRouteDocument(
            frontierId,
            path,
            GetBoolean(root, "active") ?? true,
            GetBoolean(root, "autoCopy") ?? true,
            GetInt32(root, "last") ?? -1,
            hops,
            NormalizeOptionalText(GetString(root, "name")),
            NormalizeNotes(GetString(root, NotePropertyName)),
            GetBoolean(root, "favorite") ?? false,
            parsedKind,
            ParseSpanshRouteKind(path, GetString(root, "spanshRouteKind")));
    }

    private static SpanshRouteKind? ParseSpanshRouteKind(
        string path,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedValue = value.Trim();
        if (Enum.TryParse<SpanshRouteKind>(
                normalizedValue,
                ignoreCase: true,
                out var kind)
            && Enum.IsDefined(kind)
            && string.Equals(
                Enum.GetName(kind),
                normalizedValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return kind;
        }

        throw InvalidRoute(
            path,
            $"spanshRouteKind '{value}' is not supported");
    }

    private static FollowRouteHop ParseHop(
        string path,
        int index,
        JsonObject root)
    {
        var name = GetString(root, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw InvalidRoute(path, $"hops[{index}] has no valid name");
        }

        GalacticCoordinate? position = null;
        var x = GetDouble(root, "x");
        var y = GetDouble(root, "y");
        var z = GetDouble(root, "z");
        if (x is not null && y is not null && z is not null)
        {
            try
            {
                position = new GalacticCoordinate(x.Value, y.Value, z.Value);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw InvalidRoute(
                    path,
                    $"hops[{index}] has invalid coordinates: {exception.Message}");
            }
        }

        return new FollowRouteHop(
            name,
            GetInt64(root, "id64"),
            position,
            GetString(root, NotePropertyName),
            GetBoolean(root, "refuel") ?? false,
            GetBoolean(root, "neutron") ?? false,
            ParseBioTargets(path, index, root["bio"]),
            ParseCarrierHop(path, index, root["carrier"]));
    }

    private static FollowRouteCarrierHop? ParseCarrierHop(
        string path,
        int hopIndex,
        JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonObject carrier)
        {
            throw InvalidRoute(path, $"hops[{hopIndex}].carrier is not an object");
        }

        return new FollowRouteCarrierHop(
            GetDouble(carrier, "distanceLy"),
            GetDouble(carrier, "remainingLy"),
            GetDouble(carrier, "fuelRemainingTonnes"),
            GetDouble(carrier, "tritiumInMarketTonnes"),
            GetDouble(carrier, "fuelUsedTonnes"),
            GetBoolean(carrier, "hasIcyRing") ?? false,
            GetBoolean(carrier, "systemPristine") ?? false,
            GetBoolean(carrier, "mustRestock") ?? false,
            GetDouble(carrier, "restockAmountTonnes"));
    }

    private static List<FollowRouteBioTarget>? ParseBioTargets(
        string path,
        int hopIndex,
        JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray array)
        {
            throw InvalidRoute(path, $"hops[{hopIndex}].bio is not an array");
        }

        var result = new List<FollowRouteBioTarget>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject target)
            {
                throw InvalidRoute(
                    path,
                    $"hops[{hopIndex}].bio[{index}] is not an object");
            }

            result.Add(ParseBioTarget(path, hopIndex, index, target));
        }

        return result.Count == 0 ? null : result;
    }

    private static FollowRouteBioTarget ParseBioTarget(
        string path,
        int hopIndex,
        int index,
        JsonObject target)
    {
        var bodyName = GetString(target, "body");
        if (string.IsNullOrWhiteSpace(bodyName))
        {
            throw InvalidRoute(
                path,
                $"hops[{hopIndex}].bio[{index}] has no body name");
        }

        return new FollowRouteBioTarget(
            bodyName.Trim(),
            GetInt64(target, "bodyId"),
            ParseBioTargetSpecies(path, hopIndex, index, target),
            GetBoolean(target, "completed") ?? false,
            NormalizeOptionalText(GetString(target, "subtype")),
            GetDouble(target, "distanceToArrivalLs"),
            GetInt64(target, "estimatedScanValue"),
            GetInt64(target, "estimatedMappingValue"),
            GetInt64(target, "estimatedBiologyValue"),
            GetBoolean(target, "terraformable") ?? false,
            GetBoolean(target, "biological") ?? true);
    }

    private static List<string> ParseBioTargetSpecies(
        string path,
        int hopIndex,
        int index,
        JsonObject target)
    {
        var species = new List<string>();
        if (target["species"] is not JsonArray speciesArray)
        {
            if (target.ContainsKey("species"))
            {
                throw InvalidRoute(
                    path,
                    $"hops[{hopIndex}].bio[{index}].species is not an array");
            }

            return [];
        }

        for (var speciesIndex = 0;
            speciesIndex < speciesArray.Count;
            speciesIndex++)
        {
            if (speciesArray[speciesIndex] is not JsonValue value
                || !value.TryGetValue<string>(out var speciesName)
                || string.IsNullOrWhiteSpace(speciesName))
            {
                throw InvalidRoute(
                    path,
                    $"hops[{hopIndex}].bio[{index}].species[{speciesIndex}] is not a name");
            }

            if (!species.Contains(
                speciesName,
                StringComparer.OrdinalIgnoreCase))
            {
                species.Add(speciesName.Trim());
            }
        }

        return species;
    }

    private static JsonArray MergeHops(
        JsonArray? existing,
        IReadOnlyList<FollowRouteHop> hops)
    {
        var existingRows = existing?
            .Select((node, index) => new ExistingHop(
                index,
                node as JsonObject,
                node is JsonObject row ? GetIdentity(row) : null))
            .ToArray() ?? [];
        var used = new HashSet<int>();
        var result = new JsonArray();
        foreach (var hop in hops)
        {
            var identity = GetIdentity(hop);
            var match = existingRows.FirstOrDefault(candidate =>
                !used.Contains(candidate.Index)
                && string.Equals(
                    candidate.Identity,
                    identity,
                    StringComparison.OrdinalIgnoreCase));
            JsonObject row;
            if (match?.Root is not null)
            {
                used.Add(match.Index);
                row = match.Root.DeepClone().AsObject();
            }
            else
            {
                row = [];
            }

            WriteHop(row, hop);
            result.Add(row);
        }

        return result;
    }

    private static void WriteHop(JsonObject root, FollowRouteHop hop)
    {
        if (string.IsNullOrWhiteSpace(hop.Name))
        {
            throw new InvalidDataException("A route hop name cannot be blank.");
        }

        root["name"] = hop.Name;
        WriteOptional(root, "id64", hop.SystemAddress);
        if (hop.Position is { } position)
        {
            root["x"] = position.X;
            root["y"] = position.Y;
            root["z"] = position.Z;
        }
        else
        {
            root.Remove("x");
            root.Remove("y");
            root.Remove("z");
        }

        WriteOptional(root, NotePropertyName, hop.Notes);
        WriteTrue(root, "refuel", hop.Refuel);
        WriteTrue(root, "neutron", hop.Neutron);
        WriteBioTargets(root, hop.BioTargets);
        WriteCarrierHop(root, hop.Carrier);
    }

    private static void WriteCarrierHop(
        JsonObject root,
        FollowRouteCarrierHop? carrier)
    {
        if (carrier is null)
        {
            root.Remove("carrier");
            return;
        }

        var node = root["carrier"] as JsonObject;
        if (node is null)
        {
            node = [];
            root["carrier"] = node;
        }
        WriteOptional(node, "distanceLy", carrier.DistanceLy);
        WriteOptional(node, "remainingLy", carrier.RemainingLy);
        WriteOptional(node, "fuelRemainingTonnes", carrier.FuelRemainingTonnes);
        WriteOptional(node, "tritiumInMarketTonnes", carrier.TritiumInMarketTonnes);
        WriteOptional(node, "fuelUsedTonnes", carrier.FuelUsedTonnes);
        WriteTrue(node, "hasIcyRing", carrier.HasIcyRing);
        WriteTrue(node, "systemPristine", carrier.IsSystemPristine);
        WriteTrue(node, "mustRestock", carrier.MustRestock);
        WriteOptional(node, "restockAmountTonnes", carrier.RestockAmountTonnes);
    }

    private static void WriteBioTargets(
        JsonObject root,
        IReadOnlyList<FollowRouteBioTarget> targets)
    {
        if (targets.Count == 0)
        {
            root.Remove("bio");
            return;
        }

        var existing = root["bio"] as JsonArray;
        var existingRows = existing?
            .Select((node, index) => new ExistingBioTarget(
                index,
                node as JsonObject,
                node is JsonObject row ? GetBioIdentity(row) : null))
            .ToArray() ?? [];
        var used = new HashSet<int>();
        var result = new JsonArray();
        foreach (var target in targets)
        {
            var identity = GetBioIdentity(target);
            var match = existingRows.FirstOrDefault(candidate =>
                !used.Contains(candidate.Index)
                && string.Equals(
                    candidate.Identity,
                    identity,
                    StringComparison.OrdinalIgnoreCase));
            var row = match?.Root?.DeepClone().AsObject() ?? [];
            if (match?.Root is not null)
            {
                used.Add(match.Index);
            }

            row["body"] = target.BodyName;
            WriteOptional(row, "bodyId", target.BodyId);
            row["species"] = new JsonArray(target.Species
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => JsonValue.Create(name.Trim()))
                .ToArray());
            WriteTrue(row, "completed", target.IsCompleted);
            WriteOptional(row, "subtype", NormalizeOptionalText(target.Subtype));
            WriteOptional(row, "distanceToArrivalLs", target.DistanceToArrivalLs);
            WriteOptional(row, "estimatedScanValue", target.EstimatedScanValue);
            WriteOptional(row, "estimatedMappingValue", target.EstimatedMappingValue);
            WriteOptional(row, "estimatedBiologyValue", target.EstimatedBiologyValue);
            WriteTrue(row, "terraformable", target.IsTerraformable);
            row["biological"] = target.IsBiological;
            result.Add(row);
        }

        root["bio"] = result;
    }

    private static void MergeBioProgress(
        JsonArray? existingHops,
        IReadOnlyList<FollowRouteHop> hops)
    {
        if (existingHops is null)
        {
            return;
        }

        var existingRows = existingHops.OfType<JsonObject>().ToArray();
        foreach (var hop in hops.Where(candidate => candidate.BioTargets.Count > 0))
        {
            var identity = GetIdentity(hop);
            var row = existingRows.FirstOrDefault(candidate => string.Equals(
                GetIdentity(candidate),
                identity,
                StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                continue;
            }

            var savedTargets = row["bio"] as JsonArray;
            if (savedTargets is null)
            {
                WriteBioTargets(row, hop.BioTargets);
                continue;
            }

            foreach (var target in hop.BioTargets)
            {
                var targetIdentity = GetBioIdentity(target);
                var saved = savedTargets
                    .OfType<JsonObject>()
                    .FirstOrDefault(candidate => string.Equals(
                        GetBioIdentity(candidate),
                        targetIdentity,
                        StringComparison.OrdinalIgnoreCase));
                if (saved is null)
                {
                    continue;
                }

                WriteTrue(saved, "completed", target.IsCompleted);
            }
        }
    }

    private static string GetBioIdentity(FollowRouteBioTarget target)
    {
        return target.BodyId is { } bodyId
            ? $"bodyId:{bodyId}"
            : $"body:{target.BodyName}";
    }

    private static string? GetBioIdentity(JsonObject root)
    {
        var bodyId = GetInt64(root, "bodyId");
        var bodyName = GetString(root, "body");
        if (bodyId is not null)
        {
            return $"bodyId:{bodyId}";
        }

        return string.IsNullOrWhiteSpace(bodyName)
            ? null
            : $"body:{bodyName}";
    }

    private static string GetIdentity(FollowRouteHop hop)
    {
        if (hop.SystemAddress is { } address)
        {
            return $"address:{address}";
        }

        return $"name:{hop.Name}";
    }

    private static string? GetIdentity(JsonObject root)
    {
        var address = GetInt64(root, "id64");
        var name = GetString(root, "name");
        if (address is not null)
        {
            return $"address:{address}";
        }

        return string.IsNullOrWhiteSpace(name)
            ? null
            : $"name:{name}";
    }

    private static void WriteOptional<T>(
        JsonObject root,
        string propertyName,
        T? value)
    {
        if (value is null)
        {
            root.Remove(propertyName);
        }
        else
        {
            root[propertyName] = JsonValue.Create(value);
        }
    }

    private static void WriteTrue(
        JsonObject root,
        string propertyName,
        bool value)
    {
        if (value)
        {
            root[propertyName] = true;
        }
        else
        {
            root.Remove(propertyName);
        }
    }

    private static InvalidDataException InvalidRoute(
        string path,
        string detail)
    {
        return new InvalidDataException($"The route {path} is invalid: {detail}.");
    }

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        var value = GetInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return value.TryGetValue<long>(out var integer) ? integer : null;
    }

    private static void ValidateFileName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The file name cannot be empty.",
                parameterName);
        }

        if (value is "." or ".."
            || !string.Equals(
                Path.GetFileName(value),
                value,
                StringComparison.Ordinal)
            || value.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || value.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The value must be a file name, not a path.",
                parameterName);
        }
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static async Task<JsonObjectReadResult> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var node = await JsonNode.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return node is JsonObject root
                ? new JsonObjectReadResult(root, null)
                : new JsonObjectReadResult(
                    null,
                    $"{path} does not contain a JSON object.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new JsonObjectReadResult(
                null,
                $"Could not read {path}: {exception.Message}");
        }
    }

    private static async Task WriteObjectAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                $"The route path has no parent directory: {path}");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        root,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record ExistingHop(
        int Index,
        JsonObject? Root,
        string? Identity);

    private sealed record ExistingBioTarget(
        int Index,
        JsonObject? Root,
        string? Identity);

    private sealed record JsonObjectReadResult(JsonObject? Root, string? Error);

    private sealed record WorkspaceSelectionReadResult(
        bool Exists,
        string? FileName,
        bool IsLegacy,
        string? Error);
}
