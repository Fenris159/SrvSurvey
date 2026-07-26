using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace SrvSurvey.Desktop.Theming;

public sealed class RavenThemeService
{
    private readonly Application application;
    private readonly ThemePreferenceStore preferenceStore;
    private LegacyOverlayTheme overlayTheme;

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

    public LegacyOverlayTheme CurrentOverlayTheme => overlayTheme;

    public IReadOnlyList<RavenThemeDefinition> AvailableThemes =>
        RavenThemeCatalog.All;

    public event EventHandler? ThemeChanged;

    public event EventHandler? OverlayThemeChanged;

    public void ApplyCurrent()
    {
        ApplyApplicationTheme(Current);
        ApplyOverlayTheme(overlayTheme, notify: false);
    }

    public void Select(string key)
    {
        var selected = RavenThemeCatalog.Get(key);
        if (selected == Current)
        {
            return;
        }

        Current = selected;
        ApplyApplicationTheme(selected);
        preferenceStore.SaveThemeKey(selected.Key);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyOverlayTheme(LegacyOverlayTheme theme)
    {
        ApplyOverlayTheme(theme, notify: true);
    }

    private void ApplyApplicationTheme(RavenThemeDefinition theme)
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
    }

    private void ApplyOverlayTheme(LegacyOverlayTheme theme, bool notify)
    {
        ArgumentNullException.ThrowIfNull(theme);
        overlayTheme = theme;
        foreach (var entry in theme.Colors)
        {
            application.Resources[$"LegacyTheme.{entry.Key}"] =
                new SolidColorBrush(entry.Value);
        }

        SetBrush("RavenOverlayWindowBrush", theme.GetColor("black"));
        SetBrush("RavenOverlaySurfaceBrush", theme.GetColor("black"));
        SetBrush("RavenOverlayRaisedSurfaceBrush", theme.GetColor("black"));
        SetBrush("RavenOverlayAccentBrush", theme.GetColor("orange"));
        SetBrush(
            "RavenOverlayAccentMutedBrush",
            theme.GetColor("orangeDark"));
        SetBrush("RavenOverlayTextBrush", theme.GetColor("white"));
        SetBrush("RavenOverlayMutedTextBrush", theme.GetColor("grey"));
        SetBrush("RavenOverlayBorderBrush", theme.GetColor("cyanDark"));
        SetBrush("RavenOverlayInformationBrush", theme.GetColor("cyan"));
        SetBrush("RavenOverlaySuccessBrush", theme.GetColor("green"));
        SetBrush("RavenOverlayWarningBrush", theme.GetColor("yellow"));
        SetBrush("RavenOverlayDangerBrush", theme.GetColor("red"));
        if (notify)
        {
            OverlayThemeChanged?.Invoke(this, EventArgs.Empty);
        }
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
