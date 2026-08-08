using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Representative system-survey display payload used by the overlay
/// position editor to drive shared presentation templates.
/// </summary>
internal sealed record SystemSurveyEditorPreviewState(
    SystemScanSnapshot Snapshot,
    BiologySurveyViewModel BiologySurvey,
    BiologyStatusViewModel BiologyStatus,
    BodyInformationViewModel BodyInformation,
    IReadOnlyList<FssBodyRowViewModel> FssBodies,
    IReadOnlyList<SurveyBodyReferenceViewModel> DssBodies,
    IReadOnlyList<SurveyBodyReferenceViewModel> BiologicalBodies,
    bool ShowNonBodySignals,
    int NonBodySignalCount,
    IReadOnlyList<BiologySignalRewardBandViewModel> LastFssRewardBands,
    string LastFssRewardText,
    string FlightWarningBodyName,
    double FlightWarningGravity);
