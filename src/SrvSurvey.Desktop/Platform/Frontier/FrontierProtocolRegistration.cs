using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SrvSurvey.Desktop.Platform.Frontier;

public static class FrontierProtocolRegistration
{
    private static readonly string[] LinuxDesktopToolDirectories =
    [
        "/usr/bin",
        "/bin",
        "/usr/local/bin",
        "/run/current-system/sw/bin",
    ];

    /// <summary>Registers this installation as the current user's Frontier authorization callback handler.</summary>
    public static async Task RegisterCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            RegisterWindows();
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RegisterLinuxAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new PlatformNotSupportedException(
            "Frontier account linking is currently supported on Windows and Linux."
        );
    }

    /// <summary>Associates Frontier callback links with this executable in the current user's Windows registry.</summary>
    [SupportedOSPlatform("windows")]
    private static void RegisterWindows()
    {
        string executable =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "SrvSurvey could not determine its executable path for Frontier authorization."
            );
        using RegistryKey scheme =
            Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{FrontierOAuthCallback.Scheme}", writable: true)
            ?? throw new InvalidOperationException("SrvSurvey could not register its Frontier callback protocol.");
        scheme.SetValue(null, "URL:SrvSurvey Frontier authorization");
        scheme.SetValue("URL Protocol", string.Empty);
        using RegistryKey command =
            scheme.CreateSubKey("shell\\open\\command", writable: true)
            ?? throw new InvalidOperationException("SrvSurvey could not register its Frontier callback command.");
        command.SetValue(null, $"\"{executable}\" \"%1\"");
    }

    /// <summary>Registers the current Linux installation as the Frontier callback handler.</summary>
    private static async Task RegisterLinuxAsync(CancellationToken cancellationToken)
    {
        string? executable = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = Environment.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "SrvSurvey could not determine its executable path for Frontier authorization."
            );
        }

        string applicationsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "applications"
        );
        string desktopFile = await WriteLinuxDesktopFileAsync(applicationsDirectory, executable, cancellationToken)
            .ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveLinuxDesktopToolPath("xdg-mime"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("default");
        startInfo.ArgumentList.Add(Path.GetFileName(desktopFile));
        startInfo.ArgumentList.Add($"x-scheme-handler/{FrontierOAuthCallback.Scheme}");
        try
        {
            using Process process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("The xdg-mime protocol registration process did not start.");
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "SrvSurvey could not register its Frontier callback protocol. " + error.Trim()
                );
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "SrvSurvey requires xdg-mime to register Frontier authorization on Linux.",
                exception
            );
        }
    }

    /// <summary>Reuses the application launcher, removes the legacy duplicate, and refreshes Linux MIME discovery.</summary>
    internal static async Task<string> WriteLinuxDesktopFileAsync(
        string applicationsDirectory,
        string executable,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(applicationsDirectory);
        string desktopFile = Path.Combine(applicationsDirectory, "io.github.fenris159.SrvSurvey.desktop");
        // Desktop strings are unescaped before Exec arguments, so quoted path characters need both layers.
        string escapedExecutable = executable
            .Replace("\\", "\\\\\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\\\"", StringComparison.Ordinal)
            .Replace("$", "\\\\$", StringComparison.Ordinal)
            .Replace("`", "\\\\`", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        List<string> lines = File.Exists(desktopFile)
            ? [.. await File.ReadAllLinesAsync(desktopFile, cancellationToken).ConfigureAwait(false)]
            :
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=SrvSurvey",
                "Comment=Handle SrvSurvey Frontier authorization",
                "Icon=srvsurvey",
                "Terminal=false",
                "NoDisplay=true",
            ];
        // GIO validates argv[0] before expanding %% escapes; env keeps percent paths in an argument.
        string commandPrefix = executable.Contains('%') ? "/usr/bin/env " : string.Empty;
        UpdateDesktopEntry(lines, "Exec", _ => commandPrefix + $"\"{escapedExecutable}\" %u");
        UpdateDesktopEntry(lines, "TryExec", _ => executable.Replace("\\", "\\\\", StringComparison.Ordinal));
        UpdateDesktopEntry(
            lines,
            "MimeType",
            current =>
                string.Join(
                    ';',
                    (current ?? string.Empty)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Append($"x-scheme-handler/{FrontierOAuthCallback.Scheme}")
                        .Distinct(StringComparer.Ordinal)
                ) + ";"
        );
        await File.WriteAllLinesAsync(desktopFile, lines, cancellationToken).ConfigureAwait(false);
        File.Delete(Path.Combine(applicationsDirectory, "io.github.fenris159.SrvSurvey.frontier-auth.desktop"));
        await RefreshLinuxDesktopDatabaseAsync(applicationsDirectory, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return desktopFile;
    }

    /// <summary>Rebuilds desktop MIME associations so removed launchers cannot hide the current callback handler.</summary>
    internal static async Task RefreshLinuxDesktopDatabaseAsync(
        string applicationsDirectory,
        string? updaterPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath ?? ResolveLinuxDesktopToolPath("update-desktop-database"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(applicationsDirectory);
        try
        {
            using Process process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("The desktop MIME database update process did not start.");
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "SrvSurvey could not refresh its desktop callback registration. " + error.Trim()
                );
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "SrvSurvey requires update-desktop-database (desktop-file-utils) to register Frontier authorization on Linux.",
                exception
            );
        }
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

    /// <summary>Locates a desktop registration tool in supported Linux paths, retaining the standard path as fallback.</summary>
    private static string ResolveLinuxDesktopToolPath(string name)
    {
        return LinuxDesktopToolDirectories
                .Select(directory => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists)
            ?? Path.Combine(LinuxDesktopToolDirectories[0], name);
    }
}
