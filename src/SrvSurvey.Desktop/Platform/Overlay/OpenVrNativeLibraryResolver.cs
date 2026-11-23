using System.Reflection;
using System.Runtime.InteropServices;
using Valve.VR;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OpenVrNativeLibraryResolver
{
    public const string LibraryEnvironmentVariable = "SRVSURVEY_OPENVR_LIBRARY";
    private const string LinuxLibraryFileName = "libopenvr_api.so";
    private const string OpenVrApiLibraryName = "openvr_api";
    private static readonly Lock RegistrationLock = new();
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

            NativeLibrary.SetDllImportResolver(typeof(OpenVR).Assembly, ResolveLibrary);
            registered = true;
        }
    }

    public static IReadOnlyList<string> GetLinuxCandidates()
    {
        var candidates = new List<string>();
        string? configured = Environment.GetEnvironmentVariable(LibraryEnvironmentVariable);
        if (TryResolveConfiguredPath(configured, out string resolvedConfiguredPath))
        {
            candidates.Add(resolvedConfiguredPath);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, LinuxLibraryFileName));
        string runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(runtimeIdentifier))
        {
            candidates.Add(
                Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeIdentifier, "native", LinuxLibraryFileName)
            );
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            candidates.Add(
                Path.Combine(
                    profile,
                    ".steam",
                    "steam",
                    "steamapps",
                    "common",
                    "SteamVR",
                    "bin",
                    "linux64",
                    LinuxLibraryFileName
                )
            );
            candidates.Add(
                Path.Combine(
                    profile,
                    ".local",
                    "share",
                    "Steam",
                    "steamapps",
                    "common",
                    "SteamVR",
                    "bin",
                    "linux64",
                    LinuxLibraryFileName
                )
            );
            candidates.Add(
                Path.Combine(
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
                    LinuxLibraryFileName
                )
            );
        }

        return candidates.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string? FindExistingLinuxLibrary()
    {
        return OperatingSystem.IsLinux() ? GetLinuxCandidates().FirstOrDefault(File.Exists) : null;
    }

    internal static bool TryResolveConfiguredPath(string? configured, out string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            resolvedPath = string.Empty;
            return false;
        }

        if (configured.StartsWith('/') && Path.DirectorySeparatorChar != '/' && !configured.Contains('\0'))
        {
            resolvedPath = configured;
            return true;
        }

        try
        {
            resolvedPath = Path.GetFullPath(configured);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Leave invalid user input out of the resolver candidates.
            resolvedPath = string.Empty;
            return false;
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (
            !string.Equals(libraryName, OpenVrApiLibraryName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(libraryName, LinuxLibraryFileName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return nint.Zero;
        }

        foreach (string candidate in GetLinuxCandidates())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle))
            {
                return handle;
            }
        }

        return nint.Zero;
    }
}
