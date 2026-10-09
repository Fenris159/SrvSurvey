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

        string applicationsDirectory = LinuxDesktopEntryRegistration.ResolveApplicationsDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME")
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
        string desktopFile = await LinuxDesktopEntryRegistration
            .WriteAsync(
                applicationsDirectory,
                executable,
                $"x-scheme-handler/{FrontierOAuthCallback.Scheme}",
                cancellationToken
            )
            .ConfigureAwait(false);
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

    /// <summary>Locates a desktop registration tool in supported Linux paths, retaining the standard path as fallback.</summary>
    private static string ResolveLinuxDesktopToolPath(string name)
    {
        return LinuxDesktopToolDirectories
                .Select(directory => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists)
            ?? Path.Combine(LinuxDesktopToolDirectories[0], name);
    }
}
