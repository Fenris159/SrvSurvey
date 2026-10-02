using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SrvSurvey.Desktop.Platform.Frontier;

public static class FrontierProtocolRegistration
{
    private static readonly string[] XdgMimePaths =
    [
        "/usr/bin/xdg-mime",
        "/bin/xdg-mime",
        "/usr/local/bin/xdg-mime",
        "/run/current-system/sw/bin/xdg-mime",
    ];

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
            FileName = ResolveXdgMimePath(),
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

    /// <summary>Reuses the application launcher for callback links and removes the legacy duplicate entry.</summary>
    internal static async Task<string> WriteLinuxDesktopFileAsync(
        string applicationsDirectory,
        string executable,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(applicationsDirectory);
        string desktopFile = Path.Combine(applicationsDirectory, "io.github.fenris159.SrvSurvey.desktop");
        string escapedExecutable = executable
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
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
        UpdateDesktopEntry(lines, "Exec", _ => $"\"{escapedExecutable}\" %u");
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
            line => line.StartsWith(prefix, StringComparison.Ordinal)
        );
        string value = prefix + valueFactory(index < 0 ? null : lines[index][prefix.Length..]);
        if (index < 0)
        {
            lines.Insert(end, value);
        }
        else
        {
            lines[index] = value;
        }
    }

    private static string ResolveXdgMimePath()
    {
        return XdgMimePaths.FirstOrDefault(File.Exists) ?? XdgMimePaths[0];
    }
}
