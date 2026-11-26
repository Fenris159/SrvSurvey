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
    [InlineData(VrPlatformProfileCatalog.MetaVdxrProfileId)]
    [InlineData(VrPlatformProfileCatalog.WivrnOpenXrProfileId)]
    [InlineData(VrPlatformProfileCatalog.MonadoOpenXrProfileId)]
    public void OpenXrRoutesMigratePreRc7727BundledDefaultsToSeatedWhilePreservingCustomEdits(string profile)
    {
        string data = Path.Combine(temporaryDirectory, "upgrade-" + profile);
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(data, "plotters.json"),
            """
            {
              "PlotAdjustVR": "center:0, middle:0 { s: 10.090002, p: <-0.42953613, 14.988647, 33.43891>, r: <-0.13900007, 0, -0.98910034>}",
              "PlotBioStatus": "center:0, top:8 { s: 10, p: <-0.37305868, 19.789331, 18.2856>, r: <-13.458961, 0, 0>}",
              "PlotBodyInfo": "left:8, top:8 { s: 7.5, p: <-4, 12, 10>, r: <0, -25, 0>}",
              "PlotJumpInfo": "center:0, top:8 { s: 20, p: <0.06868008, 23.358482, 19.957216>, r: <-20.24308, 0, 0>}",
              "PlotSysStatus": "left:8, bottom:44 { s: 3.1119983, p: <-9.165022, 4.2676287, 17.66371>, r: <5.0753016, -10.793489, 0>}"
            }
            """
        );

        var store = new VrOverlayCalibrationStore(data);
        VrOverlayCalibrationCatalog catalog = store.Load(profile);

        Assert.Equal(VrSeatedCalibrationDefaults.Get("PlotAdjustVR"), catalog.Defaults["PlotAdjustVR"]);
        Assert.Equal(VrSeatedCalibrationDefaults.Get("PlotBioStatus"), catalog.Defaults["PlotBioStatus"]);
        Assert.Equal(VrSeatedCalibrationDefaults.Get("PlotJumpInfo"), catalog.Defaults["PlotJumpInfo"]);
        Assert.Equal(VrSeatedCalibrationDefaults.Get("PlotSysStatus"), catalog.Defaults["PlotSysStatus"]);
        Assert.Equal(
            new VrOverlayCalibration(7.5f, new(-4f, 12f, 10f), new(0f, -25f, 0f)),
            catalog.Defaults["PlotBodyInfo"]
        );
    }

    [Fact]
    public void UpgradedSteamVrProfileMergesNewlyBundledOverlaysAndAllowsSavingThem()
    {
        string data = Path.Combine(temporaryDirectory, "upgrade-steamvr");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(data, "plotters.json"),
            """
            {
              "PlotJumpInfo": "center:0, top:8 { s: 14, p: <0, 15, 12>, r: <-18, 0, 0>}"
            }
            """
        );

        var store = new VrOverlayCalibrationStore(data);
        VrOverlayCalibrationCatalog catalog = store.Load();

        // Existing saved SteamVR placement stays intact while newly bundled overlays merge in.
        Assert.Equal(14f, catalog.Defaults["PlotJumpInfo"].Scale);
        Assert.True(catalog.Defaults.ContainsKey("PlotMiningMissions"));
        Assert.Equal(catalog.FactoryDefaults["PlotMiningMissions"], catalog.Defaults["PlotMiningMissions"]);

        // Saving a newly bundled overlay adds it cleanly to the user's plotters.json with its bundled desktop prefix.
        var edited = new VrOverlayCalibration(5.2f, new(5f, 14f, 9f), new(-12f, 24f, 0f));
        VrOverlayCalibrationSaveResult saved = store.Save("PlotMiningMissions", edited);
        Assert.NotNull(saved.BackupPath);
        string updatedText = File.ReadAllText(Path.Combine(data, "plotters.json"));
        Assert.Contains("\"PlotMiningMissions\": \"right:8, top:8 ", updatedText);
        Assert.Equal(edited, store.Load().Defaults["PlotMiningMissions"]);
        Assert.Equal(14f, store.Load().Defaults["PlotJumpInfo"].Scale);
    }

    [Fact]
    public void SavedDesktopOnlyEntryFallsBackToBundledSteamVrDefaultAndPreservesDesktopPlacementOnSave()
    {
        string data = Path.Combine(temporaryDirectory, "upgrade-steamvr-desktop-only");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(data, "plotters.json"),
            """
            {
              "PlotColonizationWishList": "left:24, bottom:12",
              "PlotJumpInfo": "center:0, top:8 { s: 14, p: <0, 15, 12>, r: <-18, 0, 0>}"
            }
            """
        );

        var store = new VrOverlayCalibrationStore(data);
        VrOverlayCalibrationCatalog catalog = store.Load();

        // A saved desktop-only entry without a VR block inherits the newly bundled SteamVR default while explicit VR suffixes stay intact.
        Assert.True(catalog.Defaults.ContainsKey("PlotColonizationWishList"));
        Assert.Equal(catalog.FactoryDefaults["PlotColonizationWishList"], catalog.Defaults["PlotColonizationWishList"]);
        Assert.Equal(14f, catalog.Defaults["PlotJumpInfo"].Scale);

        var edited = new VrOverlayCalibration(4.5f, new(-5f, 11f, 9f), new(0f, -28f, 0f));
        _ = store.Save("PlotColonizationWishList", edited);
        string updatedText = File.ReadAllText(Path.Combine(data, "plotters.json"));
        Assert.Contains("\"PlotColonizationWishList\": \"left:24, bottom:12 ", updatedText);
        Assert.Equal(edited, store.Load().Defaults["PlotColonizationWishList"]);
        Assert.Equal(14f, store.Load().Defaults["PlotJumpInfo"].Scale);
    }

    [Theory]
    [InlineData("PlotJumpInfo", "{ s: 8, p: <0, 4.5, 11>, r: <22.2, 0, 0>}")]
    [InlineData("PlotBioStatus", "{ s: 3.5, p: <0, 4.5, 11>, r: <22.2, 0, 0>}")]
    [InlineData("PlotSysStatus", "{ s: 1.9, p: <-6, -3, 9.5>, r: <-14.9, 32.3, 0>}")]
    [InlineData("PlotBioSystem", "{ s: 3.2, p: <-6, -0.5, 9.5>, r: <-2.5, 32.3, 0>}")]
    [InlineData("PlotBuildCommodities", "{ s: 3.6, p: <6, 4.5, 9.5>, r: <21.8, -32.3, 0>}")]
    [InlineData("PlotRouteBio", "{ s: 3.5, p: <6, -0.5, 9.5>, r: <-2.5, -32.3, 0>}")]
    [InlineData("PlotSphericalSearch", "{ s: 3.2, p: <6, 4.5, 9.5>, r: <21.8, -32.3, 0>}")]
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

            VrOverlayCalibration steam = steamVr.FactoryDefaults[definition.Name];
            Assert.Equal(seated.Position.X, steam.Position.X);
            Assert.Equal(seated.Position.Y + 12f, steam.Position.Y);
            Assert.Equal(seated.Position.Z, steam.Position.Z);
        }

        foreach (OverlayLayoutCategoryDefinition category in OverlayLayoutCatalog.Categories)
        {
            IReadOnlyList<OverlayLayoutDefinition> categoryOverlays = OverlayLayoutCatalog.ForCategory(
                category.Category
            );
            Assert.Equal(
                categoryOverlays.Count,
                categoryOverlays.Select(item => steamVr.FactoryDefaults[item.Name].Position).Distinct().Count()
            );
            Assert.Equal(
                categoryOverlays.Count,
                categoryOverlays.Select(item => openXr.FactoryDefaults[item.Name].Position).Distinct().Count()
            );
        }

        string[] mapOverlays =
        [
            "PlotBodyInfo",
            "PlotFSSInfo",
            "PlotGalMap",
            "PlotColonizationWishList",
            "PlotStationInfo",
        ];
        Assert.Equal(
            mapOverlays.Length,
            mapOverlays.Select(name => steamVr.FactoryDefaults[name].Position).Distinct().Count()
        );
        Assert.Equal(
            mapOverlays.Length,
            mapOverlays.Select(name => openXr.FactoryDefaults[name].Position).Distinct().Count()
        );
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
