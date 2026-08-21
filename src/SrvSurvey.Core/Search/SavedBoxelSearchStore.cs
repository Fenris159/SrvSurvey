using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Search;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is application-scoped and its semaphore may still have in-flight waiters.")]
public sealed class SavedBoxelSearchStore : IBoxelSearchLibraryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string rootDirectory;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public SavedBoxelSearchStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        rootDirectory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "savedBoxelSearches");
    }

    public async Task<IReadOnlyList<SavedBoxelSearchCatalogEntry>> ListAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        var directory = GetCommanderDirectory(frontierId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<SavedBoxelSearchCatalogEntry>();
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var document = await LoadFromPathAsync(
                        frontierId,
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                entries.Add(ToCatalogEntry(document));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException)
            {
                // One damaged library entry must not make every saved search unavailable.
            }
        }

        return entries;
    }

    public async Task<SavedBoxelSearchDocument> CreateAsync(
        string frontierId,
        string name,
        string? notes,
        BoxelSearchSnapshot search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var normalizedName = NormalizeName(name);
        var directory = GetCommanderDirectory(frontierId);
        var fileName = $"{CreateFileStem(normalizedName)}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(directory, fileName);
        var now = DateTimeOffset.UtcNow;
        var linkedSearch = search with { SavedSearchFileName = fileName };
        var document = new SavedBoxelSearchDocument(
            frontierId,
            normalizedName,
            NormalizeNotes(notes),
            IsFavorite: false,
            now,
            now,
            linkedSearch,
            fileName,
            path);

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            await WriteAsync(document, cancellationToken).ConfigureAwait(false);
            return document;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task<bool> ExistsAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetPath(frontierId, fileName)));
    }

    public Task<SavedBoxelSearchDocument> LoadAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        return LoadFromPathAsync(
            frontierId,
            GetPath(frontierId, fileName),
            cancellationToken);
    }

    public async Task<SavedBoxelSearchDocument> SaveProgressAsync(
        string frontierId,
        string fileName,
        BoxelSearchSnapshot search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadFromPathAsync(
                    frontierId,
                    GetPath(frontierId, fileName),
                    cancellationToken)
                .ConfigureAwait(false);
            var updated = document with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Search = search with { SavedSearchFileName = document.FileName }
            };
            await WriteAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task<SavedBoxelSearchDocument> RenameAsync(
        string frontierId,
        string fileName,
        string name,
        CancellationToken cancellationToken = default)
    {
        return UpdateMetadataAsync(
            frontierId,
            fileName,
            document => document with { Name = NormalizeName(name) },
            cancellationToken);
    }

    public Task<SavedBoxelSearchDocument> SaveNotesAsync(
        string frontierId,
        string fileName,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        return UpdateMetadataAsync(
            frontierId,
            fileName,
            document => document with { Notes = NormalizeNotes(notes) },
            cancellationToken);
    }

    public Task<SavedBoxelSearchDocument> SetFavoriteAsync(
        string frontierId,
        string fileName,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        return UpdateMetadataAsync(
            frontierId,
            fileName,
            document => document with { IsFavorite = isFavorite },
            cancellationToken);
    }

    public async Task<string> DeleteAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(frontierId, fileName);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The saved boxel search no longer exists.",
                    path);
            }

            var trashDirectory = Path.Combine(
                GetCommanderDirectory(frontierId),
                ".trash");
            Directory.CreateDirectory(trashDirectory);
            var trashPath = Path.Combine(
                trashDirectory,
                $"{Path.GetFileNameWithoutExtension(fileName)}-"
                    + $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
            File.Move(path, trashPath);
            return trashPath;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task<SavedBoxelSearchDocument> UpdateMetadataAsync(
        string frontierId,
        string fileName,
        Func<SavedBoxelSearchDocument, SavedBoxelSearchDocument> update,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadFromPathAsync(
                    frontierId,
                    GetPath(frontierId, fileName),
                    cancellationToken)
                .ConfigureAwait(false);
            var updated = update(document) with { UpdatedAt = DateTimeOffset.UtcNow };
            await WriteAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static async Task<SavedBoxelSearchDocument> LoadFromPathAsync(
        string frontierId,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The saved boxel search no longer exists.",
                path);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var root = await JsonNode.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException(
                "The saved boxel search did not contain a JSON object.");
        var storedFrontierId = GetString(root, "frontierId");
        if (!string.Equals(
                storedFrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The saved boxel search belongs to a different commander profile.");
        }

        var name = GetString(root, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException(
                "The saved boxel search does not have a name.");
        }

        var searchNode = root["search"] as JsonObject
            ?? throw new InvalidDataException(
                "The saved boxel search does not contain progress data.");
        var fileName = Path.GetFileName(path);
        var createdAt = GetDateTimeOffset(root, "createdAt")
            ?? new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero);
        var updatedAt = GetDateTimeOffset(root, "updatedAt")
            ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        return new SavedBoxelSearchDocument(
            frontierId,
            name.Trim(),
            NormalizeNotes(GetString(root, "notes")),
            GetBoolean(root, "favorite") ?? false,
            createdAt,
            updatedAt,
            ReadSnapshot(searchNode, fileName),
            fileName,
            path);
    }

    private static SavedBoxelSearchCatalogEntry ToCatalogEntry(
        SavedBoxelSearchDocument document)
    {
        var progress = CalculateProgress(document.Search);
        var prefixes = new List<string>();
        if (!string.IsNullOrWhiteSpace(document.Search.TopBoxel?.Prefix))
        {
            prefixes.Add(document.Search.TopBoxel.Prefix);
        }

        prefixes.AddRange(document.Search.ProgressByPrefix.Keys);
        return new SavedBoxelSearchCatalogEntry(
            document.Name,
            document.Notes,
            document.IsFavorite,
            document.CreatedAt,
            document.UpdatedAt,
            progress.Completed,
            progress.Total,
            document.Search.ProgressByPrefix.Values.Any(count => count == 0),
            document.FileName,
            document.FilePath,
            document.Search.TopBoxel?.Prefix,
            document.Search.LowMassCode,
            prefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public static (int Completed, int Total) CalculateProgress(
        BoxelSearchSnapshot search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var total = search.ProgressByPrefix.Values.Where(count => count > 0).Sum();
        if (total == 0)
        {
            total = Math.Max(0, search.CurrentCount);
        }

        var completedPrefixes = search.CompletedPrefixes.ToHashSet(
            StringComparer.Ordinal);
        var completed = completedPrefixes.Sum(prefix =>
            Math.Max(0, search.ProgressByPrefix.GetValueOrDefault(prefix)));
        completed += search.CompletedSystems.Count(systemName =>
            BoxelAddress.TryParse(systemName, out var boxel)
            && boxel is not null
            && !completedPrefixes.Contains(boxel.Prefix));
        var completedSystems = search.CompletedSystems.ToHashSet(StringComparer.Ordinal);
        completed += search.EmptySystems.Count(systemName =>
            !completedSystems.Contains(systemName)
            && BoxelAddress.TryParse(systemName, out var boxel)
            && boxel is not null
            && !completedPrefixes.Contains(boxel.Prefix));
        return (Math.Min(completed, total), total);
    }

    private static BoxelSearchSnapshot ReadSnapshot(
        JsonObject node,
        string fileName)
    {
        _ = BoxelAddress.TryParse(GetString(node, "topBoxel"), out var topBoxel);
        _ = BoxelAddress.TryParse(GetString(node, "currentBoxel"), out var current);
        if (topBoxel is not null
            && (current is null || !topBoxel.Contains(current)))
        {
            current = topBoxel;
        }

        var lowMassCodeText = GetString(node, "lowMassCode");
        return new BoxelSearchSnapshot
        {
            Active = GetBoolean(node, "active") ?? false,
            TopBoxel = topBoxel,
            StartedOn = GetDateTimeOffset(node, "startedOn")
                ?? DateTimeOffset.MinValue,
            Current = current,
            CurrentCount = GetInt32(node, "currentCount") ?? 0,
            LowMassCode = string.IsNullOrWhiteSpace(lowMassCodeText)
                ? 'c'
                : char.ToLowerInvariant(lowMassCodeText[0]),
            CompletedPrefixes = ReadStringArray(node, "completedPrefixes"),
            CompletedSystems = ReadStringArray(node, "completedSystems"),
            EmptySystems = ReadStringArray(node, "emptySystems"),
            DeferredSystems = ReadStringArray(node, "deferredSystems"),
            DeferredRanges = BoxelDeferredRangeJson.Read(node, "deferredRanges"),
            ProgressByPrefix = ReadProgress(node),
            AutoCopy = GetBoolean(node, "autoCopy") ?? false,
            SortDescending = GetBoolean(node, "sortDescending") ?? false,
            Collapsed = GetBoolean(node, "collapsed") ?? false,
            SkipAlreadyVisited = GetBoolean(node, "skipAlreadyVisited") ?? false,
            SkipKnownToSpansh = GetBoolean(node, "skipKnownToSpansh") ?? false,
            CompletionMode = GetBoolean(node, "completeOnFssAllBodies") == true
                ? BoxelCompletionMode.FssAllBodies
                : BoxelCompletionMode.EnterSystem,
            SavedSearchFileName = fileName
        };
    }

    private static async Task WriteAsync(
        SavedBoxelSearchDocument document,
        CancellationToken cancellationToken)
    {
        var root = new JsonObject
        {
            ["version"] = 1,
            ["frontierId"] = document.FrontierId,
            ["name"] = document.Name,
            ["notes"] = document.Notes,
            ["favorite"] = document.IsFavorite,
            ["createdAt"] = document.CreatedAt,
            ["updatedAt"] = document.UpdatedAt,
            ["search"] = WriteSnapshot(document.Search)
        };
        var temporaryPath = $"{document.FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        root,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, document.FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonObject WriteSnapshot(BoxelSearchSnapshot search)
    {
        return new JsonObject
        {
            ["active"] = search.Active,
            ["topBoxel"] = search.TopBoxel?.ToStoredString(),
            ["startedOn"] = search.StartedOn,
            ["currentBoxel"] = search.Current?.ToStoredString(),
            ["currentCount"] = search.CurrentCount,
            ["lowMassCode"] = search.LowMassCode.ToString(),
            ["completedPrefixes"] = WriteStringArray(search.CompletedPrefixes),
            ["completedSystems"] = WriteStringArray(search.CompletedSystems),
            ["emptySystems"] = WriteStringArray(search.EmptySystems),
            ["deferredSystems"] = WriteStringArray(search.DeferredSystems),
            ["deferredRanges"] = BoxelDeferredRangeJson.Write(search.DeferredRanges),
            ["progress"] = WriteProgress(search.ProgressByPrefix),
            ["autoCopy"] = search.AutoCopy,
            ["sortDescending"] = search.SortDescending,
            ["collapsed"] = search.Collapsed,
            ["skipAlreadyVisited"] = search.SkipAlreadyVisited,
            ["skipKnownToSpansh"] = search.SkipKnownToSpansh,
            ["completeOnFssAllBodies"] =
                search.CompletionMode == BoxelCompletionMode.FssAllBodies
        };
    }

    private string GetCommanderDirectory(string frontierId)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        return Path.Combine(rootDirectory, frontierId);
    }

    private string GetPath(string frontierId, string fileName)
    {
        ValidateFileName(fileName, nameof(fileName));
        if (!string.Equals(
                Path.GetExtension(fileName),
                ".json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The saved boxel search file must be JSON.",
                nameof(fileName));
        }

        return Path.Combine(GetCommanderDirectory(frontierId), fileName);
    }

    private static void ValidateFileName(string value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value cannot be an empty string or composed entirely of whitespace.",
                parameterName);
        }

        if (!string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The file name is invalid.", parameterName);
        }
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 120)
        {
            throw new ArgumentException(
                "The saved search name must be 120 characters or fewer.",
                nameof(name));
        }

        return normalized;
    }

    private static string CreateFileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = name
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray();
        var stem = new string(characters).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "boxel-search";
        }

        return stem.Length <= 60 ? stem : stem[..60].TrimEnd();
    }

    private static string? NormalizeNotes(string? notes)
    {
        return string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private static JsonArray WriteStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            array.Add(value);
        }

        return array;
    }

    private static string[] ReadStringArray(
        JsonObject root,
        string propertyName)
    {
        return root[propertyName] is JsonArray array
            ? array
                .Select(node => node is JsonValue value
                    && value.TryGetValue<string>(out var text)
                        ? text
                        : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private static JsonObject WriteProgress(
        IReadOnlyDictionary<string, int> progress)
    {
        var node = new JsonObject();
        foreach (var entry in progress.OrderBy(
                     entry => entry.Key,
                     StringComparer.Ordinal))
        {
            node[entry.Key] = entry.Value;
        }

        return node;
    }

    private static Dictionary<string, int> ReadProgress(JsonObject root)
    {
        if (root["progress"] is not JsonObject progress)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in progress)
        {
            if (entry.Value is JsonValue value
                && value.TryGetValue<int>(out var count))
            {
                result[entry.Key] = count;
            }
        }

        return result;
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? result
                : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonObject root,
        string propertyName)
    {
        var text = GetString(root, propertyName);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var result)
                ? result
                : null;
    }
}

public sealed record SavedBoxelSearchDocument(
    string FrontierId,
    string Name,
    string? Notes,
    bool IsFavorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BoxelSearchSnapshot Search,
    string FileName,
    string FilePath);

public sealed record SavedBoxelSearchCatalogEntry(
    string Name,
    string? Notes,
    bool IsFavorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int CompletedSystems,
    int TotalSystems,
    bool HasUncountedBoxels,
    string FileName,
    string FilePath,
    string? TopBoxelPrefix = null,
    char LowMassCode = 'c',
    IReadOnlyList<string>? MatchingPrefixes = null)
{
    public IReadOnlyList<string> Prefixes => MatchingPrefixes
        ?? (string.IsNullOrWhiteSpace(TopBoxelPrefix) ? [] : [TopBoxelPrefix]);
}
