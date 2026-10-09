namespace SrvSurvey.Desktop.Platform;

/// <summary>Provides the canonical host application identity without requiring Frontier account linking.</summary>
internal static class LinuxDesktopEntryRegistration
{
    internal const string ApplicationId = "io.github.fenris159.SrvSurvey";
    internal const string DesktopFileName = ApplicationId + ".desktop";
    internal const string ManagedEntryMarker = "X-SrvSurvey-Managed=true";
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    /// <summary>Registers the stable AppImage path or executable where the user's desktop portal discovers applications.</summary>
    public static Task EnsureCurrentAsync(CancellationToken token) =>
        EnsureAsync(
            ResolveApplicationsDirectory(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ),
            ResolveExecutable(Environment.GetEnvironmentVariable("APPIMAGE"), Environment.ProcessPath),
            (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share")
                .Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Where(Path.IsPathFullyQualified)
                .Select(directory => Path.Combine(directory, "applications")),
            token
        );

    /// <summary>Honors the XDG user-data location while ignoring invalid relative overrides.</summary>
    internal static string ResolveApplicationsDirectory(string userProfile, string? dataHome) =>
        Path.Combine(
            !string.IsNullOrWhiteSpace(dataHome) && Path.IsPathFullyQualified(dataHome)
                ? dataHome
                : Path.Combine(userProfile, ".local", "share"),
            "applications"
        );

    /// <summary>A launcher must survive the temporary AppImage mount and reject multiline desktop-entry values.</summary>
    internal static string ResolveExecutable(string? appImage, string? processPath)
    {
        string? executable = string.IsNullOrWhiteSpace(appImage) ? processPath : appImage;
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('\r') || executable.Contains('\n'))
        {
            throw new InvalidDataException("SrvSurvey could not determine a usable desktop launcher path.");
        }
        return executable;
    }

    /// <summary>Reuses installed system launchers; otherwise maintains one hidden user entry without claiming MIME defaults.</summary>
    internal static async Task EnsureAsync(
        string applicationsDirectory,
        string executable,
        IEnumerable<string> systemApplicationsDirectories,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        string desktopFile = Path.Combine(applicationsDirectory, DesktopFileName);
        if (File.Exists(desktopFile))
        {
            string[] entry = await File.ReadAllLinesAsync(desktopFile, token).ConfigureAwait(false);
            if (!entry.Contains(ManagedEntryMarker, StringComparer.Ordinal))
            {
                return;
            }
        }
        else if (systemApplicationsDirectories.Any(directory => File.Exists(Path.Combine(directory, DesktopFileName))))
        {
            return;
        }
        await WriteAsync(applicationsDirectory, executable, cancellationToken: token).ConfigureAwait(false);
    }

    /// <summary>Serializes launcher updates so portal registration and account linking preserve each other's metadata.</summary>
    internal static async Task<string> WriteAsync(
        string applicationsDirectory,
        string executable,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        await WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await WriteCoreAsync(applicationsDirectory, executable, mimeType, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    /// <summary>Preserves visibility, icons, desktop actions and existing MIME types when replacing launch paths atomically.</summary>
    private static async Task<string> WriteCoreAsync(
        string applicationsDirectory,
        string executable,
        string? mimeType,
        CancellationToken cancellationToken
    )
    {
        executable = ResolveExecutable(null, executable);
        Directory.CreateDirectory(applicationsDirectory);
        string desktopFile = Path.Combine(applicationsDirectory, DesktopFileName);
        // Desktop strings are unescaped before Exec arguments, so quoted path characters need both layers.
        string escapedExecutable = executable
            .Replace("\\", "\\\\\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\\\"", StringComparison.Ordinal)
            .Replace("$", "\\\\$", StringComparison.Ordinal)
            .Replace("`", "\\\\`", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        string[]? existing = File.Exists(desktopFile)
            ? await File.ReadAllLinesAsync(desktopFile, cancellationToken).ConfigureAwait(false)
            : null;
        List<string> lines = existing is not null
            ? [.. existing]
            :
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=SrvSurvey",
                "Comment=Exploration and surface-survey companion for Elite Dangerous",
                "Icon=srvsurvey",
                "Terminal=false",
                "NoDisplay=true",
                ManagedEntryMarker,
            ];
        // GIO validates argv[0] before expanding %% escapes; env keeps percent paths in an argument.
        string commandPrefix = executable.Contains('%') ? "/usr/bin/env " : string.Empty;
        UpdateDesktopEntry(lines, "Exec", _ => commandPrefix + $"\"{escapedExecutable}\" %u");
        UpdateDesktopEntry(lines, "TryExec", _ => executable.Replace("\\", "\\\\", StringComparison.Ordinal));
        if (mimeType is not null)
        {
            UpdateDesktopEntry(
                lines,
                "MimeType",
                current =>
                    string.Join(
                        ';',
                        (current ?? string.Empty)
                            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Append(mimeType)
                            .Distinct(StringComparer.Ordinal)
                    ) + ";"
            );
        }
        if (existing is not null && existing.SequenceEqual(lines))
        {
            return desktopFile;
        }
        string temporaryPath = desktopFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, lines, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, desktopFile, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
        return desktopFile;
    }

    /// <summary>Updates one main desktop-entry key while preserving launcher metadata and action sections.</summary>
    private static void UpdateDesktopEntry(List<string> lines, string key, Func<string?, string> valueFactory)
    {
        int start = lines.FindIndex(line => line.Trim().Equals("[Desktop Entry]", StringComparison.Ordinal));
        if (start < 0)
        {
            throw new InvalidDataException("The SrvSurvey desktop launcher has no Desktop Entry section.");
        }

        int end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('['))
        {
            end++;
        }

        string prefix = key + "=";
        int index = lines.FindIndex(
            start + 1,
            end - start - 1,
            line =>
            {
                int delimiter = line.IndexOf('=');
                return delimiter >= 0 && line[..delimiter].Trim().Equals(key, StringComparison.Ordinal);
            }
        );
        string? currentValue = index < 0 ? null : lines[index][(lines[index].IndexOf('=') + 1)..].TrimStart();
        string value = prefix + valueFactory(currentValue);
        if (index < 0)
        {
            lines.Insert(end, value);
        }
        else
        {
            lines[index] = value;
        }
    }
}
