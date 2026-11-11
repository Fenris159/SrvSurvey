using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ProfileSync;

internal enum ProfileBackupScope
{
    Portable,
    Machine,
    Excluded,
}

/// <summary>Documents why a settings store must remain outside profile backups.</summary>
internal enum ProfileBackupExclusion
{
    None,
    Credentials,
    LocalConsent,
    LocalServiceEndpoint,
    OperationalState,
}

internal sealed record SettingsBackupDefinition(
    Type StoreType,
    ProfileBackupScope Scope,
    string[] Sections,
    ProfileBackupExclusion Exclusion = ProfileBackupExclusion.None
);

/// <summary>Single inventory of settings stores and their default backup scope; file and field restrictions remain explicit.</summary>
internal static class ProfileSyncSettingsCatalog
{
    internal static IReadOnlyList<SettingsBackupDefinition> Definitions { get; } =
    [
        new(typeof(BiologyPredictionsSettingsStore), ProfileBackupScope.Portable, ["BiologyPredictions"]),
        new(typeof(BiologyRewardSettingsStore), ProfileBackupScope.Portable, ["BiologyRewards"]),
        new(typeof(BoxelSurveyStatsSettingsStore), ProfileBackupScope.Portable, ["BoxelSurveyStats"]),
        new(
            typeof(CarrierNextJumpSettingsStore),
            ProfileBackupScope.Portable,
            ["CarrierNextJump", "SquadronCarrierNextJump"]
        ),
        new(typeof(CodexImageSettingsStore), ProfileBackupScope.Portable, ["CodexImages"]),
        new(typeof(ColonizationSettingsStore), ProfileBackupScope.Portable, ["Colonization"]),
        new(typeof(CombatSettingsStore), ProfileBackupScope.Portable, ["Combat"]),
        new(typeof(CommanderPreferenceSettingsStore), ProfileBackupScope.Machine, ["CommanderPreference"]),
        new(typeof(DesktopBehaviorSettingsStore), ProfileBackupScope.Portable, ["DesktopBehavior"]),
        new(
            typeof(DiscordCarrierSettingsStore),
            ProfileBackupScope.Excluded,
            ["DiscordCarrierAnnouncements"],
            ProfileBackupExclusion.Credentials
        ),
        new(typeof(DockToDockSettingsStore), ProfileBackupScope.Portable, ["Travel"]),
        new(typeof(ExplorationPoiSettingsStore), ProfileBackupScope.Portable, ["ExplorationPois"]),
        new(typeof(FiregroupsOverlaySettingsStore), ProfileBackupScope.Portable, ["FiregroupsOverlay"]),
        new(typeof(FirstFootfallInferenceSettingsStore), ProfileBackupScope.Machine, ["FirstFootfallInference"]),
        new(typeof(GalaxyMapSettingsStore), ProfileBackupScope.Portable, ["GalaxyMap"]),
        new(typeof(GuardianGestureSettingsStore), ProfileBackupScope.Portable, ["GuardianGestures"]),
        new(typeof(GuardianOverlaySettingsStore), ProfileBackupScope.Portable, ["GuardianOverlays"]),
        new(typeof(HumanSiteSettingsStore), ProfileBackupScope.Portable, ["HumanSite"]),
        new(typeof(JournalSettingsStore), ProfileBackupScope.Machine, ["Journal"]),
        new(typeof(JumpInfoSettingsStore), ProfileBackupScope.Portable, ["JumpInfo"]),
        new(typeof(LocalizationSettingsStore), ProfileBackupScope.Portable, ["Localization"]),
        new(typeof(MineMapSettingsStore), ProfileBackupScope.Portable, ["MineMap", "MiningReferenceCommodities"]),
        new(typeof(MiningOverlaySettingsStore), ProfileBackupScope.Portable, ["MiningOverlays"]),
        new(typeof(NetworkPrivacySettingsStore), ProfileBackupScope.Machine, ["NetworkPrivacy"]),
        new(typeof(NotificationSettingsStore), ProfileBackupScope.Portable, ["Notifications"]),
        new(typeof(OverlayBehaviorSettingsStore), ProfileBackupScope.Portable, ["OverlayBehavior"]),
        new(typeof(OverlayEditorControlsSettingsStore), ProfileBackupScope.Machine, ["OverlayEditorControls"]),
        new(typeof(OverlayPanelVisibilitySettingsStore), ProfileBackupScope.Portable, ["OverlayPanelVisibility"]),
        new(typeof(OverlayScaleSettingsStore), ProfileBackupScope.Machine, ["OverlayScale"]),
        new(typeof(OverlayVehicleSettingsStore), ProfileBackupScope.Portable, ["OverlayVehicleAllowLists"]),
        new(typeof(PulseOverlaySettingsStore), ProfileBackupScope.Portable, ["PulseOverlay"]),
        new(typeof(QuestSettingsStore), ProfileBackupScope.Portable, ["Quests"]),
        new(
            typeof(RavenServiceSettingsStore),
            ProfileBackupScope.Excluded,
            ["RavenService"],
            ProfileBackupExclusion.LocalServiceEndpoint
        ),
        new(typeof(ReleaseUpdateSettingsStore), ProfileBackupScope.Portable, ["ReleaseUpdates"]),
        new(typeof(ScreenshotProcessingSettingsStore), ProfileBackupScope.Machine, ["Screenshots"]),
        new(typeof(StationInfoSettingsStore), ProfileBackupScope.Portable, ["StationInfo"]),
        new(typeof(StreamOverlaySettingsStore), ProfileBackupScope.Portable, ["Streaming"]),
        new(typeof(SurfaceMiningSettingsStore), ProfileBackupScope.Portable, ["SurfaceMining"]),
        new(typeof(SystemNicknameSettingsStore), ProfileBackupScope.Portable, ["SystemNicknames"]),
        new(typeof(SystemSurveySettingsStore), ProfileBackupScope.Portable, ["SystemSurvey"]),
        new(
            typeof(VoxStellarSettingsStore),
            ProfileBackupScope.Excluded,
            ["VoxStellar"],
            ProfileBackupExclusion.LocalConsent
        ),
        new(typeof(VrOverlaySettingsStore), ProfileBackupScope.Machine, ["VirtualReality"]),
        new(typeof(WaylandCaptureSettingsStore), ProfileBackupScope.Machine, ["WaylandCapture"]),
        new(typeof(WorkspaceContinuitySettingsStore), ProfileBackupScope.Portable, ["Workspace"]),
        new(typeof(UiSettingsDocumentStore), ProfileBackupScope.Portable, ["Version"]),
        new(typeof(ThemePreferenceStore), ProfileBackupScope.Portable, ["Theme"]),
        new(typeof(GlobalInputSettingsStore), ProfileBackupScope.Portable, ["Input"]),
        new(typeof(GroundTargetSettingsStore), ProfileBackupScope.Portable, []),
        new(typeof(SystemNotesSettingsStore), ProfileBackupScope.Portable, []),
        new(
            typeof(PortalShortcutRegistrationStore),
            ProfileBackupScope.Excluded,
            [],
            ProfileBackupExclusion.OperationalState
        ),
    ];

    internal static IReadOnlyDictionary<string, ProfileBackupScope> Sections { get; } =
        Definitions
            .SelectMany(definition => definition.Sections.Select(section => (section, definition.Scope)))
            .ToDictionary(pair => pair.section, pair => pair.Scope, StringComparer.Ordinal);
}
