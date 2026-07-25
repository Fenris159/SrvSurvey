using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class LegacyOverlayLayoutStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-layout-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LegacyAnchorsOffsetsOpacityCommentsAndVrSuffixArePreserved()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            """
            {
              // VR settings remain in the source and do not affect desktop placement.
              "PlotBodyInfo": "left:8, top:12, 0.75 { s: 10, p: <1, 2, 3>, r: <4, 5, 6>}",
              "PlotSysStatus": "right:18, bottom:44",
              "PlotAdjustVR": "screen:-100, os:25",
            }
            """);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "settings.json"),
            "{\"plotterOpacity\":55}");

        var layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Null(layout.Error);
        Assert.Equal(3, layout.Placements.Count);
        Assert.Equal(0.75, layout.GetOpacity("PlotBodyInfo"));
        Assert.Equal(0.55, layout.GetOpacity("PlotSysStatus"));
        Assert.Equal(
            new PixelPoint(108, 212),
            layout.GetPosition(
                "PlotBodyInfo",
                new PixelRect(100, 200, 1000, 800),
                new PixelSize(300, 120)));
        Assert.Equal(
            new PixelPoint(782, 836),
            layout.GetPosition(
                "PlotSysStatus",
                new PixelRect(100, 200, 1000, 800),
                new PixelSize(300, 120)));
        Assert.Equal(
            new PixelPoint(-100, 25),
            layout.GetPosition(
                "PlotAdjustVR",
                new PixelRect(100, 200, 1000, 800),
                new PixelSize(300, 120)));
    }

    [Fact]
    public void InvalidLayoutFallsBackWithoutChangingImportedFiles()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        const string invalid = "{\"PlotBodyInfo\":\"diagonal:8,top:8\"}";
        File.WriteAllText(path, invalid);

        var layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Empty(layout.Placements);
        Assert.NotNull(layout.Error);
        Assert.Contains("unknown horizontal anchor", layout.Error);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void MissingFilesUseAvaloniaDefaults()
    {
        var layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Empty(layout.Placements);
        Assert.Null(layout.DefaultOpacity);
        Assert.Null(layout.Error);
        Assert.Null(layout.GetPosition(
            "PlotJumpInfo",
            new PixelRect(0, 0, 100, 100),
            new PixelSize(20, 20)));
        Assert.Null(layout.GetOpacity("PlotJumpInfo"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
