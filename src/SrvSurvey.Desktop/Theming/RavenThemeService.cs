using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace SrvSurvey.Desktop.Theming;

public sealed class RavenThemeService
{
    private static readonly IReadOnlyDictionary<string, string>
        OverlayResourceKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RavenOverlayWindowBrush"] = "black",
            ["RavenOverlaySurfaceBrush"] = "black",
            ["RavenOverlayRaisedSurfaceBrush"] = "black",
            ["RavenOverlayAccentBrush"] = "orange",
            ["RavenOverlayAccentMutedBrush"] = "orangeDark",
            ["RavenOverlayTextBrush"] = "white",
            ["RavenOverlayMutedTextBrush"] = "grey",
            ["RavenOverlayBorderBrush"] = "cyanDark",
            ["RavenOverlayInformationBrush"] = "cyan",
            ["RavenOverlaySuccessBrush"] = "green",
            ["RavenOverlayWarningBrush"] = "yellow",
            ["RavenOverlayDangerBrush"] = "red",
            ["RavenOverlayPrimaryBrush"] = "orange",
            ["RavenOverlayPrimaryDimBrush"] = "orangeDark",
            ["RavenOverlaySecondaryBrush"] = "cyan",
            ["RavenOverlaySecondaryDimBrush"] = "cyanDark",
            ["RavenOverlayDangerDimBrush"] = "redDark",
            ["RavenOverlaySuccessDimBrush"] = "greenDark",
            ["RavenOverlayMenuGoldBrush"] = "menuGold",
            ["RavenOverlayBioConfirmedBrush"] = "bio.confirmed",
            ["RavenOverlayBioConfirmedDimBrush"] = "bio.confirmedDim",
            ["RavenOverlayBioPotentialBrush"] = "bio.potential",
            ["RavenOverlayBioPredictionBrush"] = "bio.prediction",
            ["RavenOverlayBioPredictionPotentialBrush"] =
                "bio.predictionPotential",
            ["RavenOverlayBioGoldBrush"] = "bio.gold",
            ["RavenOverlayBioGoldDimBrush"] = "bio.goldDark",
            ["RavenOverlayBioGalacticRegionBrush"] = "bio.galacticRegion",
            ["RavenOverlayBioGalacticRegionPotentialBrush"] =
                "bio.galacticRegionPotential",
            ["RavenOverlayBioUnknownBrush"] = "bio.unknown",
            ["RavenOverlayBioUnknownGlyphBrush"] = "bio.unknownGlyph",
            ["RavenOverlayBioHatchBrush"] = "bio.hatch",
            ["RavenOverlayBioEmptyBrush"] = "bio.empty",
            ["RavenOverlayBioWhiteBrush"] = "bio.white",
            ["RavenOverlayColoniseSurplusBrush"] = "colonise.surplus",
            ["RavenOverlayColoniseSurplusDimBrush"] = "colonise.surplusDark",
            ["RavenOverlayColoniseDeficitBrush"] = "colonise.deficit",
            ["RavenOverlayColoniseDeficitDimBrush"] = "colonise.deficitDark",
            ["RavenOverlayColoniseHighlightBrush"] = "colonise.highlight",
            ["RavenOverlayColoniseItemBrush"] = "colonise.item",
            ["RavenOverlayColoniseItemDimBrush"] = "colonise.itemDark",
            ["RavenOverlayColoniseRowHighlightBrush"] = "colonise.rowHighlight",
            ["RavenOverlayFczCheckpointBrush"] = "fcz.checkpoint",
            ["RavenOverlayFczCheckpointLocalBrush"] = "fcz.checkpointLocal",
            ["RavenOverlayFczPowerPostBrush"] = "fcz.powerPost",
            ["RavenOverlayGuardianBackgroundBrush"] = "guardian.background",
            ["RavenOverlayGuardianHeaderBrush"] = "guardian.header",
            ["RavenOverlayGuardianPrimaryBrush"] = "guardian.primary",
            ["RavenOverlayGuardianPrimaryDimBrush"] = "guardian.primaryDark",
            ["RavenOverlayGuardianSecondaryBrush"] = "guardian.secondary",
            ["RavenOverlayGuardianSecondaryDimBrush"] = "guardian.secondaryDark",
            ["RavenOverlayGuardianTextBrush"] = "guardian.text",
            ["RavenOverlayGuardianMutedBrush"] = "guardian.muted",
            ["RavenOverlayGuardianDangerBrush"] = "guardian.danger",
            ["RavenOverlayGuardianSuccessBrush"] = "guardian.success",
            ["RavenOverlayGuardianWarningBrush"] = "guardian.warning",
            ["RavenOverlayGuardianSurfaceBrush"] = "guardian.surface",
        };

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

    public IReadOnlyList<RavenThemeDefinition> AvailableThemes { get; } =
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
        SetBrush("RavenRouteGuidanceBadgeBrush", theme.AccentMutedColor);
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

        foreach (var mapping in OverlayResourceKeys)
        {
            SetBrush(mapping.Key, theme.GetColor(mapping.Value));
        }
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
