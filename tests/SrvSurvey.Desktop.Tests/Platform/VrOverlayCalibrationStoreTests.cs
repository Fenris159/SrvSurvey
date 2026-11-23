using System.Numerics;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrOverlayCalibrationStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-calibration-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void VdxrInheritsExistingCustomPlacementsAndModeOverridesWithoutRewritingThem()
    {
        (string Data, string Factory) paths = CreateFiles();
        var store = new VrOverlayCalibrationStore(paths.Data, paths.Factory);
        const string profile = VrPlatformProfileCatalog.MetaVdxrProfileId;
        VrOverlayCalibrationCatalog catalog = store.Load(profile);
        Assert.Equal(12, catalog.Defaults["PlotJumpInfo"].Scale);
        Assert.Equal(18, catalog.Resolve("PlotJumpInfo", "testbuggy")!.Scale);
        var saved = new VrOverlayCalibration(7, new Vector3(0, 1, 9), Vector3.Zero);
        store.Save("PlotJumpInfo", saved, "testbuggy", profile);
        VrOverlayCalibrationSaveResult result = store.Save(
            "PlotJumpInfo",
            saved with
            {
                Scale = 6,
            },
            "testbuggy",
            profile
        );
        Assert.NotNull(result.BackupPath);
        Assert.Equal(6, store.Load(profile).Resolve("PlotJumpInfo", "testbuggy")!.Scale);
        Assert.Equal(18, store.Load().Resolve("PlotJumpInfo", "testbuggy")!.Scale);
        Assert.Equal(12, store.Load().Defaults["PlotJumpInfo"].Scale);
    }

    [Theory]
    [InlineData(VrPlatformProfileCatalog.WivrnOpenXrProfileId)]
    [InlineData(VrPlatformProfileCatalog.MonadoOpenXrProfileId)]
    public void LinuxOpenXrRoutesUseSeatedDefaultsAndIsolateSavedPlacements(string profile)
    {
        (string Data, string Factory) paths = CreateFiles();
        var store = new VrOverlayCalibrationStore(paths.Data, paths.Factory);

        VrOverlayCalibrationCatalog catalog = store.Load(profile);
        Assert.Equal(8, catalog.FactoryDefaults["PlotJumpInfo"].Scale);

        var custom = new VrOverlayCalibration(6.5f, new Vector3(1, 2, 9), Vector3.Zero);
        store.Save("PlotJumpInfo", custom, null, profile);

        Assert.Equal(6.5f, store.Load(profile).Defaults["PlotJumpInfo"].Scale);
        Assert.Equal(12, store.Load().Defaults["PlotJumpInfo"].Scale);
        Assert.Equal(12, store.Load(VrPlatformProfileCatalog.MetaVdxrProfileId).Defaults["PlotJumpInfo"].Scale);
    }

    [Theory]
    [InlineData("PlotJumpInfo", "{ s: 8, p: <0, 4.5, 11>, r: <22.2, 0, 0>}")]
    [InlineData("PlotBioStatus", "{ s: 3.5, p: <0, 4.5, 11>, r: <22.2, 0, 0>}")]
    [InlineData("PlotSysStatus", "{ s: 1.9, p: <-6, -3, 9.5>, r: <-14.9, 32.3, 0>}")]
    [InlineData("PlotBioSystem", "{ s: 3.2, p: <-6, -0.5, 9.5>, r: <-2.5, 32.3, 0>}")]
    [InlineData("PlotBuildCommodities", "{ s: 3.6, p: <6, 4.5, 9.5>, r: <21.8, -32.3, 0>}")]
    [InlineData("PlotPulse", "{ s: 0.8, p: <-6, -3, 9.5>, r: <-14.9, 32.3, 0>}")]
    [InlineData("PlotUnknownLegacy", "{ s: 4, p: <0, 0, 11>, r: <0, 0, 0>}")]
    public void SeatedDefaultsUseDesktopMirroredCockpitPlacements(string name, string expected)
    {
        Assert.Equal(expected, VrSeatedCalibrationDefaults.Get(name).ToString());
    }

    [Fact]
    public void EverySupportedOverlayHasProportionalEyeFacingSeatedAndFactoryDefaults()
    {
        var store = new VrOverlayCalibrationStore(temporaryDirectory);
        VrOverlayCalibrationCatalog steamVr = store.Load();
        VrOverlayCalibrationCatalog openXr = store.Load(VrPlatformProfileCatalog.WivrnOpenXrProfileId);

        foreach (OverlayLayoutDefinition definition in OverlayLayoutCatalog.Supported)
        {
            Assert.True(
                steamVr.FactoryDefaults.ContainsKey(definition.Name),
                $"Missing SteamVR default for {definition.Name}"
            );
            VrOverlayCalibration seated = openXr.FactoryDefaults[definition.Name];
            Assert.InRange(seated.Scale, 0.8f, 8f);
            Assert.Equal(0f, seated.Rotation.Z);

            var panelPosition = new Vector3(seated.Position.X / 10f, seated.Position.Y / 10f, -seated.Position.Z / 10f);
            Vector3 radians = seated.Rotation * (MathF.PI / 180f);
            var orientation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
            var normal = Vector3.Transform(Vector3.UnitZ, orientation);
            var right = Vector3.Transform(Vector3.UnitX, orientation);
            var toEye = Vector3.Normalize(-panelPosition);
            Assert.True(Vector3.Dot(normal, toEye) > 0.999f, $"{definition.Name} does not face seated origin.");
            Assert.Equal(0f, right.Y, 4);
        }
    }

    [Fact]
    public void LegacyCalibrationRoundTripsUsingInvariantFormatting()
    {
        const string legacy =
            "center:0, middle:0 { s: 10.090002, "
            + "p: <-0.42953613, 14.988647, 33.43891>, "
            + "r: <-0.13900007, 0, -0.98910034>}";

        var calibration = VrOverlayCalibration.Parse(legacy);

        Assert.NotNull(calibration);
        Assert.Equal(calibration, VrOverlayCalibration.Parse(calibration.ToString(), allowDesktopPrefix: false));
    }

    [Fact]
    public void LoadsLegacyDefaultsAndVehicleOverridesWithoutChangingFiles()
    {
        (string Data, string Factory) paths = CreateFiles();
        string plottersPath = Path.Combine(paths.Data, "plotters.json");
        string overridePath = Path.Combine(paths.Data, "vr", "testbuggy.json");
        string originalPlotters = File.ReadAllText(plottersPath);
        string originalOverride = File.ReadAllText(overridePath);

        VrOverlayCalibrationCatalog catalog = new VrOverlayCalibrationStore(paths.Data, paths.Factory).Load();

        Assert.Equal(12, catalog.Resolve("PlotJumpInfo", null)!.Scale);
        Assert.Equal(18, catalog.Resolve("PlotJumpInfo", "testbuggy")!.Scale);
        Assert.Equal(originalPlotters, File.ReadAllText(plottersPath));
        Assert.Equal(originalOverride, File.ReadAllText(overridePath));
    }

    [Fact]
    public void DefaultSaveIsVerifiedBackedUpAndPreservesDesktopAndFutureEntries()
    {
        (string Data, string Factory) paths = CreateFiles();
        string plottersPath = Path.Combine(paths.Data, "plotters.json");
        string original = File.ReadAllText(plottersPath);
        var store = new VrOverlayCalibrationStore(paths.Data, paths.Factory);
        var calibration = new VrOverlayCalibration(21.5f, new Vector3(1, 2, 3), new Vector3(4, 5, 6));

        VrOverlayCalibrationSaveResult result = store.Save("PlotJumpInfo", calibration);

        Assert.Equal(plottersPath, result.Path);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllText(result.BackupPath));
        string saved = File.ReadAllText(plottersPath);
        Assert.Contains("center:0, top:8", saved);
        Assert.Contains("\"FutureOverlay\"", saved);
        Assert.Equal(calibration, store.Load().Defaults["PlotJumpInfo"]);
    }

    [Fact]
    public void ModeSaveIsAtomicAndPreservesOtherOverrideEntries()
    {
        (string Data, string Factory) paths = CreateFiles();
        var store = new VrOverlayCalibrationStore(paths.Data, paths.Factory);
        var calibration = new VrOverlayCalibration(25, new Vector3(-1, -2, -3), new Vector3(7, 8, 9));

        VrOverlayCalibrationSaveResult result = store.Save("PlotJumpInfo", calibration, "testbuggy");

        Assert.NotNull(result.BackupPath);
        Assert.Contains("\"FutureOverlay\"", File.ReadAllText(result.Path));
        Assert.Equal(calibration, store.Load().Resolve("PlotJumpInfo", "testbuggy"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("nested/mode")]
    public void UnsafeModeCannotEscapeTheProfileDirectory(string mode)
    {
        (string Data, string Factory) paths = CreateFiles();
        var store = new VrOverlayCalibrationStore(paths.Data, paths.Factory);

        Assert.Throws<InvalidDataException>(() =>
            store.Save("PlotJumpInfo", new VrOverlayCalibration(10, Vector3.Zero, Vector3.Zero), mode)
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private (string Data, string Factory) CreateFiles()
    {
        string data = Path.Combine(temporaryDirectory, "data");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(factoryDirectory);
        Directory.CreateDirectory(Path.Combine(data, "vr"));
        string factory = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(factory, "{\"PlotJumpInfo\":\"center:0, top:8 " + "{ s: 10, p: <1, 2, 3>, r: <4, 5, 6>}\"}");
        File.WriteAllText(
            Path.Combine(data, "plotters.json"),
            "{\"PlotJumpInfo\":\"center:0, top:8 "
                + "{ s: 12, p: <1, 2, 3>, r: <4, 5, 6>}\","
                + "\"FutureOverlay\":\"right:4, bottom:5\"}"
        );
        File.WriteAllText(
            Path.Combine(data, "vr", "testbuggy.json"),
            "{\"PlotJumpInfo\":\"{ s: 18, p: <7, 8, 9>, "
                + "r: <10, 11, 12>}\",\"FutureOverlay\":"
                + "\"{ s: 13, p: <1, 1, 1>, r: <2, 2, 2>}\"}"
        );
        return (data, factory);
    }
}
