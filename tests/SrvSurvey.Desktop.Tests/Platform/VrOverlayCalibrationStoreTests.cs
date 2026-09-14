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
