using System.Text.Json;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class LegacyOverlayLayoutStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-layout-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void MiningWarningInitiallyCopiesFlightPlacementButKeepsIndependentSavedChanges()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            """{"PlotFlightWarning":"center:9, top:176, 0.7 { s: 13 }"}"""
        );
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        LegacyOverlayLayout layout = store.Load();
        LegacyOverlayPlacement original = layout.Placements["PlotFlightWarning"];
        Assert.Equal(original, layout.Placements["PlotMiningWarning"]);
        LegacyOverlayPlacement custom = original with { HorizontalOffset = 75, VerticalOffset = 200 };
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotMiningWarning"] = custom });
        store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotFlightWarning"] = original with { HorizontalOffset = 99 },
            }
        );
        Assert.Equal(custom, store.Load().Placements["PlotMiningWarning"]);
    }

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
            """
        );
        File.WriteAllText(Path.Combine(temporaryDirectory, "settings.json"), "{\"plotterOpacity\":55}");

        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Null(layout.Error);
        Assert.Equal(3, layout.Placements.Count);
        Assert.Equal(0.75, layout.GetOpacity("PlotBodyInfo"));
        Assert.Equal(0.55, layout.GetOpacity("PlotSysStatus"));
        Assert.Equal(
            new PixelPoint(108, 212),
            layout.GetPosition("PlotBodyInfo", new PixelRect(100, 200, 1000, 800), new PixelSize(300, 120))
        );
        Assert.Equal(
            new PixelPoint(782, 836),
            layout.GetPosition("PlotSysStatus", new PixelRect(100, 200, 1000, 800), new PixelSize(300, 120))
        );
        Assert.Equal(
            new PixelPoint(-100, 25),
            layout.GetPosition("PlotAdjustVR", new PixelRect(100, 200, 1000, 800), new PixelSize(300, 120))
        );
    }

    [Fact]
    public void ReferencedPlacementsScaleOffsetsWithTheCurrentGameBounds()
    {
        var reference = new OverlayPositionReference(3840, 2160);
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["left"] = new(
                    LegacyHorizontalAnchor.Left,
                    960,
                    LegacyVerticalAnchor.Top,
                    540,
                    null,
                    PositionReference: reference
                ),
                ["right"] = new(
                    LegacyHorizontalAnchor.Right,
                    480,
                    LegacyVerticalAnchor.Bottom,
                    270,
                    null,
                    PositionReference: reference
                ),
            },
            null,
            null
        );
        var gameBounds = new PixelRect(100, 200, 2560, 1440);
        var overlaySize = new PixelSize(300, 120);

        Assert.Equal(new PixelPoint(740, 560), layout.GetPosition("left", gameBounds, overlaySize));
        Assert.Equal(new PixelPoint(2040, 1340), layout.GetPosition("right", gameBounds, overlaySize));
    }

    [Fact]
    public void InvalidLayoutFallsBackWithoutChangingImportedFiles()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "plotters.json");
        const string invalid = "{\"PlotBodyInfo\":\"diagonal:8,top:8\"}";
        File.WriteAllText(path, invalid);

        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Empty(layout.Placements);
        Assert.NotNull(layout.Error);
        Assert.Contains("unknown horizontal anchor", layout.Error);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void MissingFilesUseAvaloniaDefaults()
    {
        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Empty(layout.Placements);
        Assert.Null(layout.DefaultOpacity);
        Assert.Null(layout.Error);
        Assert.Null(layout.GetPosition("PlotJumpInfo", new PixelRect(0, 0, 100, 100), new PixelSize(20, 20)));
        Assert.Null(layout.GetOpacity("PlotJumpInfo"));
    }

    [Fact]
    public void LegacyScaleOverrideMigrationUsesDisplayBaselineCreatesBackupAndRunsOnce()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(Path.Combine(temporaryDirectory, "plotters.json"), "{\"PlotJumpInfo\":\"center:0, top:8\"}");
        string overridesPath = Path.Combine(temporaryDirectory, "overlay-scale-overrides.json");
        const string original = "{\"PlotJumpInfo\":15}";
        File.WriteAllText(overridesPath, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        LegacyOverlayScaleMigrationResult first = store.MigrateLegacyScaleOverrides(1.5);
        LegacyOverlayScaleMigrationResult second = store.MigrateLegacyScaleOverrides(1.5);

        Assert.Equal(1, first.MigratedCount);
        Assert.NotNull(first.BackupPath);
        Assert.Equal(original, File.ReadAllText(first.BackupPath));
        Assert.Equal(OverlayScaleCatalog.GetIndex(45), store.Load().Placements["PlotJumpInfo"].ScaleIndex);
        Assert.Equal(0, second.MigratedCount);
    }

    [Fact]
    public void SaveIsAtomicBackedUpAndPreservesUnknownEntriesAndVrCalibration()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "plotters.json");
        const string original =
            "{\"FutureOverlay\":\"right:99,bottom:77\","
            + "\"PlotBodyInfo\":\"left:8,top:12,0.75 "
            + "{ s: 10, p: <1, 2, 3>, r: <4, 5, 6>}\"}";
        File.WriteAllText(path, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        LegacyOverlayLayoutSaveResult result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBodyInfo"] = new(LegacyHorizontalAnchor.Screen, -120, LegacyVerticalAnchor.Middle, 45, 0),
            }
        );

        Assert.Equal(path, result.Path);
        Assert.Equal(1, result.UpdatedPlacementCount);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllText(result.BackupPath));
        string savedText = File.ReadAllText(path);
        Assert.Contains("os:-120, middle:45, 0", savedText);
        Assert.Contains("{ s: 10, p: <1, 2, 3>, r: <4, 5, 6>}", savedText);

        LegacyOverlayLayout layout = store.Load();
        Assert.Null(layout.Error);
        Assert.Equal(
            new LegacyOverlayPlacement(LegacyHorizontalAnchor.Right, 99, LegacyVerticalAnchor.Bottom, 77, null),
            layout.Placements["FutureOverlay"]
        );
        Assert.Equal(
            new LegacyOverlayPlacement(LegacyHorizontalAnchor.Screen, -120, LegacyVerticalAnchor.Middle, 45, 0),
            layout.Placements["PlotBodyInfo"]
        );
    }

    [Fact]
    public void SavePreservesBothLayoutFilesWhenPositionReferencePreparationFails()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string plottersPath = Path.Combine(temporaryDirectory, "plotters.json");
        string positionReferencesPath = Path.Combine(temporaryDirectory, "overlay-position-references.json");
        const string originalPlotters = "{\"PlotBodyInfo\":\"left:8,top:12\"}";
        const string originalPositionReferences = "{not-json";
        File.WriteAllText(plottersPath, originalPlotters);
        File.WriteAllText(positionReferencesPath, originalPositionReferences);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        Assert.ThrowsAny<JsonException>(() =>
            store.Save(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    ["PlotBodyInfo"] = new(
                        LegacyHorizontalAnchor.Right,
                        24,
                        LegacyVerticalAnchor.Bottom,
                        36,
                        null,
                        PositionReference: new OverlayPositionReference(2560, 1440)
                    ),
                }
            )
        );

        Assert.Equal(originalPlotters, File.ReadAllText(plottersPath));
        Assert.Equal(originalPositionReferences, File.ReadAllText(positionReferencesPath));
    }

    [Fact]
    public void SaveRestoresPlottersWhenPositionReferenceReplacementFails()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string plottersPath = Path.Combine(temporaryDirectory, "plotters.json");
        string positionReferencesPath = Path.Combine(temporaryDirectory, "overlay-position-references.json");
        const string originalPlotters = "{\"PlotBodyInfo\":\"left:8,top:12\"}";
        File.WriteAllText(plottersPath, originalPlotters);
        Directory.CreateDirectory(positionReferencesPath);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        Exception replacementException = Assert.ThrowsAny<Exception>(() =>
            store.Save(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    ["PlotBodyInfo"] = new(
                        LegacyHorizontalAnchor.Right,
                        24,
                        LegacyVerticalAnchor.Bottom,
                        36,
                        null,
                        PositionReference: new OverlayPositionReference(2560, 1440)
                    ),
                }
            )
        );
        Assert.True(replacementException is IOException or UnauthorizedAccessException);

        Assert.Equal(originalPlotters, File.ReadAllText(plottersPath));
        Assert.True(Directory.Exists(positionReferencesPath));
    }

    [Fact]
    public void SaveRefusesMalformedInputWithoutChangingIt()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "plotters.json");
        const string original = "{\"PlotBodyInfo\":\"diagonal:8,top:8\"}";
        File.WriteAllText(path, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            store.Save(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    ["PlotBodyInfo"] = new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Top, 8, null),
                }
            )
        );

        Assert.Contains("unknown horizontal anchor", exception.Message);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory, "overlay-layout-backups")));
    }

    [Fact]
    public void SaveCreatesNewLayoutWhenNoLegacyFileExists()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        LegacyOverlayLayoutSaveResult result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, null),
            }
        );

        Assert.Null(result.BackupPath);
        Assert.Equal(
            new LegacyOverlayPlacement(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, null),
            store.Load().Placements["PlotJumpInfo"]
        );
    }

    [Fact]
    public void GlobalOpacitySaveIsBackedUpAndPreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string original = "{\"futureSetting\":true,\"plotterOpacity\":55}";
        File.WriteAllText(settingsPath, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        LegacyOverlayLayoutSaveResult result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>(),
            0.42,
            updateDefaultOpacity: true
        );

        Assert.True(result.UpdatedDefaultOpacity);
        Assert.Equal(0, result.UpdatedPlacementCount);
        Assert.NotNull(result.SettingsBackupPath);
        Assert.Equal(original, File.ReadAllText(result.SettingsBackupPath));
        Assert.Contains("\"futureSetting\": true", File.ReadAllText(settingsPath));
        Assert.Equal(0.42, store.Load().DefaultOpacity);
    }

    [Fact]
    public void PerOverlayScaleIsStoredSeparatelyFromLegacyPlacementText()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);

        LegacyOverlayLayoutSaveResult result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotRouteBio"] = new(
                    LegacyHorizontalAnchor.Right,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null,
                    ScaleIndex: 15
                ),
            }
        );
        LegacyOverlayLayout layout = store.Load();
        layout.SetScaleIndex(24);

        Assert.Equal(1, result.UpdatedScaleOverrideCount);
        Assert.Equal(15, layout.GetScaleIndex("PlotRouteBio"));
        Assert.Equal(24, layout.GetScaleIndex("PlotJumpInfo"));
        Assert.DoesNotContain("15", File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
        Assert.Contains(
            "\"PlotRouteBio\": 15",
            File.ReadAllText(Path.Combine(temporaryDirectory, "overlay-scale-overrides.json"))
        );
    }

    [Fact]
    public void PerOverlayTypographyIsStoredSeparatelyAndDefaultsToBaseline()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var typography = new OverlayTypographyScale(10, 25, 0, -10, 15, 0, 20);

        LegacyOverlayLayoutSaveResult result = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotFSSInfo"] = new(
                    LegacyHorizontalAnchor.Left,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null,
                    TypographyScale: typography
                ),
            }
        );
        LegacyOverlayLayout layout = store.Load();

        Assert.Equal(1, result.UpdatedTypographyOverrideCount);
        Assert.Equal(typography, layout.GetTypographyScale("PlotFSSInfo"));
        Assert.Equal(OverlayTypographyScale.Default, layout.GetTypographyScale("PlotJumpInfo"));
        Assert.DoesNotContain("title", File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
        Assert.Contains(
            "\"title\": 25",
            File.ReadAllText(Path.Combine(temporaryDirectory, "overlay-typography-overrides.json"))
        );
        Assert.Contains(
            "\"icons\": 20",
            File.ReadAllText(Path.Combine(temporaryDirectory, "overlay-typography-overrides.json"))
        );
    }

    [Fact]
    public void TypographySavedBeforeIconScalingDefaultsIconsToBaseline()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(Path.Combine(temporaryDirectory, "plotters.json"), "{\"PlotFSSInfo\":\"left:8, top:8\"}");
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "overlay-typography-overrides.json"),
            "{\"PlotFSSInfo\":{\"header\":0,\"title\":25,\"value\":0,\"body\":0,\"detail\":0,\"caption\":0}}"
        );

        OverlayTypographyScale scale = new LegacyOverlayLayoutStore(temporaryDirectory)
            .Load()
            .GetTypographyScale("PlotFSSInfo");

        Assert.Equal(25, scale.Title);
        Assert.Equal(0, scale.Icons);
    }

    [Fact]
    public void ResettingPerOverlayTypographyRemovesTheSavedOverride()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        LegacyOverlayPlacement placement = OverlayLayoutCatalog.GetRequired("PlotFSSInfo").DefaultPlacement with
        {
            TypographyScale = new OverlayTypographyScale(0, 25, 0, 0, 0, 0),
        };
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotFSSInfo"] = placement });

        LegacyOverlayLayoutSaveResult unchanged = store.Save(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotFSSInfo"] = placement }
        );
        LegacyOverlayLayoutSaveResult reset = store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotFSSInfo"] = placement with { TypographyScale = OverlayTypographyScale.Default },
            }
        );

        Assert.Equal(0, unchanged.UpdatedTypographyOverrideCount);
        Assert.Equal(1, reset.UpdatedTypographyOverrideCount);
        Assert.NotNull(reset.TypographyOverridesBackupPath);
        Assert.DoesNotContain(
            "PlotFSSInfo",
            File.ReadAllText(Path.Combine(temporaryDirectory, "overlay-typography-overrides.json"))
        );
        Assert.Equal(OverlayTypographyScale.Default, store.Load().GetTypographyScale("PlotFSSInfo"));
    }

    [Fact]
    public void PerOverlaySizeIsStoredSeparatelyAndCanBeReset()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        LegacyOverlayPlacement placement = OverlayLayoutCatalog.GetRequired("PlotFSSInfo").DefaultPlacement with
        {
            SizeOverride = new OverlayPanelSize(420, 280),
        };

        LegacyOverlayLayoutSaveResult saved = store.Save(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotFSSInfo"] = placement }
        );
        LegacyOverlayLayout loaded = store.Load();

        Assert.Equal(1, saved.UpdatedSizeOverrideCount);
        Assert.Equal(new OverlayPanelSize(420, 280), loaded.GetSizeOverride("PlotFSSInfo"));
        Assert.DoesNotContain("width", File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
        Assert.Contains(
            "\"width\": 420",
            File.ReadAllText(Path.Combine(temporaryDirectory, "overlay-size-overrides.json"))
        );

        LegacyOverlayLayoutSaveResult reset = store.Save(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotFSSInfo"] = placement with { SizeOverride = null } }
        );

        Assert.Equal(1, reset.UpdatedSizeOverrideCount);
        Assert.NotNull(reset.SizeOverridesBackupPath);
        Assert.Null(store.Load().GetSizeOverride("PlotFSSInfo"));
    }

    [Theory]
    [InlineData("{\"PlotFSSInfo\":12}", "must be an object")]
    [InlineData(
        "{\"PlotFSSInfo\":{\"header\":0,\"title\":12,\"value\":0,\"body\":0,\"detail\":0,\"caption\":0}}",
        "5% increments"
    )]
    [InlineData("{\"PlotFSSInfo\":{\"header\":0,\"title\":0,\"value\":0,\"body\":0,\"detail\":0}}", "caption")]
    public void InvalidTypographyOverridesReportALayoutError(string json, string expectedError)
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(Path.Combine(temporaryDirectory, "plotters.json"), "{\"PlotFSSInfo\":\"left:8, top:8\"}");
        File.WriteAllText(Path.Combine(temporaryDirectory, "overlay-typography-overrides.json"), json);

        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(temporaryDirectory).Load();

        Assert.Contains(expectedError, layout.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OverlayTypographyScale.Default, layout.GetTypographyScale("PlotFSSInfo"));
    }

    [Fact]
    public void ReplacingPositionsPreservesIndependentGlobalScale()
    {
        var active = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        active.SetScaleIndex(19);
        var updated = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, null),
            },
            0.5,
            null
        );

        active.ReplaceWith(updated);

        Assert.Equal(19, active.ScaleIndex);
        Assert.Single(active.Placements);
        Assert.Equal(0.5, active.DefaultOpacity);
    }

    [Fact]
    public void UpdatingOneRuntimePlacementPreservesOtherLayoutState()
    {
        var original = new LegacyOverlayPlacement(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, 0.75);
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = original },
            0.5,
            null
        );
        active.SetScaleIndex(20);
        LegacyOverlayPlacement updated = original with { HorizontalOffset = 42 };

        Assert.True(active.SetPlacement("PlotJumpInfo", updated));
        Assert.False(active.SetPlacement("PlotJumpInfo", updated));

        Assert.Equal(updated, active.Placements["PlotJumpInfo"]);
        Assert.Equal(0.5, active.DefaultOpacity);
        Assert.Equal(20, active.ScaleIndex);
    }

    [Fact]
    public void CustomOpacityVerificationAcceptsTheSerializedSliderPrecision()
    {
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        double opacity = Math.BitIncrement(0.43d);

        store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBioSystem"] = new(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144, opacity),
            }
        );

        Assert.Equal(0.43d, store.Load().Placements["PlotBioSystem"].Opacity);
    }

    [Fact]
    public void EveryRuntimeLayoutMutationPublishesASettingsChange()
    {
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        int changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.SetScaleIndex(18);
        layout.SetPlacement(
            "PlotBioSystem",
            new LegacyOverlayPlacement(LegacyHorizontalAnchor.Left, 8, LegacyVerticalAnchor.Bottom, 144, 0.45, 17)
        );
        layout.ReplaceWith(new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), 0.7, null));

        Assert.Equal(3, changes);
    }

    [Fact]
    public void UnchangedRuntimePlacementDoesNotPublishAnotherSettingsChange()
    {
        var placement = new LegacyOverlayPlacement(
            LegacyHorizontalAnchor.Left,
            8,
            LegacyVerticalAnchor.Bottom,
            144,
            0.45,
            17
        );
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotBioSystem"] = placement },
            null,
            null
        );
        int changes = 0;
        layout.Changed += (_, _) => changes++;

        Assert.False(layout.SetPlacement("PlotBioSystem", placement));

        Assert.Equal(0, changes);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
