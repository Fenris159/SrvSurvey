using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum LinuxOpenXrActiveRuntime
{
    Unknown,
    WiVRn,
    Monado,
    SteamVr,
    Other,
}

/// <summary>Inspects Linux OpenVR and OpenXR environment state without invoking unsafe native hooks.</summary>
public sealed class LinuxVrRuntimeInspector
{
    public const string LinuxOpenXrLibraryEnvironmentVariable = "SRVSURVEY_LINUX_OPENXR_LIBRARY";
    private const string LinuxOpenXrLibraryFileName = "libSrvSurveyLinuxOpenXrOverlay.so";

    private readonly Func<bool> isLinux;
    private readonly Func<string, string?> getEnvironmentVariable;
    private readonly Func<string, string?> readAllText;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, bool> isProcessRunning;
    private readonly Func<string> getUserProfile;

    public static LinuxVrRuntimeInspector Default { get; } = new();

    public LinuxVrRuntimeInspector(
        Func<bool>? isLinux = null,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string, string?>? readAllText = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? isProcessRunning = null,
        Func<string>? getUserProfile = null
    )
    {
        this.isLinux = isLinux ?? OperatingSystem.IsLinux;
        this.getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        this.readAllText = readAllText ?? TryReadAllText;
        this.fileExists = fileExists ?? File.Exists;
        this.isProcessRunning = isProcessRunning ?? IsSystemProcessRunning;
        this.getUserProfile =
            getUserProfile ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public VrRuntimeProbe? CheckOpenVrSafety(bool isCustomRuntime)
    {
        if (!isLinux())
        {
            return null;
        }

        string? shim = DetectConfiguredOpenVrShim();
        if (shim is not null)
        {
            return VrRuntimeProbe.RuntimeUnavailable(
                $"The active OpenVR runtime is configured to use {shim}, which translates VR games to OpenXR and does not support cross-process OpenVR overlays. Connecting through OpenVR can crash active games; choose the WiVRn or Monado (Linux OpenXR) route instead."
            );
        }

        bool vrServerRunning = isProcessRunning("vrserver");
        if (!vrServerRunning && IsWivrnActive())
        {
            return VrRuntimeProbe.RuntimeUnavailable(
                "WiVRn is running without SteamVR. Probing SteamVR/OpenVR can launch SteamVR or crash an active WiVRn game session. Choose the WiVRn (Linux OpenXR, experimental) route instead."
            );
        }

        if (!vrServerRunning && IsMonadoActive())
        {
            return VrRuntimeProbe.RuntimeUnavailable(
                "Monado is running without SteamVR. Probing SteamVR/OpenVR can launch SteamVR or crash an active Monado game session. Choose the Monado (Linux OpenXR, experimental) route instead."
            );
        }

        if (!isCustomRuntime && !vrServerRunning)
        {
            return VrRuntimeProbe.HeadsetUnavailable(
                "Start SteamVR (vrserver) and connect your headset before enabling SteamVR overlays on Linux."
            );
        }

        return null;
    }

    public VrRuntimeProbe? CheckLinuxOpenXrPreflight(string profileId)
    {
        if (!isLinux())
        {
            return VrRuntimeProbe.RuntimeUnavailable("Linux OpenXR overlays require Linux.");
        }

        LinuxOpenXrActiveRuntime activeRuntime = DetectActiveOpenXrRuntime();
        bool wantsWivrn = profileId == VrPlatformProfileCatalog.WivrnOpenXrProfileId;
        string targetName = wantsWivrn ? "WiVRn" : "Monado";
        string manifestHint = wantsWivrn ? "openxr_wivrn.json" : "openxr_monado.json";

        if (activeRuntime == LinuxOpenXrActiveRuntime.SteamVr)
        {
            return VrRuntimeProbe.HeadsetUnavailable(
                $"The active OpenXR runtime is SteamVR, not {targetName}. Connect your headset in {targetName} or point ~/.config/openxr/1/active_runtime.json to {manifestHint}."
            );
        }

        if (wantsWivrn && activeRuntime == LinuxOpenXrActiveRuntime.Monado)
        {
            return VrRuntimeProbe.HeadsetUnavailable(
                $"The active OpenXR runtime is Monado, not {targetName}. Connect your headset in {targetName} or point ~/.config/openxr/1/active_runtime.json to {manifestHint}."
            );
        }

        if (!wantsWivrn && activeRuntime == LinuxOpenXrActiveRuntime.WiVRn)
        {
            return VrRuntimeProbe.HeadsetUnavailable(
                $"The active OpenXR runtime is WiVRn, not {targetName}. Connect your headset in {targetName} or point ~/.config/openxr/1/active_runtime.json to {manifestHint}."
            );
        }

        if (wantsWivrn && !IsWivrnActive() && activeRuntime != LinuxOpenXrActiveRuntime.WiVRn)
        {
            return VrRuntimeProbe.HeadsetUnavailable(
                "Waiting for WiVRn. Start wivrn-server and connect your headset, then confirm WiVRn is the active OpenXR runtime."
            );
        }

        if (!wantsWivrn && !IsMonadoActive() && activeRuntime != LinuxOpenXrActiveRuntime.Monado)
        {
            return VrRuntimeProbe.HeadsetUnavailable(
                "Waiting for Monado. Start monado-service and connect your headset, then confirm Monado is the active OpenXR runtime."
            );
        }

        return null;
    }

    public string? FindOverlayLibrary(string? baseDirectory = null)
    {
        if (!isLinux())
        {
            return null;
        }

        return GetOverlayLibraryCandidates(baseDirectory ?? AppContext.BaseDirectory).FirstOrDefault(fileExists);
    }

    public IReadOnlyList<string> GetOverlayLibraryCandidates(string baseDirectory)
    {
        var candidates = new List<string>();
        string? configured = getEnvironmentVariable(LinuxOpenXrLibraryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            TryAddFullPath(candidates, configured);
        }

        candidates.Add(CombinePosixPath(baseDirectory, "OpenXR", LinuxOpenXrLibraryFileName));
        candidates.Add(CombinePosixPath(baseDirectory, LinuxOpenXrLibraryFileName));
        string runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(runtimeIdentifier))
        {
            candidates.Add(
                CombinePosixPath(baseDirectory, "runtimes", runtimeIdentifier, "native", LinuxOpenXrLibraryFileName)
            );
        }

        return candidates.Distinct(StringComparer.Ordinal).ToArray();
    }

