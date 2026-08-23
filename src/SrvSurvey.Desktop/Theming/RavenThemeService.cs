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
            ["RavenOverlayHeaderBrush"] = "header",
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
            ["RavenOverlayBioConfirmedDimPotentialBrush"] =
                "bio.confirmedDimPotential",
            ["RavenOverlayBioPredictionBrush"] = "bio.prediction",
            ["RavenOverlayBioPredictionPotentialBrush"] =
                "bio.predictionPotential",
            ["RavenOverlayBioGoldBrush"] = "bio.gold",
            ["RavenOverlayBioGoldDimBrush"] = "bio.goldDark",
            ["RavenOverlayBioGoldFillBrush"] = "bio.goldFill",
            ["RavenOverlayBioGoldDimFillBrush"] = "bio.goldDarkFill",
            ["RavenOverlayBioGoldPotentialBrush"] = "bio.goldPotential",
            ["RavenOverlayBioGoldDimPotentialBrush"] =
                "bio.goldDarkPotential",
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

    private static readonly string[] BiologyEdgeKeys =
    [
        "confirmedEdge",
        "confirmedDimEdge",
        "predictionEdge",
        "goldEdge",
        "goldDarkEdge",
        "galacticRegionEdge",
        "unknownEdge",
        "confirmedSegmentEdge",
        "confirmedPotentialSegmentEdge",
        "confirmedDimSegmentEdge",
        "confirmedDimPotentialSegmentEdge",
        "predictionSegmentEdge",
        "predictionPotentialSegmentEdge",
        "goldSegmentEdge",
        "goldPotentialSegmentEdge",
        "goldDarkSegmentEdge",
        "goldDarkPotentialSegmentEdge",
        "galacticRegionSegmentEdge",
        "galacticRegionPotentialSegmentEdge",
    ];

    private static readonly string[] FluentCheckedGlyphResourceKeys =
    [
        "CheckBoxCheckGlyphForegroundChecked",
        "CheckBoxCheckGlyphForegroundCheckedPointerOver",
        "CheckBoxCheckGlyphForegroundCheckedPressed",
        "CheckBoxCheckGlyphForegroundIndeterminate",
        "CheckBoxCheckGlyphForegroundIndeterminatePointerOver",
        "CheckBoxCheckGlyphForegroundIndeterminatePressed",
    ];

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

        ApplyFluentAccent(theme.ControlAccentColor);
        SetBrush("RavenWindowBrush", theme.WindowColor);
        SetBrush("RavenSidebarBrush", theme.SidebarColor);
        SetBrush("RavenSurfaceBrush", theme.SurfaceColor);
        SetBrush("RavenRaisedSurfaceBrush", theme.RaisedSurfaceColor);
        SetBrush("RavenHighestSurfaceBrush", theme.HighestSurfaceColor);
        SetBrush("RavenAccentBrush", theme.AccentColor);
        SetBrush("RavenAccentHoverBrush", theme.AccentHoverColor);
        SetBrush("RavenControlAccentBrush", theme.ControlAccentColor);
        SetBrush(
            "RavenControlAccentHoverBrush",
            theme.ControlAccentHoverColor);
        SetBrush("RavenAccentMutedBrush", theme.AccentMutedColor);
        SetBrush("RavenSecondaryFillBrush", theme.SecondaryFillColor);
        SetBrush("RavenInteractiveHoverBrush", theme.InteractiveHoverColor);
        SetBrush("RavenRouteGuidanceBadgeBrush", theme.AccentMutedColor);
        SetBrush("RavenAccentForegroundBrush", theme.AccentForegroundColor);
        SetBrush("RavenTextBrush", theme.TextColor);
        SetBrush("RavenMutedTextBrush", theme.MutedTextColor);
        SetBrush("RavenTertiaryTextBrush", theme.TertiaryTextColor);
        SetBrush("RavenBorderBrush", theme.BorderColor);
        SetBrush("RavenStrongBorderBrush", theme.StrongBorderColor);
        SetBrush("RavenFocusRingBrush", theme.FocusRingColor);
        SetBrush("RavenModalScrimBrush", theme.ModalScrimColor);
        SetBrush("RavenSuccessBrush", theme.SuccessColor);
        SetBrush("RavenWarningBrush", theme.WarningColor);
        SetBrush("RavenDangerBrush", theme.DangerColor);
        ApplyFluentCheckBoxResources(theme);
        ApplyDepthResources(theme);
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

        var typography = theme.EffectiveTypography;
        application.Resources["RavenOverlayHeaderFontSize"] = typography.Header;
        application.Resources["RavenOverlayTitleFontSize"] = typography.Title;
        application.Resources["RavenOverlayValueFontSize"] = typography.Value;
        application.Resources["RavenOverlayBodyFontSize"] = typography.Body;
        application.Resources["RavenOverlayDetailFontSize"] = typography.Detail;
        application.Resources["RavenOverlayCaptionFontSize"] = typography.Caption;

        foreach (var themeKey in BiologyEdgeKeys)
        {
            SetBrush(
                GetBiologyEdgeResourceKey(themeKey),
                theme.GetColor($"bio.{themeKey}"));
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

    private void ApplyFluentAccent(string value)
    {
        var accent = Color.Parse(value);
        application.Resources["SystemAccentColor"] = accent;
        application.Resources["SystemAccentColorLight1"] =
            Mix(accent, Colors.White, 0.15);
        application.Resources["SystemAccentColorLight2"] =
            Mix(accent, Colors.White, 0.30);
        application.Resources["SystemAccentColorLight3"] =
            Mix(accent, Colors.White, 0.45);
        application.Resources["SystemAccentColorDark1"] =
            Mix(accent, Colors.Black, 0.15);
        application.Resources["SystemAccentColorDark2"] =
            Mix(accent, Colors.Black, 0.30);
        application.Resources["SystemAccentColorDark3"] =
            Mix(accent, Colors.Black, 0.45);
    }

    private void ApplyFluentCheckBoxResources(RavenThemeDefinition theme)
    {
        foreach (var resourceKey in FluentCheckedGlyphResourceKeys)
        {
            SetBrush(resourceKey, theme.AccentForegroundColor);
        }
    }

    private static Color Mix(Color source, Color target, double amount)
    {
        static byte Blend(byte source, byte target, double amount) =>
            (byte)Math.Round(source + ((target - source) * amount));

        return Color.FromArgb(
            source.A,
            Blend(source.R, target.R, amount),
            Blend(source.G, target.G, amount),
            Blend(source.B, target.B, amount));
    }

    private void SetInsetShadow(string key, string value)
    {
        var color = Color.Parse(value);
        application.Resources[key] = new BoxShadows(new BoxShadow
        {
            Blur = 10,
            Color = Color.FromArgb(153, color.R, color.G, color.B),
            IsInset = true,
            Spread = 2,
        });
    }

    private void ApplyDepthResources(RavenThemeDefinition theme)
    {
        if (theme.UseSurfaceOnlyDepth)
        {
            application.Resources["RavenWarningInsetShadow"] =
                new BoxShadows();
            application.Resources["RavenFloatingPanelShadow"] =
                new BoxShadows();
            return;
        }

        SetInsetShadow("RavenWarningInsetShadow", theme.WarningColor);
        application.Resources["RavenFloatingPanelShadow"] = new BoxShadows(
            new BoxShadow
            {
                OffsetY = 8,
                Blur = 24,
                Color = Color.Parse("#66000000"),
            });
    }

    private static string GetBiologyEdgeResourceKey(string themeKey)
    {
        var resourceSuffix = char.ToUpperInvariant(themeKey[0]) + themeKey[1..];
        resourceSuffix = resourceSuffix.Replace(
            "GoldDark",
            "GoldDim",
            StringComparison.Ordinal);
        return $"RavenOverlayBio{resourceSuffix}Brush";
    }
}
