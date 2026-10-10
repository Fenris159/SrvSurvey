using System.Runtime.InteropServices;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed partial record GamescopeOverlaySession(string Display, GamescopeDisplayIdentity Identity)
{
    public static GamescopeOverlaySession? Current { get; private set; }
    public static string? UnavailableReason { get; private set; }

    /// <summary>Routes Avalonia before platform initialization, independently of how Steam launched the companion.</summary>
    public static void InitializeCurrent(Action<string> log)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        string? original = Environment.GetEnvironmentVariable("DISPLAY");
        (Current, UnavailableReason) = Initialize(
            log,
            original,
            () => Resolve(original, ReadSteamDisplays("/proc"), Probe),
            display => SetProcessEnvironment("DISPLAY", display)
        );
    }

    /// <summary>Reports startup routing or a failure without changing the display on ordinary desktops.</summary>
    internal static (GamescopeOverlaySession? Session, string? Error) Initialize(
        Action<string> log,
        string? original,
        Func<(GamescopeOverlaySession? Session, string? Error)> resolve,
        Func<string, bool> route
    )
    {
        try
        {
            (GamescopeOverlaySession? session, string? error) = resolve();
            if (session is not null)
            {
                if (!route(session.Display))
                {
                    const string failure = "Gamescope display routing failed; overlays are unavailable.";
                    log(failure);
                    return (null, failure);
                }
                log($"Gamescope overlay display: {session.Display} (server 0); inherited display: {original}.");
                log(
                    "Gamescope overlays use an external canvas. Turn off the performance HUD; live pointer interaction yields to Steam menus and keeps Elite keyboard input."
                );
            }
            else if (error is not null)
            {
                log(error);
            }
            return (session, error);
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            log("Gamescope display probing is unavailable: " + exception.Message);
            return (null, null);
        }
    }

    /// <summary>Updates both libc (used by Xlib) and .NET before Avalonia initializes its native connection.</summary>
    internal static bool SetProcessEnvironment(string name, string value)
    {
        if (SetNativeEnvironment(name, value, 1) != 0)
        {
            return false;
        }
        Environment.SetEnvironmentVariable(name, value);
        return true;
    }

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetNativeEnvironment(string name, string value, int overwrite);

    internal static GamescopeDisplayIdentity? Probe(string? display)
    {
        using var connection = GamescopeX11Connection.TryOpen(display);
        return connection?.ReadIdentity();
    }

    internal static (GamescopeOverlaySession? Session, string? Error) Resolve(
        string? currentDisplay,
        IEnumerable<string> candidates,
        Func<string?, GamescopeDisplayIdentity?> probe
    )
    {
        GamescopeDisplayIdentity? current = probe(currentDisplay);
        if (current is null)
        {
            return (null, null); // Ordinary desktops retain their own windowing backend.
        }
        if (current.ServerId == 0)
        {
            return (new GamescopeOverlaySession(currentDisplay!, current), null);
        }
        var roots = candidates
            .Distinct(StringComparer.Ordinal)
            .Select(name => new { Name = name, Identity = probe(name) })
            .Where(item =>
                item.Identity is { ServerId: 0 }
                && (current.ProcessId is null || item.Identity.ProcessId == current.ProcessId)
            )
            .ToArray();
        return roots.Length == 1
            ? (new GamescopeOverlaySession(roots[0].Name, roots[0].Identity!), null)
            : (
                null,
                "Gamescope was detected, but its primary overlay display could not be identified uniquely. Overlays are unavailable."
            );
    }

    /// <summary>Reads display candidates only from same-user Steam executables; never logs process environments.</summary>
    internal static IReadOnlyList<string> ReadSteamDisplays(string procDirectory)
    {
        var result = new List<string>();
        try
        {
            string? owner = ReadOwner(Path.Combine(procDirectory, "self"));
            if (owner is null)
            {
                return result;
            }
            foreach (string directory in Directory.EnumerateDirectories(procDirectory))
            {
                try
                {
                    if (
                        ReadOwner(directory) != owner
                        || File.ReadAllText(Path.Combine(directory, "comm")).Trim() != "steam"
                    )
                    {
                        continue;
                    }
                    string executable = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0')[0];
                    if (Path.GetFileName(executable) != "steam")
                    {
                        continue;
                    }
                    string? display = File.ReadAllText(Path.Combine(directory, "environ"))
                        .Split('\0')
                        .FirstOrDefault(entry => entry.StartsWith("DISPLAY=", StringComparison.Ordinal))
                        ?[8..];
                    if (EliteKeyboardDisplayDiscovery.IsLocalDisplay(display))
                    {
                        result.Add(display!);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Steam can exit while its metadata is being inspected.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Restricted procfs cannot supply authenticated display candidates.
        }
        return result;
    }

    private static string? ReadOwner(string directory)
    {
        return File.ReadLines(Path.Combine(directory, "status"))
            .FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))
            ?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ElementAtOrDefault(1);
    }
}
