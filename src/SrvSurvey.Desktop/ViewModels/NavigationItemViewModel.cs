namespace SrvSurvey.Desktop.ViewModels;

public sealed record NavigationItemViewModel(
    string Key,
    string Label,
    string Description,
    bool HasOverlaySettings = false);
