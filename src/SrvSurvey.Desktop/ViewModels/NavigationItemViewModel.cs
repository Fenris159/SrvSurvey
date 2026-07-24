namespace SrvSurvey.Desktop.ViewModels;

public sealed record NavigationItemViewModel(
    string Key,
    string Label,
    string Glyph,
    string Description,
    bool IsImplemented)
{
    public string StatusLabel => IsImplemented ? string.Empty : "Pending";
}
