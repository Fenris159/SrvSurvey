using System.Security.Cryptography;

namespace SrvSurvey.Core.Storage;

public sealed record ProfileInventory(
    string RootPath,
    IReadOnlyList<string> RelativeDirectories,
    IReadOnlyList<ProfileInventoryEntry> Entries
)
{
    /// <summary>Inventories every existing file for verification and current-profile preservation.</summary>
    public static Task<ProfileInventory> CreateAsync(string rootPath, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(rootPath, null, excludeDownloadedImages: false, cancellationToken);

    /// <summary>Inventories incoming data while excluding downloaded images and recording recoverable read failures.</summary>
    internal static Task<ProfileInventory> CreateForImportAsync(
        string rootPath,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    ) => CreateCoreAsync(rootPath, warnings, excludeDownloadedImages: true, cancellationToken);

    /// <summary>Compares directory inventories using the destination platform's path identity rules.</summary>
    internal static bool DirectoryPathsMatch(
        IEnumerable<string> expected,
        IEnumerable<string> actual,
        StringComparer pathComparer
    ) => expected.ToHashSet(pathComparer).SetEquals(actual);

    /// <summary>Builds a hashed inventory, optionally excluding incoming downloadable reference images.</summary>
    private static async Task<ProfileInventory> CreateCoreAsync(
        string rootPath,
        ICollection<ProfileImportWarning>? warnings,
        bool excludeDownloadedImages,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The selected profile directory does not exist: {root}");
        }

        RejectReparsePoint(new DirectoryInfo(root));

        (List<string> directories, List<string> files) = await Task.Run(
                () => EnumerateProfile(root, warnings, excludeDownloadedImages, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);

        var entries = new List<ProfileInventoryEntry>(files.Count);
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(file);
                string hash = await ComputeSha256Async(file, cancellationToken).ConfigureAwait(false);
                fileInfo.Refresh();
                entries.Add(
                    new ProfileInventoryEntry(
                        NormalizeRelativePath(root, file),
                        fileInfo.Length,
                        fileInfo.LastWriteTimeUtc,
                        hash
                    )
                );
            }
            catch (Exception exception) when (warnings is not null && IsRecoverable(exception))
            {
                warnings.Add(new ProfileImportWarning(NormalizeRelativePath(root, file), exception.Message));
            }
        }

        return new ProfileInventory(root, directories, entries);
    }

    /// <summary>Walks profile data while avoiding links and skipping the incoming Codex cache before traversing it.</summary>
    private static (List<string> Directories, List<string> Files) EnumerateProfile(
        string root,
        ICollection<ProfileImportWarning>? warnings,
        bool excludeDownloadedImages,
        CancellationToken cancellationToken
    )
    {
        var directories = new List<string>();
        var files = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pendingDirectories.Pop();

            foreach (string directory in EnumeratePaths(root, current, true, warnings))
            {
                if (
                    excludeDownloadedImages
                    && string.Equals(
                        NormalizeRelativePath(root, directory),
                        "codexImages",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }
                var directoryInfo = new DirectoryInfo(directory);
                if (!AcceptEntry(root, directoryInfo, warnings))
                {
                    continue;
                }
                directories.Add(NormalizeRelativePath(root, directory));
                pendingDirectories.Push(directory);
            }

            foreach (string file in EnumeratePaths(root, current, false, warnings))
            {
                var fileInfo = new FileInfo(file);
                if (AcceptEntry(root, fileInfo, warnings))
                {
                    files.Add(file);
                }
            }
        }

        directories.Sort(StringComparer.Ordinal);
        files.Sort(StringComparer.Ordinal);

        return (directories, files);
    }

    private static string[] EnumeratePaths(
        string root,
        string current,
        bool directories,
        ICollection<ProfileImportWarning>? warnings
    )
    {
        try
        {
            return (
                directories ? Directory.EnumerateDirectories(current) : Directory.EnumerateFiles(current)
            ).ToArray();
        }
        catch (Exception exception) when (warnings is not null && IsRecoverable(exception))
        {
            warnings.Add(new ProfileImportWarning(NormalizeRelativePath(root, current), exception.Message));
            return [];
        }
    }

    private static bool AcceptEntry(string root, FileSystemInfo info, ICollection<ProfileImportWarning>? warnings)
    {
        try
        {
            RejectReparsePoint(info);
            return true;
        }
        catch (Exception exception) when (warnings is not null && IsRecoverable(exception))
        {
            warnings.Add(new ProfileImportWarning(NormalizeRelativePath(root, info.FullName), exception.Message));
            return false;
        }
    }

    internal static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException;

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    internal static string ResolveEntryPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"The profile contains an invalid relative path: {relativePath}");
        }

        string platformRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string resolvedRoot = Path.GetFullPath(rootPath);
        string resolvedPath = Path.GetFullPath(Path.Combine(resolvedRoot, platformRelativePath));
        string rootPrefix = Path.EndsInDirectorySeparator(resolvedRoot)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!resolvedPath.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException($"The profile path escapes its root directory: {relativePath}");
        }

        return resolvedPath;
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void RejectReparsePoint(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Profile import does not follow symbolic links or junctions: {info.FullName}"
            );
        }
    }
}

public sealed record ProfileInventoryEntry(string RelativePath, long Length, DateTime LastWriteTimeUtc, string Sha256);
