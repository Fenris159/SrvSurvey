namespace SrvSurvey.Desktop.Platform.Overlay;

public static class VrPlatformProfileCatalog
{
    public const string SteamVrProfileId = "steamvr";
    public const string MetaViaSteamVrProfileId = "meta-via-steamvr";
    public const string MetaSteamLinkProfileId = "meta-steam-link";
    public const string MetaVirtualDesktopProfileId = "meta-virtual-desktop";
    public const string MetaAlvrProfileId = "meta-alvr";
    public const string WindowsMixedRealityProfileId = "wmr-via-steamvr";
    public const string CustomOpenVrProfileId = "custom-openvr";

    public static IReadOnlyList<VrPlatformProfile> All { get; } =
    [
        new(
            SteamVrProfileId,
            "SteamVR headset",
            "Valve Index, HTC Vive, Pimax, Bigscreen Beyond, Pico, and other SteamVR headsets",
            "Native SteamVR / OpenVR connection",
            "Recommended on Windows. SrvSurvey publishes through SteamVR's overlay compositor; SteamVR for Linux and Elite through Proton are experimental.",
            [
                "Connect the headset and complete its normal SteamVR room or seated setup.",
                "Start SteamVR and wait until the headset icon reports ready.",
                "Launch Elite Dangerous in VR, then enable SrvSurvey overlays below.",
            ]
        ),
        new(
            MetaViaSteamVrProfileId,
            "Meta Quest or Rift — Link / Air Link",
            "Quest 2, 3, 3S, Pro, Rift, and Rift S on Windows",
            "Meta Horizon Link bridged into SteamVR",
            "Supported on Windows. SteamVR must stay running because SrvSurvey publishes to its OpenVR overlay compositor.",
            [
                "Connect the headset with Meta Horizon Link over USB or Air Link and enter PC VR.",
                "Start SteamVR and wait until it reports the Meta headset and controllers ready.",
                "Launch Elite through SteamVR, then enable SrvSurvey overlays below.",
            ]
        ),
        new(
            MetaSteamLinkProfileId,
            "Meta Quest — Steam Link",
            "Quest 2, 3, and Pro using Valve's wireless Steam Link app",
            "Valve Steam Link direct to SteamVR",
            "Supported on Windows 10 or newer. This free wireless route requires the PC to run Steam and SteamVR on the same local network.",
            [
                "Install Valve's Steam Link app on the Quest and connect the PC to the router by Ethernet.",
                "Open Steam Link in the headset, pair the PC, and wait for SteamVR to report the headset ready.",
                "Launch Elite from the SteamVR session, then enable SrvSurvey overlays below.",
            ]
        ),
        new(
            MetaVirtualDesktopProfileId,
            "Meta Quest — Virtual Desktop",
            "Quest 1, 2, 3, 3S, and Pro using Virtual Desktop's SteamVR mode",
            "Virtual Desktop Streamer bridged into SteamVR",
            "Supported on Windows. Virtual Desktop must use its SteamVR path; VDXR bypasses SteamVR and cannot carry SrvSurvey overlays.",
            [
                "Install Virtual Desktop on the Quest and Virtual Desktop Streamer on the Windows PC.",
                "Connect in Virtual Desktop, launch SteamVR from its VR menu, and confirm the headset is ready.",
                "Launch Elite in that SteamVR session, then enable SrvSurvey overlays below.",
            ]
        ),
        new(
            MetaAlvrProfileId,
            "Meta Quest — ALVR (experimental)",
            "Quest headsets using the open-source ALVR SteamVR driver on Windows or Linux",
            "ALVR wireless streaming driver for SteamVR",
            "Experimental. ALVR supports Windows and Linux, but Linux SteamVR, ALVR, and Elite through Proton each add unsupported setup and troubleshooting requirements.",
            [
                "Install SteamVR and ALVR on the PC, then install and trust the matching ALVR client on the Quest.",
                "Connect from ALVR and wait until SteamVR reports the headset ready.",
                "Launch Elite in the same SteamVR session, then enable SrvSurvey overlays below.",
            ]
        ),
        new(
            WindowsMixedRealityProfileId,
            "Windows Mixed Reality",
            "Reverb G2 and other Windows Mixed Reality headsets on supported Windows installations",
            "Windows Mixed Reality bridged through SteamVR",
            "Legacy Windows route. Microsoft removed WMR from Windows 11 24H2; remaining Windows 11 23H2 SteamVR support ends in November 2026.",
            [
                "Start Mixed Reality Portal and complete the headset's normal connection setup.",
                "Start SteamVR through the Windows Mixed Reality bridge and confirm the headset is ready.",
                "Launch Elite through SteamVR, then enable SrvSurvey overlays below.",
            ]
        ),
        new(
            CustomOpenVrProfileId,
            "Custom OpenVR runtime",
            "Advanced setups with an OpenVR-compatible compositor",
            "User-specified OpenVR runtime process",
            "Advanced. The runtime must implement the OpenVR overlay API; an OpenXR-only runtime is not sufficient.",
            [
                "Start the OpenVR-compatible compositor and connect the headset.",
                "Enter the compositor's process name below without a file extension.",
                "Launch Elite in the same runtime, then enable SrvSurvey overlays.",
            ],
            true
        ),
    ];

    public static VrPlatformProfile Get(string? profileId)
    {
        return All.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal)) ?? All[0];
    }
}

public sealed record VrPlatformProfile(
    string Id,
    string DisplayName,
    string HeadsetExamples,
    string ConnectionRoute,
    string CompatibilityNote,
    IReadOnlyList<string> PairingSteps,
    bool IsCustom = false
);

public enum VrOverlayConnectionState
{
    Disabled,
    WaitingForRuntime,
    Connecting,
    Ready,
    Error,
}
