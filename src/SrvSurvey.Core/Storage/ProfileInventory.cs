using System.Security.Cryptography;

namespace SrvSurvey.Core.Storage;

public sealed record ProfileInventory(
    string RootPath,
    IReadOnlyList<string> RelativeDirectories,
    IReadOnlyList<ProfileInventoryEntry> Entries)
{
    public static async Task<ProfileInventory> CreateAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"The legacy profile directory does not exist: {root}");
        }

        RejectReparsePoint(new DirectoryInfo(root));

        var directories = new List<string>();
        var files = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pendingDirectories.Pop();

            foreach (var directory in Directory.EnumerateDirectories(current).Order())
            {
                var directoryInfo = new DirectoryInfo(directory);
                RejectReparsePoint(directoryInfo);
                directories.Add(NormalizeRelativePath(root, directory));
                pendingDirectories.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current).Order())
            {
                var fileInfo = new FileInfo(file);
                RejectReparsePoint(fileInfo);
                files.Add(file);
            }
        }

        directories.Sort(StringComparer.Ordinal);
        files.Sort(StringComparer.Ordinal);

        var entries = new List<ProfileInventoryEntry>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(file);
            var hash = await ComputeSha256Async(file, cancellationToken)
                .ConfigureAwait(false);
            fileInfo.Refresh();
            entries.Add(new ProfileInventoryEntry(
                NormalizeRelativePath(root, file),
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                hash));
        }

        return new ProfileInventory(root, directories, entries);
    }

    internal static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    internal static string ResolveEntryPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                $"The profile contains an invalid relative path: {relativePath}");
        }

        var platformRelativePath = relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var resolvedRoot = Path.GetFullPath(rootPath);
        var resolvedPath = Path.GetFullPath(
            Path.Combine(resolvedRoot, platformRelativePath));
        var rootPrefix = Path.EndsInDirectorySeparator(resolvedRoot)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!resolvedPath.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(
                $"The profile path escapes its root directory: {relativePath}");
        }

        return resolvedPath;
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void RejectReparsePoint(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Profile import does not follow symbolic links or junctions: {info.FullName}");
        }
    }
}

public sealed record ProfileInventoryEntry(
    string RelativePath,
    long Length,
    DateTime LastWriteTimeUtc,
    string Sha256);
