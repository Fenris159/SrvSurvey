using System.Reflection;
using System.Runtime.InteropServices;
using Valve.VR;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OpenVrNativeLibraryResolver
{
    public const string LibraryEnvironmentVariable =
        "SRVSURVEY_OPENVR_LIBRARY";
    private static readonly object RegistrationLock = new();
    private static bool registered;

    public static void Register()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (registered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(
                typeof(OpenVR).Assembly,
                ResolveLibrary);
            registered = true;
        }
    }

    public static IReadOnlyList<string> GetLinuxCandidates()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable(
            LibraryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                candidates.Add(Path.GetFullPath(configured));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                // Leave invalid user input out of the resolver candidates.
            }
        }

        candidates.Add(Path.Combine(
            AppContext.BaseDirectory,
            "libopenvr_api.so"));
        var profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            candidates.Add(Path.Combine(
                profile,
                ".steam",
                "steam",
                "steamapps",
                "common",
                "SteamVR",
                "bin",
                "linux64",
                "libopenvr_api.so"));
            candidates.Add(Path.Combine(
                profile,
                ".local",
                "share",
                "Steam",
                "steamapps",
                "common",
                "SteamVR",
                "bin",
                "linux64",
                "libopenvr_api.so"));
            candidates.Add(Path.Combine(
                profile,
                ".var",
                "app",
                "com.valvesoftware.Steam",
                ".local",
                "share",
                "Steam",
                "steamapps",
                "common",
                "SteamVR",
                "bin",
                "linux64",
                "libopenvr_api.so"));
        }

        return candidates.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static nint ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(
                libraryName,
                "openvr_api",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                libraryName,
                "libopenvr_api.so",
                StringComparison.OrdinalIgnoreCase))
        {
            return nint.Zero;
        }

        foreach (var candidate in GetLinuxCandidates())
        {
            if (File.Exists(candidate)
                && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return nint.Zero;
    }
}