    public string? DetectConfiguredOpenVrShim()
    {
        string? libraryOverride = getEnvironmentVariable(OpenVrNativeLibraryResolver.LibraryEnvironmentVariable);
        if (
            !string.IsNullOrWhiteSpace(libraryOverride)
            && TryNormalizePath(libraryOverride, out string resolvedLibraryPath)
            && fileExists(resolvedLibraryPath)
        )
        {
            string? libraryShim = IdentifyOpenVrShim(resolvedLibraryPath);
            if (libraryShim is not null)
            {
                return libraryShim;
            }
        }

        string? vrOverride = getEnvironmentVariable("VR_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(vrOverride))
        {
            return IdentifyOpenVrShim(vrOverride);
        }

        string? vrPathsFile = getEnvironmentVariable("VR_PATHREG_OVERRIDE")?.Trim();
        if (string.IsNullOrEmpty(vrPathsFile))
        {
            string? configDirectory = GetXdgConfigDirectory();
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                return null;
            }

            vrPathsFile = CombinePosixPath(configDirectory, "openvr", "openvrpaths.vrpath");
        }

        string? contents = readAllText(vrPathsFile);
        return ParseOpenVrPathsForShim(contents);
    }

    public LinuxOpenXrActiveRuntime DetectActiveOpenXrRuntime()
    {
        foreach (string manifestPath in GetOpenXrManifestCandidates())
        {
            string? contents = readAllText(manifestPath);
            if (contents is null)
            {
                continue;
            }

            return ClassifyOpenXrManifest(manifestPath, contents);
        }

        return LinuxOpenXrActiveRuntime.Unknown;
    }

    public bool IsWivrnActive()
    {
        if (isProcessRunning("wivrn-server"))
        {
            return true;
        }

        string? runtimeDir = getEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(runtimeDir)
            && (
                fileExists(CombinePosixPath(runtimeDir, "wivrn", "comp_ipc"))
                || fileExists(CombinePosixPath(runtimeDir, "wivrn_comp_ipc"))
            );
    }

