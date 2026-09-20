namespace SrvSurvey.Core.Updates;

public static class AppImageRuntimeResolver
{
    public static string? ResolveCurrentPath()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        return Resolve(
            Environment.GetEnvironmentVariable("APPIMAGE"),
            Environment.GetEnvironmentVariable("APPDIR"),
            AppContext.BaseDirectory
        );
    }

    internal static string? Resolve(string? appImagePath, string? appDirPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appImagePath) || string.IsNullOrWhiteSpace(appDirPath))
        {
            return null;
        }

        try
        {
            string? canonicalAppDir = ResolveDirectory(appDirPath);
            string? canonicalBaseDirectory = ResolveDirectory(baseDirectory);
            if (
                canonicalAppDir is null
                || canonicalBaseDirectory is null
                || !IsSameOrChildPath(canonicalBaseDirectory, canonicalAppDir)
            )
            {
                return null;
            }

            string canonicalAppImage = AppImageReleaseInstallationPreparer.ResolveInstallationPath(appImagePath);
            using var stream = new FileStream(
                canonicalAppImage,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                AppImageReleaseInstallationPreparer.AppImageHeaderLength,
                FileOptions.SequentialScan
            );
            Span<byte> header = stackalloc byte[AppImageReleaseInstallationPreparer.AppImageHeaderLength];
            int headerBytes = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            return AppImageReleaseInstallationPreparer.HasSupportedAppImageHeader(header[..headerBytes])
                ? canonicalAppImage
                : null;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return null;
        }
    }

    private static string? ResolveDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var info = new DirectoryInfo(fullPath);
        info.Refresh();
        if (!info.Exists)
        {
            return null;
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return Path.TrimEndingDirectorySeparator(fullPath);
        }

        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        if (target is not DirectoryInfo targetDirectory || !targetDirectory.Exists)
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory.FullName));
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(path, root, comparison)
            || path.StartsWith(
                Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                comparison
            );
    }
}
