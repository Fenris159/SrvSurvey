using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SrvSurvey.Desktop.Platform;

public sealed partial class SteamScreenshotSource
{
    private const string SteamDirectoryName = "Steam";
    private const string SteamUserDataDirectoryName = "userdata";

    internal string? FindFolder(string? journalDirectory)
    {
        try
        {
            string? root = SelectClientRoot(journalDirectory);
            if (root is null)
            {
                return null;
            }
            string users = Path.Combine(root, SteamUserDataDirectoryName);
            string? account = FindAccount(root, users);
            return account is null ? null : Path.Combine(users, account, "760", "remote", EliteAppId, "screenshots");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            // Unreadable ownership evidence must not select another client's or account's images.
            return null;
        }
    }

    private string? SelectClientRoot(string? journalDirectory)
    {
        string? library = FindJournalLibrary(journalDirectory);
        if (library is not null && Directory.Exists(Path.Combine(library, SteamUserDataDirectoryName)))
        {
            return library;
        }
        string? configured = steamRoots is null
            ? Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH")
            : null;
        if (
            !string.IsNullOrWhiteSpace(configured)
            && Path.IsPathFullyQualified(configured)
            && Directory.Exists(Path.Combine(configured, SteamUserDataDirectoryName))
        )
        {
            return Path.GetFullPath(configured);
        }
        string[] clients = GetRoots()
            .Where(root => Directory.Exists(Path.Combine(root, SteamUserDataDirectoryName)))
            .Select(ResolvePhysicalFolder)
            .Distinct(PathComparer)
            .ToArray();
        if (library is not null)
        {
            string[] owners = clients.Where(root => OwnsLibrary(root, library)).ToArray();
            return owners.Length == 1 ? owners[0] : null;
        }
        return clients.Length == 1 ? clients[0] : null;
    }

    private static bool OwnsLibrary(string root, string library)
    {
        string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        SteamKeyValues? libraries;
        try
        {
            libraries = SteamKeyValues.Read(file).Children.GetValueOrDefault("libraryfolders");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        if (libraries is null || libraries.Value is not null)
        {
            throw new InvalidDataException("The Steam library ownership index is invalid.");
        }
        return libraries.Children.Values.Any(item =>
        {
            string? path = item.GetValue("path") ?? item.Value;
            return path is not null
                && Path.IsPathFullyQualified(path)
                && Directory.Exists(path)
                && PathComparer.Equals(ResolvePhysicalFolder(path), ResolvePhysicalFolder(library));
        });
    }

    private static string? FindJournalLibrary(string? journalDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory) || !Path.IsPathFullyQualified(journalDirectory))
        {
            return null;
        }
        for (DirectoryInfo? part = new(journalDirectory); part?.Parent is not null; part = part.Parent)
        {
            if (part.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return part.Parent.FullName;
            }
        }
        return null;
    }

    private static string? FindAccount(string root, string users)
    {
        string loginFile = Path.Combine(root, "config", "loginusers.vdf");
        SteamKeyValues? accounts;
        try
        {
            accounts = SteamKeyValues.Read(loginFile).Children.GetValueOrDefault("users");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return FindSoleAccount(users);
        }
        string[] active = (accounts?.Children.AsEnumerable() ?? [])
            .Where(pair => pair.Value.GetValue("MostRecent") == "1")
            .Select(pair => pair.Key)
            .ToArray();
        return active.Length == 1 && ulong.TryParse(active[0], CultureInfo.InvariantCulture, out ulong steamId)
            ? (steamId & uint.MaxValue).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? FindSoleAccount(string users)
    {
        string[] directories = Directory
            .EnumerateDirectories(users)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => uint.TryParse(name, CultureInfo.InvariantCulture, out uint id) && id > 0)
            .ToArray();
        return directories.Length == 1 ? directories[0] : null;
    }

    private IEnumerable<string> GetRoots()
    {
        var roots = new List<string>();
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
