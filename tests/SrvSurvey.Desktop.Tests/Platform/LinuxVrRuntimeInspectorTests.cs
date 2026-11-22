using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class LinuxVrRuntimeInspectorTests
{
    [Fact]
    public void NonLinuxHostBypassesOpenVrSafetyAndRejectsLinuxOpenXrPreflight()
    {
        var inspector = new LinuxVrRuntimeInspector(isLinux: () => false);

        Assert.Null(inspector.CheckOpenVrSafety(isCustomRuntime: false));
        Assert.Null(inspector.FindOverlayLibrary("/app"));
        VrRuntimeProbe? openXr = inspector.CheckLinuxOpenXrPreflight(VrPlatformProfileCatalog.WivrnOpenXrProfileId);
        Assert.NotNull(openXr);
        Assert.False(openXr.RuntimeAvailable);
        Assert.Contains("require Linux", openXr.Message);
    }

    [Theory]
    [InlineData("/usr/lib/xrizer", "xrizer")]
    [InlineData("/opt/OpenComposite/bin/linux64", "OpenComposite")]
    [InlineData("/usr/share/openovr", "OpenComposite")]
    public void OpenVrSafetyBlocksXrizerAndOpenCompositeFromEnvironmentOverride(
        string overridePath,
        string expectedShim
    )
    {
        var inspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "VR_OVERRIDE" ? overridePath : null,
            readAllText: _ => null,
            fileExists: _ => false,
            isProcessRunning: _ => true,
            getUserProfile: () => "/home/pilot"
        );

        VrRuntimeProbe? probe = inspector.CheckOpenVrSafety(isCustomRuntime: false);
        Assert.NotNull(probe);
        Assert.False(probe.RuntimeAvailable);
        Assert.Contains(expectedShim, probe.Message);
        Assert.Contains("WiVRn", probe.Message);

        using var openVr = new OpenVrRuntime(inspector);
        VrRuntimeProbe runtimeProbe = openVr.Probe();
        Assert.False(runtimeProbe.RuntimeAvailable);
        Assert.Contains(expectedShim, runtimeProbe.Message);
        VrRuntimeResult initResult = openVr.Initialize();
        Assert.False(initResult.Succeeded);
        Assert.Contains(expectedShim, initResult.Message);
    }

    [Fact]
    public void OpenVrSafetyBlocksShimConfiguredInOpenVrPathsFile()
    {
        const string vrPathJson = """{"runtime":["/usr/lib/xrizer","/opt/SteamVR"]}""";
        var inspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XDG_CONFIG_HOME" ? "/home/pilot/.xdg" : null,
            readAllText: path => path == "/home/pilot/.xdg/openvr/openvrpaths.vrpath" ? vrPathJson : null,
            fileExists: _ => false,
            isProcessRunning: _ => true,
            getUserProfile: () => "/home/pilot"
        );

        VrRuntimeProbe? probe = inspector.CheckOpenVrSafety(isCustomRuntime: true);
        Assert.NotNull(probe);
        Assert.False(probe.RuntimeAvailable);
        Assert.Contains("xrizer", probe.Message);

        // OpenVR selects only the first entry; historical shim entries after SteamVR must not block SteamVR.
        const string steamFirstJson = """{"runtime":["/opt/SteamVR","/usr/lib/xrizer"]}""";
        var steamFirstInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XDG_CONFIG_HOME" ? "/home/pilot/.xdg" : null,
            readAllText: path => path == "/home/pilot/.xdg/openvr/openvrpaths.vrpath" ? steamFirstJson : null,
            fileExists: _ => false,
            isProcessRunning: name => name == "vrserver",
            getUserProfile: () => "/home/pilot"
        );
        Assert.Null(steamFirstInspector.CheckOpenVrSafety(isCustomRuntime: false));
    }

    [Fact]
    public void OpenVrSafetyHonorsLibraryOverrideNonShimVrOverrideAndPathRegOverride()
    {
        // SRVSURVEY_OPENVR_LIBRARY pointing at a shim blocks even with SteamVR registered and vrserver running.
        var libraryShimInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key =>
                key == OpenVrNativeLibraryResolver.LibraryEnvironmentVariable
                    ? "/opt/OpenComposite/bin/linux64/libopenvr_api.so"
                    : null,
            readAllText: _ => """{"runtime":["/opt/SteamVR"]}""",
            fileExists: _ => false,
            isProcessRunning: name => name == "vrserver",
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? libShimProbe = libraryShimInspector.CheckOpenVrSafety(isCustomRuntime: false);
        Assert.NotNull(libShimProbe);
        Assert.False(libShimProbe.RuntimeAvailable);
        Assert.Contains("OpenComposite", libShimProbe.Message);

        // Non-shim VR_OVERRIDE takes precedence over an inactive xrizer entry in openvrpaths.vrpath.
        var steamOverrideInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "VR_OVERRIDE" ? "/opt/SteamVR" : null,
            readAllText: _ => """{"runtime":["/usr/lib/xrizer"]}""",
            fileExists: _ => false,
            isProcessRunning: name => name == "vrserver",
            getUserProfile: () => "/home/pilot"
        );
        Assert.Null(steamOverrideInspector.CheckOpenVrSafety(isCustomRuntime: false));

        // VR_PATHREG_OVERRIDE takes precedence over default openvrpaths.vrpath.
        var pathRegInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "VR_PATHREG_OVERRIDE" ? "/custom/openvrpaths.vrpath" : null,
            readAllText: path =>
                path == "/custom/openvrpaths.vrpath"
                    ? """{"runtime":["/usr/lib/xrizer"]}"""
                    : """{"runtime":["/opt/SteamVR"]}""",
            fileExists: _ => false,
            isProcessRunning: name => name == "vrserver",
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? pathRegProbe = pathRegInspector.CheckOpenVrSafety(isCustomRuntime: false);
        Assert.NotNull(pathRegProbe);
        Assert.False(pathRegProbe.RuntimeAvailable);
        Assert.Contains("xrizer", pathRegProbe.Message);
    }

    [Fact]
    public void OpenVrSafetyBlocksWhenWivrnOrMonadoIsRunningWithoutVrServer()
    {
        var wivrnProcessInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => null,
            fileExists: _ => false,
            isProcessRunning: name => name == "wivrn-server",
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? wivrnProbe = wivrnProcessInspector.CheckOpenVrSafety(isCustomRuntime: false);
        Assert.NotNull(wivrnProbe);
        Assert.False(wivrnProbe.RuntimeAvailable);
        Assert.Contains("WiVRn is running without SteamVR", wivrnProbe.Message);

        var wivrnSocketInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XDG_RUNTIME_DIR" ? "/run/user/1000" : null,
            readAllText: _ => null,
            fileExists: path => path == "/run/user/1000/wivrn/comp_ipc",
            isProcessRunning: _ => false,
            getUserProfile: () => "/home/pilot"
        );
        Assert.Contains("WiVRn", wivrnSocketInspector.CheckOpenVrSafety(isCustomRuntime: true)!.Message);

        var monadoSocketInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XDG_RUNTIME_DIR" ? "/run/user/1000" : null,
            readAllText: _ => null,
            fileExists: path => path == "/run/user/1000/monado_comp_ipc",
            isProcessRunning: _ => false,
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? monadoProbe = monadoSocketInspector.CheckOpenVrSafety(isCustomRuntime: false);
        Assert.NotNull(monadoProbe);
        Assert.Contains("Monado is running without SteamVR", monadoProbe.Message);
    }

    [Fact]
    public void OpenVrSafetyWaitsForVrServerOnStandardSteamVrRoutesButAllowsCustomAndRunningSteamVr()
    {
        var stoppedInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => """{"runtime":["/home/pilot/.steam/steam/steamapps/common/SteamVR"]}""",
            fileExists: _ => false,
            isProcessRunning: _ => false,
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? stopped = stoppedInspector.CheckOpenVrSafety(isCustomRuntime: false);
        Assert.NotNull(stopped);
        Assert.True(stopped.RuntimeAvailable);
        Assert.False(stopped.HeadsetPresent);
        Assert.Contains("vrserver", stopped.Message);
        Assert.Null(stoppedInspector.CheckOpenVrSafety(isCustomRuntime: true));

        var runningInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => "{not valid json",
            fileExists: _ => false,
            isProcessRunning: name => name == "vrserver",
            getUserProfile: () => "/home/pilot"
        );
        Assert.Null(runningInspector.CheckOpenVrSafety(isCustomRuntime: false));
    }

    [Fact]
    public void LinuxOpenXrPreflightRejectsSteamVrManifestAndWaitsUntilWivrnOrMonadoIsActive()
    {
        var steamVrManifestInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XR_RUNTIME_JSON" ? "/opt/steamxr_linux64.json" : null,
            readAllText: path =>
                path == "/opt/steamxr_linux64.json"
                    ? """{"runtime":{"name":"SteamVR","library_path":"/opt/steamxr.so"}}"""
                    : null,
            fileExists: _ => false,
            isProcessRunning: name => name == "wivrn-server",
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? steamMismatch = steamVrManifestInspector.CheckLinuxOpenXrPreflight(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId
        );
        Assert.NotNull(steamMismatch);
        Assert.False(steamMismatch.HeadsetPresent);
        Assert.Contains("SteamVR", steamMismatch.Message);
        Assert.Contains("openxr_wivrn.json", steamMismatch.Message);

        var inactiveInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => null,
            fileExists: _ => false,
            isProcessRunning: _ => false,
            getUserProfile: () => "/home/pilot"
        );
        Assert.Contains(
            "wivrn-server",
            inactiveInspector.CheckLinuxOpenXrPreflight(VrPlatformProfileCatalog.WivrnOpenXrProfileId)!.Message
        );
        Assert.Contains(
            "monado-service",
            inactiveInspector.CheckLinuxOpenXrPreflight(VrPlatformProfileCatalog.MonadoOpenXrProfileId)!.Message
        );

        var activeWivrnManifestInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: path =>
                path == "/home/pilot/.config/openxr/1/active_runtime.json"
                    ? """{"runtime":{"name":"wivrn","library_path":"/usr/lib/libopenxr_wivrn.so"}}"""
                    : null,
            fileExists: _ => false,
            isProcessRunning: _ => false,
            getUserProfile: () => "/home/pilot"
        );
        Assert.Null(
            activeWivrnManifestInspector.CheckLinuxOpenXrPreflight(VrPlatformProfileCatalog.WivrnOpenXrProfileId)
        );
        Assert.Equal(LinuxOpenXrActiveRuntime.WiVRn, activeWivrnManifestInspector.DetectActiveOpenXrRuntime());

        var activeMonadoInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: path =>
                path == "/etc/xdg/openxr/1/active_runtime.json"
                    ? """{"runtime":{"library_path":"/usr/lib/libopenxr_monado.so"}}"""
                    : null,
            fileExists: _ => false,
            isProcessRunning: name => name == "monado-service",
            getUserProfile: () => "/home/pilot"
        );
        Assert.Null(activeMonadoInspector.CheckLinuxOpenXrPreflight(VrPlatformProfileCatalog.MonadoOpenXrProfileId));
        Assert.Equal(LinuxOpenXrActiveRuntime.Monado, activeMonadoInspector.DetectActiveOpenXrRuntime());

        // Positively identified Monado manifest must be rejected for WiVRn even if wivrn-server is running.
        var monadoMismatchInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: path =>
                path == "/home/pilot/.config/openxr/1/active_runtime.json"
                    ? """{"runtime":{"name":"Monado","library_path":"/usr/lib/libopenxr_monado.so"}}"""
                    : null,
            fileExists: _ => false,
            isProcessRunning: name => name == "wivrn-server",
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? monadoForWivrn = monadoMismatchInspector.CheckLinuxOpenXrPreflight(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId
        );
        Assert.NotNull(monadoForWivrn);
        Assert.Contains("Monado, not WiVRn", monadoForWivrn.Message);

        // Positively identified WiVRn manifest must be rejected for Monado even if monado-service is running.
        var wivrnMismatchInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: path =>
                path == "/home/pilot/.config/openxr/1/active_runtime.json"
                    ? """{"runtime":{"name":"WiVRn","library_path":"/usr/lib/libopenxr_wivrn.so"}}"""
                    : null,
            fileExists: _ => false,
            isProcessRunning: name => name == "monado-service",
            getUserProfile: () => "/home/pilot"
        );
        VrRuntimeProbe? wivrnForMonado = wivrnMismatchInspector.CheckLinuxOpenXrPreflight(
            VrPlatformProfileCatalog.MonadoOpenXrProfileId
        );
        Assert.NotNull(wivrnForMonado);
        Assert.Contains("WiVRn, not Monado", wivrnForMonado.Message);

        // XDG_CONFIG_DIRS is searched in order and replaces the default /etc/xdg lookup when set.
        var xdgConfigDirsInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XDG_CONFIG_DIRS" ? "/custom/first:/custom/second" : null,
            readAllText: path =>
                path switch
                {
                    "/custom/second/openxr/1/active_runtime.json" =>
                        """{"runtime":{"name":"WiVRn","library_path":"/usr/lib/libopenxr_wivrn.so"}}""",
                    "/etc/xdg/openxr/1/active_runtime.json" =>
                        """{"runtime":{"name":"SteamVR","library_path":"/opt/steamxr.so"}}""",
                    _ => null,
                },
            fileExists: _ => false,
            isProcessRunning: name => name == "wivrn-server",
            getUserProfile: () => "/home/pilot"
        );
        Assert.Equal(LinuxOpenXrActiveRuntime.WiVRn, xdgConfigDirsInspector.DetectActiveOpenXrRuntime());
        Assert.Null(xdgConfigDirsInspector.CheckLinuxOpenXrPreflight(VrPlatformProfileCatalog.WivrnOpenXrProfileId));

        // XDG_CONFIG_DIRS containing only colons falls back to /etc/xdg.
        var emptySegmentsInspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key => key == "XDG_CONFIG_DIRS" ? ":::" : null,
            readAllText: path =>
                path == "/etc/xdg/openxr/1/active_runtime.json"
                    ? """{"runtime":{"name":"Monado","library_path":"/usr/lib/libopenxr_monado.so"}}"""
                    : null,
            fileExists: _ => false,
            isProcessRunning: name => name == "monado-service",
            getUserProfile: () => "/home/pilot"
        );
        Assert.Equal(LinuxOpenXrActiveRuntime.Monado, emptySegmentsInspector.DetectActiveOpenXrRuntime());
    }

    [Fact]
    public void OverlayLibraryCandidatesIncludeEnvironmentOverrideAndPackagedPaths()
    {
        var inspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: key =>
                key == LinuxVrRuntimeInspector.LinuxOpenXrLibraryEnvironmentVariable
                    ? "/custom/libSrvSurveyLinuxOpenXrOverlay.so"
                    : null,
            fileExists: path => path == "/app/OpenXR/libSrvSurveyLinuxOpenXrOverlay.so"
        );

        IReadOnlyList<string> candidates = inspector.GetOverlayLibraryCandidates("/app");
        Assert.Equal("/custom/libSrvSurveyLinuxOpenXrOverlay.so", candidates[0]);
        Assert.Contains("/app/OpenXR/libSrvSurveyLinuxOpenXrOverlay.so", candidates);
        Assert.Contains("/app/libSrvSurveyLinuxOpenXrOverlay.so", candidates);
        Assert.Equal("/app/OpenXR/libSrvSurveyLinuxOpenXrOverlay.so", inspector.FindOverlayLibrary("/app"));
    }

    [Fact]
    public void OpenXrManifestClassificationHandlesInvalidJsonAndUnknownRuntimes()
    {
        Assert.Equal(
            LinuxOpenXrActiveRuntime.WiVRn,
            LinuxVrRuntimeInspector.ClassifyOpenXrManifest("/usr/share/openxr/1/openxr_wivrn.json", "{broken")
        );
        Assert.Equal(
            LinuxOpenXrActiveRuntime.Other,
            LinuxVrRuntimeInspector.ClassifyOpenXrManifest(
                "/etc/xdg/openxr/1/active_runtime.json",
                """{"runtime":{"name":"custom","library_path":"/usr/lib/libopenxr_custom.so"}}"""
            )
        );
        Assert.Null(LinuxVrRuntimeInspector.ParseOpenVrPathsForShim(null));
        Assert.Null(LinuxVrRuntimeInspector.ParseOpenVrPathsForShim("""{"runtime":[]}"""));
    }
}
