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
        }

        Assert.Equal("green-dark", store.LoadThemeKey());
    }

    [Fact]
    public void CustomLegacyPaletteCreatesOverlayAndNamedResources()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
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
        var service = new RavenThemeService(
            application,
            store,
            new LegacyOverlayTheme(colors, true, null));

        service.ApplyCurrent();

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

        service.Select("green-light");

        Assert.Same(overlay, service.CurrentOverlayTheme);
        Assert.Equal(0, overlayChanges);
        Assert.Equal(
            Color.FromArgb(255, 11, 22, 33),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayAccentBrush"]).Color);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
