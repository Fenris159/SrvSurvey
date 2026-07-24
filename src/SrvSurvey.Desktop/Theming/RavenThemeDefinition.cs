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
    string BorderColor);
