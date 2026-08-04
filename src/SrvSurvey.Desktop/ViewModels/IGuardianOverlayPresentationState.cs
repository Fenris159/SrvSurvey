using System.ComponentModel;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Data consumed by the shared Guardian overlay presentations. The live
/// Guardian workspace and the isolated editor sample both implement this
/// contract so the editor never needs a second, approximate renderer.
/// </summary>
public interface IGuardianOverlayPresentationState : INotifyPropertyChanged
{
    int PreferredOverlayWidth { get; }

    int PreferredOverlayHeight { get; }

    GuardianSiteMapProjection? ActiveMapProjection { get; }

    GuardianSiteProximitySnapshot? Proximity { get; }

    double ActiveMapScale { get; }

    double ActiveMapRelativeHeading { get; }

    string? TargetObeliskName { get; }

    GuardianAlignmentMode? AlignmentMode { get; }

    double AlignmentOpacity { get; }

    bool IsAlignmentVisible { get; }

    string ActiveMapTitle { get; }

    string ActiveMapSummary { get; }

    bool HasLiveMapPrompt { get; }

    string LiveMapPromptTitle { get; }

    string LiveMapPromptText { get; }

    bool HasHeadingGuide { get; }

    string? HeadingGuideAssetPath { get; }

    string AlignmentStatusText { get; }

    string BlinkGestureText { get; }

    string ActiveMapScaleText { get; }

    string TargetObeliskText { get; }

    bool IsGlideApproach { get; }

    string GlideApproachTitle { get; }

    string GlideApproachText { get; }

    string GlideApproachFooter { get; }

    bool IsLocalGuardianStatus { get; }

    bool IsGuardianSiteTypeChoiceVisible { get; }

    bool IsGuardianHeadingChoiceVisible { get; }

    bool IsGuardianOriginVisible { get; }

    bool IsGuardianOnFootRelicVisible { get; }

    bool IsGuardianObeliskVisible { get; }

    bool IsGuardianPoiChoiceVisible { get; }

    bool IsGuardianNoPointVisible { get; }

    string GuardianStatusTitle { get; }

    string GuardianStatusDetail { get; }

    string GuardianOriginFooter { get; }

    string GuardianOnFootFooter { get; }

    string GuardianStatusObeliskTitle { get; }

    string GuardianStatusObeliskLogText { get; }

    string GuardianStatusObeliskRequirementsText { get; }

    IReadOnlyList<GuardianArtifactRequirementViewModel>
        GuardianStatusObeliskArtifacts
    { get; }

    string GuardianStatusObeliskMissionStatus { get; }

    string GuardianStatusObeliskScanStatus { get; }

    string GuardianStatusObeliskFooter { get; }

    bool HasGuardianMaterialCapacityWarning { get; }

    string GuardianMaterialCapacityWarning { get; }

    string GuardianChoiceOneText { get; }

    string GuardianChoiceTwoText { get; }

    string GuardianChoiceThreeText { get; }

    bool IsGuardianChoiceThreeVisible { get; }

    bool IsGuardianChoiceOneSelected { get; }

    bool IsGuardianChoiceTwoSelected { get; }

    bool IsGuardianChoiceThreeSelected { get; }

    string SiteDistanceText { get; }

    string NearbyPointText { get; }

    IReadOnlyList<GuardianSiteRowViewModel> CurrentSystemSites { get; }

    string CurrentSystemGuardianTitle { get; }

    IReadOnlyList<GuardianRamTahLogViewModel> CurrentRamTahLogs { get; }

    bool HasCurrentRamTahLogs { get; }

    string CurrentRamTahTitle { get; }

    string ActiveSiteTitle { get; }
}
