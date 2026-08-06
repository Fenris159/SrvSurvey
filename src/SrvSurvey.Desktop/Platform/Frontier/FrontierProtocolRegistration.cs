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

    public static async Task RegisterCurrentAsync(
        CancellationToken cancellationToken = default)
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
            "Frontier account linking is currently supported on Windows and Linux.");
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "SrvSurvey could not determine its executable path for Frontier authorization.");
        using var scheme = Registry.CurrentUser.CreateSubKey(
            $"Software\\Classes\\{FrontierOAuthCallback.Scheme}",
            writable: true)
            ?? throw new InvalidOperationException(
                "SrvSurvey could not register its Frontier callback protocol.");
        scheme.SetValue(null, "URL:SrvSurvey Frontier authorization");
        scheme.SetValue("URL Protocol", string.Empty);
        using var command = scheme.CreateSubKey("shell\\open\\command", writable: true)
            ?? throw new InvalidOperationException(
                "SrvSurvey could not register its Frontier callback command.");
        command.SetValue(null, $"\"{executable}\" \"%1\"");
    }

    private static async Task RegisterLinuxAsync(CancellationToken cancellationToken)
    {
        var executable = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = Environment.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "SrvSurvey could not determine its executable path for Frontier authorization.");
        }

        var applicationsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "applications");
        Directory.CreateDirectory(applicationsDirectory);
        var desktopFile = Path.Combine(
            applicationsDirectory,
            "io.github.fenris159.SrvSurvey.frontier-auth.desktop");
        var escapedExecutable = executable.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var content = string.Join(
            '\n',
            "[Desktop Entry]",
            "Type=Application",
            "Name=SrvSurvey",
            "Comment=Handle SrvSurvey Frontier authorization",
            $"Exec=\"{escapedExecutable}\" %u",
            "Icon=srvsurvey",
            "Terminal=false",
            "NoDisplay=true",
            $"MimeType=x-scheme-handler/{FrontierOAuthCallback.Scheme};",
            string.Empty);
        await File.WriteAllTextAsync(
                desktopFile,
                content,
                cancellationToken)
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
        startInfo.ArgumentList.Add(
            $"x-scheme-handler/{FrontierOAuthCallback.Scheme}");
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "The xdg-mime protocol registration process did not start.");
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "SrvSurvey could not register its Frontier callback protocol. "
                    + error.Trim());
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "SrvSurvey requires xdg-mime to register Frontier authorization on Linux.",
                exception);
        }
    }

    private static string ResolveXdgMimePath()
    {
        return XdgMimePaths.FirstOrDefault(File.Exists)
            ?? XdgMimePaths[0];
    }
}
