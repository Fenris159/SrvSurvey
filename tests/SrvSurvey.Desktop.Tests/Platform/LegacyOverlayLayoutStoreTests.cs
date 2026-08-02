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

    [Fact]
    public void SaveIsAtomicBackedUpAndPreservesUnknownEntriesAndVrCalibration()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        const string original =
            "{\"FutureOverlay\":\"right:99,bottom:77\","
            + "\"PlotBodyInfo\":\"left:8,top:12,0.75 "
            + "{ s: 10, p: <1, 2, 3>, r: <4, 5, 6>}\"}";
        File.WriteAllText(path, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        var result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBodyInfo"] = new(
                    LegacyHorizontalAnchor.Screen,
                    -120,
                    LegacyVerticalAnchor.Middle,
                    45,
                    0),
            });

        Assert.Equal(path, result.Path);
        Assert.Equal(1, result.UpdatedPlacementCount);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllText(result.BackupPath!));
        var savedText = File.ReadAllText(path);
        Assert.Contains("os:-120, middle:45, 0", savedText);
        Assert.Contains(
            "{ s: 10, p: <1, 2, 3>, r: <4, 5, 6>}",
            savedText);

        var layout = store.Load();
        Assert.Null(layout.Error);
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Right,
                99,
                LegacyVerticalAnchor.Bottom,
                77,
                null),
            layout.Placements["FutureOverlay"]);
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Screen,
                -120,
                LegacyVerticalAnchor.Middle,
                45,
                0),
            layout.Placements["PlotBodyInfo"]);
    }

    [Fact]
    public void SaveRefusesMalformedInputWithoutChangingIt()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        const string original = "{\"PlotBodyInfo\":\"diagonal:8,top:8\"}";
        File.WriteAllText(path, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        var exception = Assert.Throws<InvalidDataException>(() => store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBodyInfo"] = new(
                    LegacyHorizontalAnchor.Left,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null),
            }));

        Assert.Contains("unknown horizontal anchor", exception.Message);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(
            temporaryDirectory,
            "overlay-layout-backups")));
    }

    [Fact]
    public void SaveCreatesNewLayoutWhenNoLegacyFileExists()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        var result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(
                    LegacyHorizontalAnchor.Center,
                    0,
                    LegacyVerticalAnchor.Top,
                    8,
                    null),
            });

        Assert.Null(result.BackupPath);
        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Center,
                0,
                LegacyVerticalAnchor.Top,
                8,
                null),
            store.Load().Placements["PlotJumpInfo"]);
    }

    [Fact]
    public void GlobalOpacitySaveIsBackedUpAndPreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string original = "{\"futureSetting\":true,\"plotterOpacity\":55}";
        File.WriteAllText(settingsPath, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        var result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>(),
            0.42,
            updateDefaultOpacity: true);

        Assert.True(result.UpdatedDefaultOpacity);
        Assert.Equal(0, result.UpdatedPlacementCount);
        Assert.NotNull(result.SettingsBackupPath);
        Assert.Equal(original, File.ReadAllText(result.SettingsBackupPath!));
        Assert.Contains("\"futureSetting\": true", File.ReadAllText(settingsPath));
        Assert.Equal(0.42, store.Load().DefaultOpacity);
    }

    [Fact]
    public void PerOverlayScaleIsStoredSeparatelyFromLegacyPlacementText()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        var result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotRouteBio"] = new(
                    LegacyHorizontalAnchor.Right,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null,
                    ScaleIndex: 15),
            });
        var layout = store.Load();
        layout.SetScaleIndex(24);

        Assert.Equal(1, result.UpdatedScaleOverrideCount);
        Assert.Equal(15, layout.GetScaleIndex("PlotRouteBio"));
        Assert.Equal(24, layout.GetScaleIndex("PlotJumpInfo"));
        Assert.DoesNotContain(
            "15",
            File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
        Assert.Contains(
            "\"PlotRouteBio\": 15",
            File.ReadAllText(Path.Combine(
                temporaryDirectory,
                "overlay-scale-overrides.json")));
    }

    [Fact]
    public void ReplacingPositionsPreservesIndependentGlobalScale()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(),
            null,
            null);
        active.SetScaleIndex(19);
        var updated = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(
                    LegacyHorizontalAnchor.Center,
                    0,
                    LegacyVerticalAnchor.Top,
                    8,
                    null),
            },
            0.5,
            null);

        active.ReplaceWith(updated);

        Assert.Equal(19, active.ScaleIndex);
        Assert.Single(active.Placements);
        Assert.Equal(0.5, active.DefaultOpacity);
    }

    [Fact]
    public void UpdatingOneRuntimePlacementPreservesOtherLayoutState()
    {
        var original = new LegacyOverlayPlacement(
            LegacyHorizontalAnchor.Center,
            0,
            LegacyVerticalAnchor.Top,
            8,
            0.75);
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = original,
            },
            0.5,
            null);
        active.SetScaleIndex(20);
        var updated = original with { HorizontalOffset = 42 };

        Assert.True(active.SetPlacement("PlotJumpInfo", updated));
        Assert.False(active.SetPlacement("PlotJumpInfo", updated));

        Assert.Equal(updated, active.Placements["PlotJumpInfo"]);
        Assert.Equal(0.5, active.DefaultOpacity);
        Assert.Equal(20, active.ScaleIndex);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
