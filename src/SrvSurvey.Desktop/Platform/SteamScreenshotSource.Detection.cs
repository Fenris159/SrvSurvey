using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SrvSurvey.Desktop.Platform;

public sealed partial class SteamScreenshotSource
{
    private const string SteamDirectoryName = "Steam";

    internal string? FindFolder(string? journalDirectory)
    {
        foreach (string root in GetRoots(journalDirectory).Distinct(PathComparer))
        {
            string users = Path.Combine(root, "userdata");
            if (!Directory.Exists(users))
            {
                continue;
            }
            try
            {
                string? account = FindAccount(root, users);
                return account is null
                    ? null
                    : Path.Combine(users, account, "760", "remote", EliteAppId, "screenshots");
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                            or ArgumentException
                )
            {
                // Do not fall through to a different account when Steam's ownership evidence is unreadable.
                return null;
            }
        }
        return null;
    }

    private static string? FindAccount(string root, string users)
    {
        string loginFile = Path.Combine(root, "config", "loginusers.vdf");
        if (File.Exists(loginFile))
        {
            SteamKeyValues? accounts = SteamKeyValues.Read(loginFile).Children.GetValueOrDefault("users");
            string[] active = (accounts?.Children.AsEnumerable() ?? [])
                .Where(pair => pair.Value.GetValue("MostRecent") == "1")
                .Select(pair => pair.Key)
                .ToArray();
            if (active.Length == 1 && ulong.TryParse(active[0], CultureInfo.InvariantCulture, out ulong steamId))
            {
                return (steamId & uint.MaxValue).ToString(CultureInfo.InvariantCulture);
            }
            if (active.Length > 0)
            {
                return null;
            }
        }
        string[] directories = Directory
            .EnumerateDirectories(users)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => uint.TryParse(name, CultureInfo.InvariantCulture, out uint id) && id > 0)
            .ToArray();
        return directories.Length == 1 ? directories[0] : null;
    }

    private IEnumerable<string> GetRoots(string? journalDirectory)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(journalDirectory) && Path.IsPathFullyQualified(journalDirectory))
        {
            for (DirectoryInfo? part = new(journalDirectory); part?.Parent is not null; part = part.Parent)
            {
                if (part.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(part.Parent.FullName);
                    break;
                }
            }
        }
        if (steamRoots is not null)
        {
            return roots.Concat(steamRoots);
        }
        string? configuredRoot = Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && Path.IsPathFullyQualified(configuredRoot))
        {
            roots.Add(configuredRoot);
        }
        if (OperatingSystem.IsWindows())
        {
            string? installed = ReadWindowsSteamRoot();
            if (installed is not null)
            {
                roots.Add(installed);
            }
            roots.Add(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), SteamDirectoryName)
            );
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.AddRange([
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", SteamDirectoryName),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", SteamDirectoryName),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", SteamDirectoryName),
            ]);
        }
        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsSteamRoot()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") is string path && Path.IsPathFullyQualified(path) ? path : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
