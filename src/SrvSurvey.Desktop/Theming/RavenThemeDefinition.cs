namespace SrvSurvey.Desktop.Theming;

public sealed record RavenThemeDefinition(
    string Key,
    string DisplayName,
    bool IsDark,
    string WindowColor,
    string SidebarColor,
    string SurfaceColor,
    string RaisedSurfaceColor,
    string AccentColor,
    string AccentHoverColor,
    string AccentMutedColor,
    string AccentForegroundColor,
    string TextColor,
    string MutedTextColor,
    string BorderColor)
{
    public string HighestSurfaceColor { get; init; } = RaisedSurfaceColor;

    public string StrongBorderColor { get; init; } = BorderColor;

    public string TertiaryTextColor { get; init; } = MutedTextColor;

    public string SecondaryFillColor { get; init; } = SurfaceColor;

    public string InteractiveHoverColor { get; init; } = AccentMutedColor;

    public string ControlAccentColor { get; init; } = AccentColor;

    public string ControlAccentHoverColor { get; init; } = AccentHoverColor;

    public string FocusRingColor { get; init; } = AccentColor;

    public string ModalScrimColor { get; init; } = "#8C000000";

    public string SuccessColor { get; init; } =
        IsDark ? "#6CCB72" : "#107C10";

    public string WarningColor { get; init; } =
        IsDark ? "#F7C948" : "#8A5D00";

    public string DangerColor { get; init; } =
        IsDark ? "#FF7B72" : "#C50F1F";

    public bool UseSurfaceOnlyDepth { get; init; }
}
