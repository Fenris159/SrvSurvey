using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class SystemSurveySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemSurveySettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingDocumentUsesLegacyCompatibleDefaults()
    {
        var preferences = CreateStore().Load();

        Assert.Equal(SystemSurveyPreferences.Default, preferences);
        Assert.Equal(0, preferences.BodyPredictionPreviewExtensionSeconds);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"Blue-dark\"}");
        var store = new SystemSurveySettingsStore(path);
        var expected = new SystemSurveyPreferences(
            AutoShowBodyInfo: false,
            ShowBodyInfoInSystemMap: false,
            BodyPredictionPreviewExtensionSeconds: 45,
            ShowBodyInfoInOrbit: false,
            ShowBodyInfoAtSurface: true,
            HideBodyInfoInBubble: false,
            BodyInfoBubbleSizeLy: 150,
            HideBodyInfoMaterials: true,
            AutoShowFlightWarnings: false,
            HighGravityWarningLevel: 2.5,
            UseExternalData: false,
            UseExternalBioData: true,
            AutoShowBioSystem: false,
            AutoShowBioStatus: true,
            AutoHideBioPlotOnRepeat: false,
            KeepBioPlottersVisibleAfterDss: false,
            BioPlotterDssDurationSeconds: 300,
            AutoShowPriorScans: false,
            SkipPriorScansLowValue: true,
            PriorScanMinimumValue: 2_000_000,
            HideOwnCanonnSignals: false,
            ShowCanonnSignalsOnRadar: false,
            UseSmallCanonnRadarCircles: false,
            AutoShowSurfaceRadar: false,
            AutoShowMiniTrack: true,
            SurfaceRadarSize: 4,
            AutoHideSurfaceRadarWithoutLandingGear: true,
            AutoRemoveTrackerOnSampling: false,
            AutoRemoveTrackerOnFinalSample: true,
            AutoTrackCompositionScans: false,
            SkipAnalyzedCompositionScans: false,
            DrawBodyBiosOnlyWhenNear: false,
            HighlightRegionalFirsts: true,
            DimAnalyzedOrganisms: false,
            HideGeoCountInBioSystem: true,
            DisableBioPredictions: true,
            ShowTemperatureRangeDebug: true,
            AutoShowLastFssBody: false,
            AutoShowFssInfo: false,
            ShowFssInfoInSystemMap: true,
            ShowFssInfoInNavigationPanel: true,
            AutoShowSystemStatus: false,
            HideGeoCount: true,
            FssBodyValueFloor: 25_000,
            HighlightDssCandidates: false,
            DssValueFloor: 750_000,
            SkipDistantDssCandidates: true,
            DssDistanceLimitLs: 50_000,
            SkipGasGiantsForDss: false,
            SkipRingsForDss: false,
            ShowNonBodySignals: true,
            FssTuningDetector: new FssTuningDetectorSettings(
                false,
                true,
                new FssPixelColor(1, 2, 3, 4),
                5,
                new FssPixelColor(6, 7, 8, 9),
                new FssPixelColor(10, 11, 12, 13),
                new FssPixelColor(14, 15, 16, 17)),
            SuppressForActiveBuildProjects: true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("Blue-dark", File.ReadAllText(path));
    }

    [Fact]
    public void NegativeNumericValuesAreClamped()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"SystemSurvey\":{\"FssBodyValueFloor\":-1,"
                + "\"DssValueFloor\":-2,\"DssDistanceLimitLs\":-3,"
                + "\"BodyInfoBubbleSizeLy\":-4,"
                + "\"PriorScanMinimumValue\":-5,"
                + "\"BodyPredictionPreviewExtensionSeconds\":999,"
                + "\"BioPlotterDssDurationSeconds\":999,"
                + "\"HighGravityWarningLevel\":75,"
                + "\"SurfaceRadarSize\":99,"
                + "\"FssTuningDetector\":{"
                + "\"YellowHorizontalTolerance\":999,"
                + "\"YellowBar\":{\"Red\":-1,\"Green\":999}}}}");

        var preferences = new SystemSurveySettingsStore(path).Load();

        Assert.Equal(0, preferences.FssBodyValueFloor);
        Assert.Equal(0, preferences.DssValueFloor);
        Assert.Equal(0, preferences.DssDistanceLimitLs);
        Assert.Equal(0, preferences.BodyInfoBubbleSizeLy);
        Assert.Equal(0, preferences.PriorScanMinimumValue);
        Assert.Equal(600, preferences.BodyPredictionPreviewExtensionSeconds);
        Assert.Equal(600, preferences.BioPlotterDssDurationSeconds);
        Assert.Equal(50, preferences.HighGravityWarningLevel);
        Assert.Equal(4, preferences.SurfaceRadarSize);
        Assert.Equal(
            255,
            preferences.FssTuningDetector.YellowHorizontalTolerance);
        Assert.Equal(0, preferences.FssTuningDetector.YellowBar.Red);
        Assert.Equal(255, preferences.FssTuningDetector.YellowBar.Green);
        Assert.Equal(
            FssTuningDetectorSettings.Default.YellowBar.Blue,
            preferences.FssTuningDetector.YellowBar.Blue);
    }

    [Fact]
    public void SavingDetectorSettingsPreservesFutureNestedValues()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"SystemSurvey\":{\"FssTuningDetector\":{"
                + "\"FutureOption\":42,"
                + "\"YellowBar\":{\"FutureColor\":true}}}}");
        var store = new SystemSurveySettingsStore(path);
        var preferences = store.Load() with
        {
            FssTuningDetector = FssTuningDetectorSettings.Default with
            {
                Enabled = false,
            },
        };

        store.Save(preferences);

        var json = File.ReadAllText(path);
        Assert.Contains("FutureOption", json);
        Assert.Contains("FutureColor", json);
        Assert.False(store.Load().FssTuningDetector.Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private SystemSurveySettingsStore CreateStore()
    {
        return new SystemSurveySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