    public bool IsMonadoActive()
    {
        if (isProcessRunning("monado-service"))
        {
            return true;
        }

        string? runtimeDir = getEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(runtimeDir) && fileExists(CombinePosixPath(runtimeDir, "monado_comp_ipc"));
    }

    internal static string? IdentifyOpenVrShim(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.Contains("xrizer", StringComparison.OrdinalIgnoreCase))
        {
            return "xrizer";
        }

        if (
            path.Contains("opencomposite", StringComparison.OrdinalIgnoreCase)
            || path.Contains("openovr", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "OpenComposite";
        }

        return null;
    }

    internal static string? ParseOpenVrPathsForShim(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (
                JsonNode.Parse(json) is not JsonObject root
                || root["runtime"] is not JsonArray runtimes
                || runtimes.Count == 0
            )
            {
                return null;
            }

            // OpenVR selects the first runtime entry; subsequent entries are historical fallbacks.
            if (runtimes[0] is JsonValue value && value.TryGetValue<string>(out string? runtimePath))
            {
                return IdentifyOpenVrShim(runtimePath);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    internal static LinuxOpenXrActiveRuntime ClassifyOpenXrManifest(string manifestPath, string json)
    {
        string probe = manifestPath;
        try
        {
            if (JsonNode.Parse(json) is JsonObject root && root["runtime"] is JsonObject runtime)
            {
                string? name = runtime["name"]?.GetValue<string>();
                string? libraryPath = runtime["library_path"]?.GetValue<string>();
                probe = $"{manifestPath} {name} {libraryPath}";
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            probe = $"{manifestPath} {json}";
        }

        if (probe.Contains("wivrn", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxOpenXrActiveRuntime.WiVRn;
        }

        if (probe.Contains("monado", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxOpenXrActiveRuntime.Monado;
        }

        if (
            probe.Contains("steamxr", StringComparison.OrdinalIgnoreCase)
            || probe.Contains("steamvr", StringComparison.OrdinalIgnoreCase)
        )
        {
            return LinuxOpenXrActiveRuntime.SteamVr;
        }

        return LinuxOpenXrActiveRuntime.Other;
    }

    private IEnumerable<string> GetOpenXrManifestCandidates()
    {
        string? envManifest = getEnvironmentVariable("XR_RUNTIME_JSON");
        if (!string.IsNullOrWhiteSpace(envManifest))
        {
            yield return envManifest.Trim();
        }

        string? configDirectory = GetXdgConfigDirectory();
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            yield return CombinePosixPath(configDirectory, "openxr", "1", "active_runtime.json");
        }

        string? xdgConfigDirs = getEnvironmentVariable("XDG_CONFIG_DIRS");
        if (string.IsNullOrWhiteSpace(xdgConfigDirs))
        {
            yield return "/etc/xdg/openxr/1/active_runtime.json";
        }
        else
        {
            bool yieldedAny = false;
            foreach (
                string rawDir in xdgConfigDirs.Split(
                    ':',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                yieldedAny = true;
                yield return CombinePosixPath(rawDir, "openxr", "1", "active_runtime.json");
            }

            if (!yieldedAny)
            {
                yield return "/etc/xdg/openxr/1/active_runtime.json";
            }
        }
    }

    private string? GetXdgConfigDirectory()
    {
        string? xdgConfig = getEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfig))
        {
            return xdgConfig.Trim();
        }

        string profile = getUserProfile();
        return string.IsNullOrWhiteSpace(profile) ? null : CombinePosixPath(profile, ".config");
    }

    private static string CombinePosixPath(string first, params string[] rest)
    {
        string result = first.TrimEnd('/');
        foreach (string segment in rest)
        {
            result = $"{result}/{segment.Trim('/')}";
        }

        return result;
    }

    private static void TryAddFullPath(List<string> candidates, string rawPath)
    {
        if (TryNormalizePath(rawPath, out string normalized))
        {
            candidates.Add(normalized);
        }
    }

    private static bool TryNormalizePath(string rawPath, out string normalizedPath)
    {
        string trimmed = rawPath.Trim();
        if (trimmed.StartsWith('/') && Path.DirectorySeparatorChar != '/')
        {
            normalizedPath = trimmed;
            return true;
        }

        try
        {
            normalizedPath = Path.GetFullPath(trimmed);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Ignore malformed environment overrides.
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static string? TryReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSystemProcessRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        try
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
