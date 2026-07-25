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
            ShowNonBodySignals: true);

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
                + "\"HighGravityWarningLevel\":75,"
                + "\"SurfaceRadarSize\":99}}");

        var preferences = new SystemSurveySettingsStore(path).Load();

        Assert.Equal(0, preferences.FssBodyValueFloor);
        Assert.Equal(0, preferences.DssValueFloor);
        Assert.Equal(0, preferences.DssDistanceLimitLs);
        Assert.Equal(0, preferences.BodyInfoBubbleSizeLy);
        Assert.Equal(0, preferences.PriorScanMinimumValue);
        Assert.Equal(50, preferences.HighGravityWarningLevel);
        Assert.Equal(4, preferences.SurfaceRadarSize);
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
