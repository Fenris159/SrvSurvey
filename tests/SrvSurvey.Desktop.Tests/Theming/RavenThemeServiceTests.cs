using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class RavenThemeServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-theme-service-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MonochromeThemeAppliesLayeredLowGlareRolesAndRemovesDepthShadows()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var service = new RavenThemeService(application, store);
        service.ApplyCurrent();

        service.Select("monochrome-dark");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RavenWindowBrush"] = "#0A0A0A",
            ["RavenSidebarBrush"] = "#141414",
            ["RavenSurfaceBrush"] = "#141414",
            ["RavenRaisedSurfaceBrush"] = "#1C1C1C",
            ["RavenHighestSurfaceBrush"] = "#242424",
            ["RavenBorderBrush"] = "#2A2A2A",
            ["RavenStrongBorderBrush"] = "#3A3A3A",
            ["RavenTextBrush"] = "#EDEDED",
            ["RavenMutedTextBrush"] = "#A3A3A3",
            ["RavenSelectedMutedTextBrush"] = "#0A0A0A",
            ["RavenTertiaryTextBrush"] = "#737373",
            ["RavenAccentForegroundBrush"] = "#0A0A0A",
            ["RavenAccentBrush"] = "#E6D59A",
            ["RavenControlAccentBrush"] = "#F5F5F5",
            ["RavenControlAccentHoverBrush"] = "#EDEDED",
            ["RavenSecondaryFillBrush"] = "#262626",
            ["RavenInteractiveHoverBrush"] = "#3A3A3A",
            ["RavenFocusRingBrush"] = "#CCE6D59A",
            ["RavenModalScrimBrush"] = "#8C000000",
        };

        foreach (var entry in expected)
        {
            var brush = Assert.IsType<SolidColorBrush>(
                application.Resources[entry.Key]);
            Assert.Equal(Color.Parse(entry.Value), brush.Color);
        }

        foreach (var resourceKey in new[]
                 {
                     "RavenSuccessBrush",
                     "RavenWarningBrush",
                 })
        {
            var color = Assert.IsType<SolidColorBrush>(
                application.Resources[resourceKey]).Color;
            Assert.Equal(color.R, color.G);
            Assert.Equal(color.G, color.B);
        }

        Assert.Equal(
            Color.Parse("#FF7B72"),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenDangerBrush"]).Color);

        Assert.Equal(
            Color.Parse("#F5F5F5"),
            Assert.IsType<Color>(application.Resources["SystemAccentColor"]));
        foreach (var resourceKey in new[]
                 {
                     "CheckBoxCheckGlyphForegroundChecked",
                     "CheckBoxCheckGlyphForegroundCheckedPointerOver",
                     "CheckBoxCheckGlyphForegroundCheckedPressed",
                 })
        {
            var brush = Assert.IsType<SolidColorBrush>(
                application.Resources[resourceKey]);
            Assert.Equal(Color.Parse("#0A0A0A"), brush.Color);
        }

        Assert.Equal(0, Assert.IsType<BoxShadows>(
            application.Resources["RavenWarningInsetShadow"]).Count);
        Assert.Equal(0, Assert.IsType<BoxShadows>(
            application.Resources["RavenFloatingPanelShadow"]).Count);
    }

    [Fact]
    public void EveryThemeUpdatesAvaloniaResourcesAndNativeMode()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var service = new RavenThemeService(application, store);
        service.ApplyCurrent();

        foreach (var theme in RavenThemeCatalog.All)
        {
            service.Select(theme.Key);

            Assert.Equal(theme, service.Current);
            Assert.Equal(
                theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light,
                application.RequestedThemeVariant);
            var accent = Assert.IsType<SolidColorBrush>(
                application.Resources["RavenAccentBrush"]);
            Assert.Equal(Color.Parse(theme.AccentColor), accent.Color);
            var selectedMutedText = Assert.IsType<SolidColorBrush>(
                application.Resources["RavenSelectedMutedTextBrush"]);
            Assert.Equal(
                Color.Parse(theme.Key == "monochrome-dark"
                    ? theme.AccentForegroundColor
                    : theme.MutedTextColor),
                selectedMutedText.Color);
            var warning = Assert.IsType<SolidColorBrush>(
                application.Resources["RavenWarningBrush"]);
            var warningShadow = Assert.IsType<BoxShadows>(
                application.Resources["RavenWarningInsetShadow"]);
            var floatingShadow = Assert.IsType<BoxShadows>(
                application.Resources["RavenFloatingPanelShadow"]);
            if (theme.UseSurfaceOnlyDepth)
            {
                Assert.Equal(0, warningShadow.Count);
                Assert.Equal(0, floatingShadow.Count);
            }
            else
            {
                Assert.Equal(1, warningShadow.Count);
                Assert.True(warningShadow[0].IsInset);
                Assert.Equal(
                    Color.FromArgb(
                        153,
                        warning.Color.R,
                        warning.Color.G,
                        warning.Color.B),
                    warningShadow[0].Color);
                Assert.Equal(1, floatingShadow.Count);
            }
        }

        Assert.Equal("monochrome-dark", store.LoadThemeKey());
    }

    [Fact]
    public void CustomLegacyPaletteCreatesOverlayAndNamedResources()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        colors["header"] = Color.FromArgb(255, 210, 180, 30);
        colors["orange"] = Color.FromArgb(255, 12, 34, 56);
        colors["orangeDark"] = Color.FromArgb(255, 65, 43, 21);
        colors["bio.confirmed"] = Color.FromArgb(255, 23, 45, 67);
        colors["bio.gold"] = Color.FromArgb(255, 78, 90, 12);
        colors["bio.goldFill"] = Color.FromArgb(255, 44, 55, 66);
        colors["bio.predictionEdge"] = Color.FromArgb(72, 12, 98, 123);
        colors["bio.goldDarkEdge"] = Color.FromArgb(91, 45, 67, 89);
        colors["bio.predictionSegmentEdge"] = Color.FromArgb(255, 9, 87, 65);
        colors["bio.galacticRegion"] = Color.FromArgb(255, 240, 241, 242);
        colors["bio.galacticRegionPotential"] = Color.FromArgb(255, 91, 92, 93);
        colors["bio.unknownGlyph"] = Color.FromArgb(255, 98, 76, 54);
        colors["guardian.primary"] = Color.FromArgb(255, 21, 42, 63);
        var typography = OverlayTypographySettings.Default with
        {
            Header = 11.5,
            Detail = 10.5,
        };
        var service = new RavenThemeService(
            application,
            store,
            new LegacyOverlayTheme(colors, true, null, typography));

        service.ApplyCurrent();

        Assert.Equal(
            Color.FromArgb(255, 210, 180, 30),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayHeaderBrush"]).Color);
        Assert.Equal(11.5, application.Resources["RavenOverlayHeaderFontSize"]);
        Assert.Equal(10.5, application.Resources["RavenOverlayDetailFontSize"]);
        Assert.Equal(
            Color.FromArgb(255, 12, 34, 56),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayAccentBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 65, 43, 21),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayAccentMutedBrush"]).Color);
        Assert.Equal(
            Color.Parse(RavenThemeCatalog.Get(null).AccentMutedColor),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenRouteGuidanceBadgeBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 78, 90, 12),
            Assert.IsType<SolidColorBrush>(
                application.Resources["LegacyTheme.bio.gold"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 78, 90, 12),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioGoldBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 44, 55, 66),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioGoldFillBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(72, 12, 98, 123),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioPredictionEdgeBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(91, 45, 67, 89),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioGoldDimEdgeBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 9, 87, 65),
            Assert.IsType<SolidColorBrush>(application.Resources[
                "RavenOverlayBioPredictionSegmentEdgeBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 23, 45, 67),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioConfirmedBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 240, 241, 242),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioGalacticRegionBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 91, 92, 93),
            Assert.IsType<SolidColorBrush>(application.Resources[
                "RavenOverlayBioGalacticRegionPotentialBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 98, 76, 54),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayBioUnknownGlyphBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 21, 42, 63),
            Assert.IsType<SolidColorBrush>(
                application.Resources["LegacyTheme.guardian.primary"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 21, 42, 63),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayGuardianPrimaryBrush"]).Color);
        foreach (var resource in new[]
                 {
                     "RavenOverlayPrimaryBrush",
                     "RavenOverlayPrimaryDimBrush",
                     "RavenOverlaySecondaryBrush",
                     "RavenOverlaySecondaryDimBrush",
                     "RavenOverlayDangerDimBrush",
                     "RavenOverlaySuccessDimBrush",
                     "RavenOverlayMenuGoldBrush",
                     "RavenOverlayBioConfirmedBrush",
                     "RavenOverlayBioConfirmedDimBrush",
                     "RavenOverlayBioPotentialBrush",
                     "RavenOverlayBioConfirmedDimPotentialBrush",
                     "RavenOverlayBioPredictionPotentialBrush",
                     "RavenOverlayBioGoldDimBrush",
                     "RavenOverlayBioGoldFillBrush",
                     "RavenOverlayBioGoldDimFillBrush",
                     "RavenOverlayBioGoldPotentialBrush",
                     "RavenOverlayBioGoldDimPotentialBrush",
                     "RavenOverlayBioGalacticRegionBrush",
                     "RavenOverlayBioGalacticRegionPotentialBrush",
                     "RavenOverlayBioUnknownBrush",
                     "RavenOverlayBioUnknownGlyphBrush",
                     "RavenOverlayBioHatchBrush",
                     "RavenOverlayBioEmptyBrush",
                     "RavenOverlayBioWhiteBrush",
                     "RavenOverlayBioPredictionBrush",
                     "RavenOverlayBioConfirmedEdgeBrush",
                     "RavenOverlayBioConfirmedDimEdgeBrush",
                     "RavenOverlayBioPredictionEdgeBrush",
                     "RavenOverlayBioGoldEdgeBrush",
                     "RavenOverlayBioGoldDimEdgeBrush",
                     "RavenOverlayBioGalacticRegionEdgeBrush",
                     "RavenOverlayBioUnknownEdgeBrush",
                     "RavenOverlayBioConfirmedSegmentEdgeBrush",
                     "RavenOverlayBioConfirmedPotentialSegmentEdgeBrush",
                     "RavenOverlayBioConfirmedDimSegmentEdgeBrush",
                     "RavenOverlayBioConfirmedDimPotentialSegmentEdgeBrush",
                     "RavenOverlayBioPredictionSegmentEdgeBrush",
                     "RavenOverlayBioPredictionPotentialSegmentEdgeBrush",
                     "RavenOverlayBioGoldSegmentEdgeBrush",
                     "RavenOverlayBioGoldPotentialSegmentEdgeBrush",
                     "RavenOverlayBioGoldDimSegmentEdgeBrush",
                     "RavenOverlayBioGoldDimPotentialSegmentEdgeBrush",
                     "RavenOverlayBioGalacticRegionSegmentEdgeBrush",
                     "RavenOverlayBioGalacticRegionPotentialSegmentEdgeBrush",
                     "RavenOverlayColoniseSurplusBrush",
                     "RavenOverlayColoniseSurplusDimBrush",
                     "RavenOverlayColoniseDeficitBrush",
                     "RavenOverlayColoniseDeficitDimBrush",
                     "RavenOverlayColoniseHighlightBrush",
                     "RavenOverlayColoniseItemBrush",
                     "RavenOverlayColoniseItemDimBrush",
                     "RavenOverlayFczCheckpointBrush",
                     "RavenOverlayFczCheckpointLocalBrush",
                     "RavenOverlayFczPowerPostBrush",
                     "RavenOverlayGuardianBackgroundBrush",
                     "RavenOverlayGuardianHeaderBrush",
                     "RavenOverlayGuardianPrimaryBrush",
                     "RavenOverlayGuardianPrimaryDimBrush",
                     "RavenOverlayGuardianSecondaryBrush",
                     "RavenOverlayGuardianSecondaryDimBrush",
                     "RavenOverlayGuardianTextBrush",
                     "RavenOverlayGuardianMutedBrush",
                     "RavenOverlayGuardianDangerBrush",
                     "RavenOverlayGuardianSuccessBrush",
                     "RavenOverlayGuardianWarningBrush",
                     "RavenOverlayGuardianSurfaceBrush",
                 })
        {
            Assert.IsType<SolidColorBrush>(application.Resources[resource]);
        }
        Assert.Equal(
            Color.Parse(RavenThemeCatalog.Get(null).AccentColor),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenAccentBrush"]).Color);
    }

    [Fact]
    public void SelectingApplicationThemeDoesNotReapplyOrChangeOverlayTheme()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        colors["orange"] = Color.FromArgb(255, 11, 22, 33);
        var overlay = new LegacyOverlayTheme(colors, true, null);
        var service = new RavenThemeService(application, store, overlay);
        var overlayChanges = 0;
        service.OverlayThemeChanged += (_, _) => overlayChanges++;
        service.ApplyCurrent();

        service.Select("monochrome-dark");

        Assert.Same(overlay, service.CurrentOverlayTheme);
        Assert.Equal(0, overlayChanges);
        Assert.Equal(
            Color.FromArgb(255, 11, 22, 33),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayAccentBrush"]).Color);
        Assert.Equal(
            Color.Parse("#E6D59A"),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenAccentBrush"]).Color);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
