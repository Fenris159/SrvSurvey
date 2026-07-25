using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace SrvSurvey.Desktop.Theming;

public sealed class RavenThemeService
{
    private readonly Application application;
    private readonly ThemePreferenceStore preferenceStore;
    private readonly LegacyOverlayTheme overlayTheme;

    public RavenThemeService(
        Application application,
        ThemePreferenceStore preferenceStore,
        LegacyOverlayTheme? overlayTheme = null)
    {
        this.application = application;
        this.preferenceStore = preferenceStore;
        this.overlayTheme = overlayTheme
            ?? LegacyOverlayThemeStore.CreateDefault();
        Current = RavenThemeCatalog.Get(preferenceStore.LoadThemeKey());
    }

    public RavenThemeDefinition Current { get; private set; }

    public IReadOnlyList<RavenThemeDefinition> AvailableThemes =>
        RavenThemeCatalog.All;

    public event EventHandler? ThemeChanged;

    public void ApplyCurrent()
    {
        Apply(Current);
    }

    public void Select(string key)
    {
        var selected = RavenThemeCatalog.Get(key);
        if (selected == Current)
        {
            return;
        }

        Current = selected;
        Apply(selected);
        preferenceStore.SaveThemeKey(selected.Key);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(RavenThemeDefinition theme)
    {
        application.RequestedThemeVariant = theme.IsDark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        SetBrush("RavenWindowBrush", theme.WindowColor);
        SetBrush("RavenSidebarBrush", theme.SidebarColor);
        SetBrush("RavenSurfaceBrush", theme.SurfaceColor);
        SetBrush("RavenRaisedSurfaceBrush", theme.RaisedSurfaceColor);
        SetBrush("RavenAccentBrush", theme.AccentColor);
        SetBrush("RavenAccentHoverBrush", theme.AccentHoverColor);
        SetBrush("RavenAccentMutedBrush", theme.AccentMutedColor);
        SetBrush("RavenAccentForegroundBrush", theme.AccentForegroundColor);
        SetBrush("RavenTextBrush", theme.TextColor);
        SetBrush("RavenMutedTextBrush", theme.MutedTextColor);
        SetBrush("RavenBorderBrush", theme.BorderColor);
        SetBrush("RavenSuccessBrush", theme.IsDark ? "#6CCB72" : "#107C10");
        SetBrush("RavenWarningBrush", theme.IsDark ? "#F7C948" : "#8A5D00");
        SetBrush("RavenDangerBrush", theme.IsDark ? "#FF7B72" : "#C50F1F");
        ApplyOverlayTheme();
    }

    private void ApplyOverlayTheme()
    {
        foreach (var entry in overlayTheme.Colors)
        {
            application.Resources[$"LegacyTheme.{entry.Key}"] =
                new SolidColorBrush(entry.Value);
        }

        SetBrush("RavenOverlayWindowBrush", overlayTheme.GetColor("black"));
        SetBrush("RavenOverlaySurfaceBrush", overlayTheme.GetColor("black"));
        SetBrush("RavenOverlayRaisedSurfaceBrush", overlayTheme.GetColor("black"));
        SetBrush("RavenOverlayAccentBrush", overlayTheme.GetColor("orange"));
        SetBrush(
            "RavenOverlayAccentMutedBrush",
            overlayTheme.GetColor("orangeDark"));
        SetBrush("RavenOverlayTextBrush", overlayTheme.GetColor("white"));
        SetBrush("RavenOverlayMutedTextBrush", overlayTheme.GetColor("grey"));
        SetBrush("RavenOverlayBorderBrush", overlayTheme.GetColor("cyanDark"));
        SetBrush("RavenOverlayInformationBrush", overlayTheme.GetColor("cyan"));
        SetBrush("RavenOverlaySuccessBrush", overlayTheme.GetColor("green"));
        SetBrush("RavenOverlayWarningBrush", overlayTheme.GetColor("yellow"));
        SetBrush("RavenOverlayDangerBrush", overlayTheme.GetColor("red"));
    }

    private void SetBrush(string key, string value)
    {
        application.Resources[key] = new SolidColorBrush(Color.Parse(value));
    }

    private void SetBrush(string key, Color value)
    {
        application.Resources[key] = new SolidColorBrush(value);
    }
}
