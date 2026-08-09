using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SystemSurveyViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemSurveyViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EmptyBiologyStateProvidesAStableNonNullBindingTarget()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasBiologySurvey);
        Assert.Same(
            BiologySurveyViewModel.Empty,
            viewModel.BiologySurveyDisplay);
    }

    [Fact]
    public void UseBioSignalRadiusInvertsSmallCanonnRadarCircles()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.UseSmallCanonnRadarCircles);
        Assert.False(viewModel.UseBioSignalRadius);

        viewModel.UseBioSignalRadius = true;

        Assert.True(viewModel.UseBioSignalRadius);
        Assert.False(viewModel.UseSmallCanonnRadarCircles);

        viewModel.UseSmallCanonnRadarCircles = true;

        Assert.True(viewModel.UseSmallCanonnRadarCircles);
        Assert.False(viewModel.UseBioSignalRadius);

        // Setting the inverted option to false is a no-op until the small-circle
        // preference is enabled again through its own setter.
        viewModel.UseBioSignalRadius = false;
        Assert.False(viewModel.UseBioSignalRadius);
        Assert.True(viewModel.UseSmallCanonnRadarCircles);
    }

    [Fact]
    public void IdenticalEmptyUpdateRetainsPresentationAndDoesNotNotify()
    {
        var viewModel = CreateViewModel();
        var exobiology = ExobiologySnapshot.Empty with
        {
            ScannedBioEntryIds = ["bio-entry"],
        };
        viewModel.ApplyUpdate(
        [
            Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
        ],
        new EliteStatus(),
        exobiology);
        var fssBodies = viewModel.FssBodies;
        var dssBodies = viewModel.DssBodies;
        var biologicalBodies = viewModel.BiologicalBodies;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        viewModel.ApplyUpdate(
            [],
            null,
            exobiology with { ScannedBioEntryIds = ["bio-entry"] });

        Assert.Same(fssBodies, viewModel.FssBodies);
        Assert.Same(dssBodies, viewModel.DssBodies);
        Assert.Same(biologicalBodies, viewModel.BiologicalBodies);
        Assert.Empty(notifications);
    }

    [Fact]
    public void ApplyUpdateRaisesStatusAfterSnapshotIsConsistent()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            ],
            new EliteStatus
            {
                BodyName = "Test 1",
                Flags = StatusFlags.HasLatLong,
            },
            ExobiologySnapshot.Empty);

        long? statusSeenAddress = null;
        long? exoSeenAddress = null;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName
                == nameof(SystemSurveyViewModel.CurrentStatus))
            {
                statusSeenAddress = viewModel.Snapshot.SystemAddress;
            }

            if (eventArgs.PropertyName
                == nameof(SystemSurveyViewModel.CurrentExobiology))
            {
                exoSeenAddress = viewModel.Snapshot.SystemAddress;
            }
        };

        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"FSDJump","StarSystem":"Next","SystemAddress":99}"""),
            ],
            new EliteStatus
            {
                BodyName = "Next 1",
                Flags = StatusFlags.HasLatLong,
            },
            ExobiologySnapshot.Empty with
            {
                ScannedBioEntryIds = ["entry-1"],
            });

        Assert.Equal(99, statusSeenAddress);
        Assert.Equal(99, exoSeenAddress);
        Assert.Equal(99, viewModel.Snapshot.SystemAddress);
        Assert.Equal("Next 1", viewModel.CurrentStatus?.BodyName);
        Assert.Equal(["entry-1"], viewModel.CurrentExobiology.ScannedBioEntryIds);
    }

    [Fact]
    public void PriorScanEligibilityUsesLegacySurfaceModesAndPreferences()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                BodyName = "Test 1",
                PlanetRadius = 1_000_000,
            });

        Assert.True(viewModel.ShouldLoadPriorScans);

        viewModel.SetRepeatVisitBiologySuppression(true);
        Assert.True(viewModel.AreBiologyOverlaysSuppressedForRepeatVisit);
        Assert.False(viewModel.ShouldLoadPriorScans);
        viewModel.AutoHideBioPlotOnRepeat = false;
        Assert.False(viewModel.AreBiologyOverlaysSuppressedForRepeatVisit);
        Assert.True(viewModel.ShouldLoadPriorScans);

        viewModel.UseExternalData = false;
        Assert.False(viewModel.ShouldLoadPriorScans);
        viewModel.UseExternalData = true;
        viewModel.AutoShowPriorScans = false;
        Assert.False(viewModel.ShouldLoadPriorScans);
        viewModel.AutoShowPriorScans = true;

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.Docked,
            BodyName = "Test 1",
            PlanetRadius = 1_000_000,
        });
        Assert.False(viewModel.ShouldLoadPriorScans);
    }

    [Fact]
    public void FssOverlayUsesLegacyModesAndForcedToggleSemantics()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        Assert.True(viewModel.ShouldShowFssInfo);
        Assert.True(viewModel.ToggleFssInfoVisibility());
        Assert.False(viewModel.ShouldShowFssInfo);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        Assert.False(viewModel.ShouldShowFssInfo);
        Assert.True(viewModel.ToggleFssInfoVisibility());
        Assert.True(viewModel.ShouldShowFssInfo);
        Assert.True(viewModel.IsFssInfoForced);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"StartJump","JumpType":"Hyperspace"}""")],
            null);
        Assert.False(viewModel.ShouldShowFssInfo);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Died"}""")],
            null);
        Assert.True(viewModel.ShouldShowFssInfo);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Music","MusicTrack":"GalaxyMap"}""")],
            new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.True(viewModel.ShouldShowFssInfo);
    }

    [Fact]
    public void SystemStatusRequiresHonkAndSupportedFlightMode()
    {
        var viewModel = CreateViewModel();
        var supercruise = new EliteStatus
        {
            Flags = StatusFlags.Supercruise | StatusFlags.InMainShip,
        };
        viewModel.ApplyUpdate(
            [Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}""")],
            supercruise);
        Assert.False(viewModel.ShouldShowSystemStatus);

        viewModel.UpdateCanonnSystemPoi(new CanonnSystemPoiResult(
            "Test",
            []));
        Assert.True(viewModel.ShouldShowSystemStatus);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":3}""")],
            null);
        Assert.True(viewModel.ShouldShowSystemStatus);
        Assert.Equal("FSS 0% complete", viewModel.SystemStatusText);

        viewModel.ApplyUpdate([], new EliteStatus { Flags2 = StatusFlags2.InTaxi });
        Assert.False(viewModel.ShouldShowSystemStatus);
    }

    [Fact]
    public void FlightWarningMatchesLegacyGravityBodyAndModeRules()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong,
                BodyName = "Test 1",
            });

        Assert.True(viewModel.ShouldShowFlightWarning);
        Assert.Equal("WARNING: SURFACE GRAVITY 1.20 g", viewModel.FlightWarningText);

        viewModel.AutoShowFlightWarnings = false;
        Assert.False(viewModel.ShouldShowFlightWarning);
        viewModel.AutoShowFlightWarnings = true;
        viewModel.HighGravityWarningLevel = 2;
        Assert.False(viewModel.ShouldShowFlightWarning);

        viewModel.HighGravityWarningLevel = 1;
        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
            BodyName = "Test 1",
        });
        Assert.False(viewModel.ShouldShowFlightWarning);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
            BodyName = "Test 1",
        });
        Assert.False(viewModel.ShouldShowFlightWarning);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.InSrv,
            BodyName = "Test 1",
            GuiFocus = GuiFocus.RolePanel,
        });
        Assert.False(viewModel.ShouldShowFlightWarning);
    }

    [Fact]
    public void DisplayFiltersBodiesAndBuildsDssAndSignalProgress()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":3,"NonBodyCount":1}"""),
                Parse(TerraformableScan),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.01,"DistanceFromArrivalLS":50,"WasDiscovered":true,"WasMapped":true}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2},{"Type":"$SAA_SignalType_Geological;","Count":1}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":2,"Genus":"$Genus_A;"}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 3","BodyID":3,"PlanetClass":"Sudarsky class II gas giant","MassEM":10,"WasDiscovered":true,"WasMapped":true}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.Fss,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.Equal(3, viewModel.FssBodies.Count);
        var terraformable = Assert.Single(
            viewModel.FssBodies,
            body => body.Name.Contains('1'));
        Assert.Contains("TERRAFORMABLE", terraformable.Markers);
        Assert.True(terraformable.IsDssCandidate);

        var signalBody = Assert.Single(
            viewModel.FssBodies,
            body => body.Name.Contains('2'));
        Assert.Equal(2, signalBody.BiologicalSignalCount);
        Assert.Equal(1, signalBody.AnalyzedBiologicalSignalCount);
        Assert.Equal(1, signalBody.GeologicalSignalCount);

        Assert.True(viewModel.ShouldShowLastFssBody);
        Assert.Equal("Test 3", viewModel.LastFssBodyName);
        Assert.Equal("Sudarsky class II gas giant", viewModel.LastFssBodyClass);
        Assert.Equal("0 LS", viewModel.LastFssBodyDistance);
        Assert.Equal("18.3 K CR", viewModel.LastFssScanValue);
        Assert.Equal("99.2 K CR", viewModel.LastFssMappedValue);
        Assert.False(viewModel.HasLastFssMarkers);
        Assert.False(viewModel.HasLastFssSignals);

        var dssBody = Assert.Single(viewModel.DssBodies);
        Assert.Equal("1", dssBody.Name);
        Assert.True(dssBody.IsDestination);
        Assert.Equal("1 biological signal remaining", viewModel.BiologicalHeading);
        Assert.Equal("2", Assert.Single(viewModel.BiologicalBodies).Name);
    }

    [Fact]
    public void FssBodiesPutUnmappedBodiesFirstAndSortEachGroupNaturally()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test B 10","BodyID":10,"PlanetClass":"Water world","MassEM":1,"WasDiscovered":true,"WasMapped":false}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test A 10","BodyID":11,"PlanetClass":"Water world","MassEM":1,"WasDiscovered":true,"WasMapped":false}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test A 2","BodyID":2,"PlanetClass":"Water world","MassEM":1,"WasDiscovered":true,"WasMapped":false}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test B 2","BodyID":3,"PlanetClass":"Water world","MassEM":1,"WasDiscovered":true,"WasMapped":false}"""),
                Parse("""{"event":"SAAScanComplete","SystemAddress":42,"BodyName":"Test A 10","BodyID":11}"""),
                Parse("""{"event":"SAAScanComplete","SystemAddress":42,"BodyName":"Test B 2","BodyID":3}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        Assert.Equal(
            ["A2", "B10", "A10", "B2"],
            viewModel.FssBodies.Select(body => body.Name));
        Assert.Equal(
            [false, false, true, true],
            viewModel.FssBodies.Select(body => body.IsSurfaceScanned));
    }

    [Fact]
    public void LastFssBodyVisibilityHonorsModeAndPreference()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(TerraformableScan),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        Assert.True(viewModel.HasLastFssBody);
        Assert.Equal("⚑ Test 1", viewModel.LastFssBodyName);
        Assert.Equal("TERRAFORMABLE · LANDABLE", viewModel.LastFssMarkers);
        Assert.True(viewModel.ShouldShowLastFssBody);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        Assert.False(viewModel.ShouldShowLastFssBody);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.Fss });
        viewModel.AutoShowLastFssBody = false;
        Assert.False(viewModel.ShouldShowLastFssBody);
    }

    [Fact]
    public void LastFssBodyPreservesPerSignalBiologyRewardBars()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        Assert.True(viewModel.HasLastFssSignals);
        Assert.True(viewModel.HasLastFssBiologyRewards);
        Assert.Equal(2, viewModel.LastFssBiologyRewardBands.Count);
        Assert.Equal(
            7_252_500,
            viewModel.LastFssBiologyRewardBands[0].MinimumReward);
        Assert.Equal(0, viewModel.LastFssBiologyRewardBands[1].MinimumReward);
        Assert.Contains("7.25 M CR", viewModel.LastFssBiologyRewardText);
    }

    [Fact]
    public void SettingsImmediatelyRecalculateFilteredRows()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","MassEM":0.01,"WasDiscovered":true,"WasMapped":true}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });
        Assert.Empty(viewModel.FssBodies);

        viewModel.FssBodyValueFloor = 0;

        Assert.Single(viewModel.FssBodies);
    }

    [Fact]
    public void BodyInformationUsesMapDestinationAndFormatsDetailedScan()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[500,0,0]}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2},{"Type":"$SAA_SignalType_Geological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.True(viewModel.ShouldShowBodyInfo);
        var body = Assert.IsType<BodyInformationViewModel>(
            viewModel.BodyInformation);
        Assert.Equal("⚑ Test 1", body.Name);
        Assert.Equal("High metal content body", body.BodyClass);
        Assert.Equal("123 LS", body.Distance);
        Assert.Equal("TERRAFORMABLE · UNDISCOVERED", body.Markers);
        Assert.EndsWith(" CR", body.ScanValue);
        Assert.EndsWith(" CR", body.MappedValue);
        Assert.Equal("300 K", body.Temperature);
        Assert.Equal("1.200 g", body.Gravity);
        Assert.True(body.IsHighGravity);
        Assert.True(body.IsHighValue);
        Assert.Equal("0.0100 bar", body.Pressure);
        Assert.Equal("2 biological signals", body.BiologicalSignals);
        Assert.Equal("1 geological signal", body.GeologicalSignals);
        Assert.Equal("Minor silicate vapour geysers", body.Volcanism);
        Assert.Equal("Thin carbon dioxide", body.Atmosphere);
        Assert.Equal(2, body.AtmosphereComposition.Count);
        Assert.Equal("Carbon Dioxide", body.AtmosphereComposition[0].Name);
        Assert.Equal(2, body.Materials.Count);
        Assert.True(Assert.Single(
            body.Materials,
            material => material.Name == "Yttrium").IsRare);
        var ring = Assert.Single(body.Rings);
        Assert.Equal("A", ring.Name);
        Assert.Equal("Rocky", ring.RingClass);
    }

    [Fact]
    public void BodyInformationPreservesLegacyVisibilityAndToggleModes()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[500,0,0]}"""),
                Parse(BodyInformationScan),
            ],
            new EliteStatus
            {
                BodyName = "Test 1",
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.False(viewModel.ShouldShowBodyInfo);
        Assert.True(viewModel.ToggleBodyInfoVisibility());
        Assert.True(viewModel.IsBodyInfoForced);
        Assert.True(viewModel.ShouldShowBodyInfo);
        Assert.True(viewModel.ToggleBodyInfoVisibility());
        Assert.False(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            GuiFocus = GuiFocus.SystemMap,
            Destination = new StatusDestination
            {
                System = 42,
                Body = 1,
                Name = "Test 1",
            },
        });
        viewModel.ShowFssInfoInSystemMap = true;
        Assert.False(viewModel.ShouldShowBodyInfo);
        viewModel.ShowFssInfoInSystemMap = false;
        Assert.True(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            BodyName = "Test 1",
            Flags = StatusFlags.InMainShip
                | StatusFlags.Supercruise
                | StatusFlags.HasLatLong,
        });
        Assert.True(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            BodyName = "Test 1",
            Flags = StatusFlags.InMainShip
                | StatusFlags.HasLatLong
                | StatusFlags.HudInAnalysisMode,
        });
        Assert.False(viewModel.ShouldShowBodyInfo);
        viewModel.ShowBodyInfoAtSurface = true;
        Assert.True(viewModel.ShouldShowBodyInfo);
    }

    [Fact]
    public void BodyInformationRequiresLegacyExactTargetMatch()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [Parse("""{"event":"Location","StarSystem":"Sol vicinity","SystemAddress":42,"StarPos":[100,0,0]}""")],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 9,
                    Name = "Sol vicinity 9",
                },
            });

        Assert.Null(viewModel.BodyInformation);
        Assert.True(viewModel.IsWithinBodyInfoBubble);
        Assert.False(viewModel.ShouldShowBodyInfo);

        viewModel.HideBodyInfoInBubble = false;
        Assert.False(viewModel.ShouldShowBodyInfo);
    }

    [Fact]
    public void BodyInformationUsesNearbyBodyOutsideMapAndRejectsExternalTarget()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[500,0,0]}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.01,"Landable":true}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip
                    | StatusFlags.Supercruise
                    | StatusFlags.HasLatLong,
                BodyName = "Test 1",
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 2,
                    Name = "Test 2",
                },
            });

        Assert.EndsWith("Test 1", viewModel.BodyInformation?.Name);
        Assert.True(viewModel.ShouldShowBodyInfo);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.InMainShip
                | StatusFlags.Supercruise
                | StatusFlags.HasLatLong,
            BodyName = "Test 1",
            Destination = new StatusDestination
            {
                System = 84,
                Body = 2,
                Name = "Elsewhere 2",
            },
        });

        Assert.Null(viewModel.BodyInformation);
        Assert.False(viewModel.ShouldShowBodyInfo);
    }

    [Fact]
    public void BiologySurveyUsesSystemOverviewInMapModes()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Analyse","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            },
            new ExobiologySnapshot(null, null, null, 0, [], 4));

        Assert.True(viewModel.ShouldShowBioSystem);
        viewModel.SetRepeatVisitBiologySuppression(true);
        Assert.False(viewModel.ShouldShowBioSystem);
        viewModel.SetRepeatVisitBiologySuppression(false);
        var biology = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.True(biology.IsSystemOverview);
        Assert.True(biology.HasRadicoidaUnicaCount);
        Assert.Equal("Radicoida scans: 4", biology.RadicoidaUnicaCountText);
        Assert.Equal("1 of 2 biological signals analyzed", biology.ProgressText);
        var body = Assert.Single(biology.Bodies);
        Assert.True(body.IsDestination);
        Assert.Equal("High metal content body", body.BodySubtype);
        Assert.EndsWith(
            "/Assets/Bodies/high-metal-content.png",
            body.BodyIconAssetPath,
            StringComparison.Ordinal);
        Assert.Equal(7_252_500, body.KnownReward);
        Assert.True(body.HasUnknownReward);
        Assert.Equal(body.SignalCount, body.RewardBands.Count);
        Assert.Equal(2, body.RewardBands.Count);
        Assert.Equal(7_252_500, body.RewardBands[0].MinimumReward);
        Assert.False(body.RewardBands[0].IsPrediction);
        Assert.True(body.RewardBands[0].ShouldDim);
        Assert.Equal(0, body.RewardBands[1].MinimumReward);
        Assert.False(body.RewardBands[1].IsPrediction);
        Assert.Equal("Known reward: 7.25 M CR", biology.RewardSummary);
    }

    [Fact]
    public void BiologySurveyTemporarilyShowsNewMapSelectionPerSignal()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var viewModel = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(
                temporaryDirectory,
                "timed-ui-settings.json")),
            utcNow: () => now);
        var firstStatus = new EliteStatus
        {
            GuiFocus = GuiFocus.SystemMap,
            Destination = new StatusDestination
            {
                System = 42,
                Body = 1,
                Name = "Test 1",
            },
        };
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}]}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.1,"Landable":true}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":3}]}"""),
            ],
            firstStatus);

        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
        Assert.False(viewModel.HasTimedBiologySelection);

        viewModel.ApplyUpdate([], firstStatus with
        {
            Destination = new StatusDestination
            {
                System = 42,
                Body = 2,
                Name = "Test 2",
            },
        });

        Assert.True(viewModel.HasTimedBiologySelection);
        Assert.True(viewModel.BiologySurvey!.IsBodyDetail);
        Assert.Equal("Test 2 biology", viewModel.BiologySurvey.Heading);
        Assert.Equal(100, viewModel.TimedBiologySelectionProgressPercent);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Scan","ScanType":"AutoScan","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body"}""")],
            null);

        Assert.True(viewModel.HasTimedBiologySelection);
        Assert.True(viewModel.BiologySurvey!.IsBodyDetail);

        now = now.AddSeconds(3);
        Assert.False(viewModel.RefreshTransientState());
        Assert.Equal(50, viewModel.TimedBiologySelectionProgressPercent);

        now = now.AddSeconds(3);
        Assert.True(viewModel.RefreshTransientState());
        Assert.False(viewModel.HasTimedBiologySelection);
        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
    }

    [Fact]
    public void BiologySurveyCancelsTimedSelectionWhenMapCloses()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var viewModel = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(
                temporaryDirectory,
                "cancel-timed-ui-settings.json")),
            utcNow: () => now);
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 0,
                    Name = "Test",
                },
            });
        viewModel.ApplyUpdate(
            [],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });
        Assert.True(viewModel.HasTimedBiologySelection);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.Supercruise,
            GuiFocus = GuiFocus.NoFocus,
        });

        Assert.False(viewModel.HasTimedBiologySelection);
        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
    }

    [Fact]
    public void BiologyBodyDetailRequiresNearBodyStatusOrPostDssGrace()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var viewModel = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(
                temporaryDirectory,
                "body-transition-ui-settings.json")),
            utcNow: () => now);
        var nearBodyStatus = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong,
            BodyName = "Test 1",
        };
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            nearBodyStatus);

        Assert.True(viewModel.BiologySurvey!.IsBodyDetail);
        Assert.NotNull(viewModel.BiologyStatus);

        viewModel.ApplyUpdate([], nearBodyStatus with
        {
            Flags = StatusFlags.InMainShip,
        });

        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
        Assert.Null(viewModel.BiologyStatus);
        Assert.False(viewModel.ShouldShowBioSystem);
    }

    [Fact]
    public void BiologyBodyDetailSurvivesDepartureOnlyForPostDssGraceWindow()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var viewModel = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(
                temporaryDirectory,
                "body-dss-transition-ui-settings.json")),
            utcNow: () => now);
        var nearBodyStatus = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong,
            BodyName = "Test 1",
        };
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
                Parse("""{"timestamp":"2026-07-25T12:00:00Z","event":"SAAScanComplete","SystemAddress":42,"BodyName":"Test 1","BodyID":1}"""),
            ],
            nearBodyStatus);

        viewModel.ApplyUpdate([], nearBodyStatus with
        {
            Flags = StatusFlags.InMainShip,
        });

        Assert.True(viewModel.IsWithinPostDssBiologyWindow);
        Assert.True(viewModel.BiologySurvey!.IsBodyDetail);
        Assert.NotNull(viewModel.BiologyStatus);

        now = now.AddSeconds(121);

        Assert.True(viewModel.RefreshTransientState());
        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
        Assert.Null(viewModel.BiologyStatus);
    }

    [Fact]
    public void BiologySurveyShowsBodySamplesRewardsFootfallAndGeology()
    {
        var viewModel = CreateViewModel();
        var scan = new BioSampleSnapshot(
            new SurfaceLocation(1, 2),
            150,
            "$Codex_Ent_Aleoids_Genus_Name;",
            "$Codex_Ent_Aleoids_01_Name;",
            "Active",
            2310101,
            "Test 1");
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"Population":0}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"SAASignalsFound","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1},{"Type":"$SAA_SignalType_Geological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":100,"Name_Localised":"Silicate Vapour Fumarole","SubCategory":"$Codex_SubCategory_Geology_and_Anomalies;"}"""),
                Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2,"IsNewEntry":true}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
                Parse("""{"event":"Disembark","SystemAddress":42,"Body":"Test 1","BodyID":1,"OnPlanet":true,"OnStation":false}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss },
            new ExobiologySnapshot(null, scan, null, 0, [], 0));

        Assert.True(viewModel.ShouldShowBioSystem);
        var biology = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.True(biology.IsBodyDetail);
        Assert.Equal("Test 1 biology", biology.Heading);
        var organism = Assert.Single(biology.Organisms);
        Assert.Equal("Aleoida Arcus - Green", organism.DisplayName);
        Assert.Equal("Arcus", organism.SpeciesName);
        Assert.Equal("Green", organism.VariantName);
        Assert.Equal("7.25 M CR", organism.RewardText);
        Assert.False(organism.IsGlobalRegionalFirst);
        Assert.True(organism.IsRegionalFirst);
        Assert.False(organism.IsHighlightedFirst);
        Assert.True(organism.IsCurrentSample);
        Assert.False(organism.ShouldDim);
        var organismGroup = Assert.Single(biology.OrganismGroups);
        Assert.Equal("Aleoida:", organismGroup.GenusLabel);
        Assert.False(organismGroup.IsGlobalRegionalFirst);
        Assert.True(organismGroup.IsRegionalFirst);
        Assert.Equal("Arcus", Assert.Single(organismGroup.Species).SpeciesName);
        Assert.Equal(
            "First-footfall value: 36.26 M CR",
            biology.FirstFootfallRewardSummary);
        Assert.Equal(2, biology.GeologicalSignalCount);
        Assert.Equal("Silicate Vapour Fumarole", Assert.Single(
            biology.GeologicalSignals));

        viewModel.HideGeoCountInBioSystem = true;
        Assert.False(viewModel.BiologySurvey!.HasGeologicalSignals);
        viewModel.HighlightRegionalFirsts = true;
        Assert.True(Assert.Single(
            viewModel.BiologySurvey!.Organisms).IsHighlightedFirst);
    }

    [Fact]
    public void BiologySurveyMarksOnlyTheExactSameGenusSpeciesAsCurrent()
    {
        var viewModel = CreateViewModel();
        var activeScan = new BioSampleSnapshot(
            new SurfaceLocation(1, 2),
            150,
            "$Codex_Ent_Aleoids_Genus_Name;",
            "$Codex_Ent_Aleoids_02_Name;",
            "Aleoida Coronamus - Lime",
            2310206,
            "Test 1");
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_02_Name;","Species_Localised":"Aleoida Coronamus","Variant":"$Codex_Ent_Aleoids_02_L_Name;","Variant_Localised":"Aleoida Coronamus - Lime"}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss },
            new ExobiologySnapshot(null, activeScan, null, 0, [], 0));

        var organisms = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey).Organisms;
        Assert.Equal(2, organisms.Count);
        Assert.False(Assert.Single(
            organisms,
            organism => organism.SpeciesName == "Arcus").IsCurrentSample);
        Assert.True(Assert.Single(
            organisms,
            organism => organism.SpeciesName == "Coronamus").IsCurrentSample);
    }

    [Fact]
    public void BiologySurveyClearsExternalFirstCandidateAfterJournalConfirmation()
    {
        var reference = ExobiologyReferenceCatalog.LoadEmbedded()
            .FindByDisplayName("Aleoida Coronamus - Lime");
        Assert.NotNull(reference);
        var globalRegionalCandidates = RegionalCodexCandidateCatalog.FromEntries(
        [
            new(
                18,
                "Inner Orion Spur",
                reference.EntryId,
                reference.DisplayName ?? "Aleoida Coronamus - Lime"),
        ]);
        var viewModel = CreateViewModel(globalRegionalCandidates);
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""),
                Parse(PredictableAleoidaScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });
        var previouslyDiscovered = new Dictionary<long, CommanderCodexFirst>
        {
            [reference.EntryId] = new(
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                99,
                7),
        };
        viewModel.UpdateCommanderCodexContext(
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                0,
                null,
                previouslyDiscovered),
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                18,
                "Inner Orion Spur",
                previouslyDiscovered),
            18);

        var prediction = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.True(prediction.IsPrediction);
        Assert.True(prediction.IsGlobalRegionalFirst);

        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310206,"Name_Localised":"Aleoida Coronamus - Lime","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2,"IsNewEntry":false}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_02_Name;","Species_Localised":"Aleoida Coronamus","Variant":"$Codex_Ent_Aleoids_02_L_Name;","Variant_Localised":"Aleoida Coronamus - Lime"}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        var confirmed = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.False(confirmed.IsPrediction);
        Assert.False(confirmed.IsGlobalRegionalFirst);
        Assert.False(confirmed.IsCommanderFirst);
        Assert.False(confirmed.IsRegionalFirst);
        Assert.False(confirmed.IsHighlightedFirst);
        Assert.False(Assert.Single(
            viewModel.BiologySurvey.OrganismGroups).IsGlobalRegionalFirst);
    }

    [Fact]
    public void BiologySurveyInfersCommanderAndRegionalFirstsFromLedgers()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;","Latitude":1,"Longitude":2}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        var unavailable = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.False(unavailable.IsGlobalRegionalFirst);
        Assert.False(unavailable.IsCommanderFirst);
        Assert.False(unavailable.IsRegionalFirst);

        var globalOtherLocation = new CommanderCodexData(
            "fid",
            "Cmdr Test",
            0,
            null,
            new Dictionary<long, CommanderCodexFirst>
            {
                [2310101] = new(
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    99,
                    7),
            });
        var emptyRegional = new CommanderCodexData(
            "fid",
            "Cmdr Test",
            18,
            "Inner Orion Spur",
            new Dictionary<long, CommanderCodexFirst>());
        viewModel.UpdateCommanderCodexContext(
            globalOtherLocation,
            emptyRegional);

        var regional = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.False(regional.IsGlobalRegionalFirst);
        Assert.False(regional.IsCommanderFirst);
        Assert.True(regional.IsRegionalFirst);
        Assert.False(regional.IsHighlightedFirst);

        viewModel.HighlightRegionalFirsts = true;
        Assert.True(Assert.Single(
            viewModel.BiologySurvey!.Organisms).IsHighlightedFirst);

        var globalCurrentLocation = globalOtherLocation with
        {
            Firsts = new Dictionary<long, CommanderCodexFirst>
            {
                [2310101] = new(
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    42,
                    1),
            },
        };
        viewModel.UpdateCommanderCodexContext(
            globalCurrentLocation,
            emptyRegional);

        var commander = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.False(commander.IsGlobalRegionalFirst);
        Assert.True(commander.IsCommanderFirst);
        Assert.False(commander.IsRegionalFirst);
        Assert.True(commander.IsHighlightedFirst);
    }

    [Fact]
    public void BiologySystemOverviewHighlightsKnownRegionalFirstCandidates()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
                Parse(BodyInformationScan.Replace(
                    "\"WasDiscovered\":false",
                    "\"WasDiscovered\":true",
                    StringComparison.Ordinal)),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
                Parse("""{"event":"ScanOrganic","ScanType":"Log","SystemAddress":42,"Body":1,"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida","Species":"$Codex_Ent_Aleoids_01_Name;","Species_Localised":"Aleoida Arcus","Variant":"$Codex_Ent_Aleoids_01_B_Name;","Variant_Localised":"Aleoida Arcus - Green"}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.SystemMap });
        Assert.True(Assert.Single(viewModel.Snapshot.Bodies).WasDiscovered);

        var globalOtherLocation = new CommanderCodexData(
            "fid",
            "Cmdr Test",
            0,
            null,
            new Dictionary<long, CommanderCodexFirst>
            {
                [2310101] = new(
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    99,
                    7),
            });
        var emptyRegional = new CommanderCodexData(
            "fid",
            "Cmdr Test",
            18,
            "Inner Orion Spur",
            new Dictionary<long, CommanderCodexFirst>());
        viewModel.UpdateCommanderCodexContext(
            globalOtherLocation,
            emptyRegional,
            18);

        var overview = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.True(overview.IsSystemOverview);
        Assert.False(Assert.Single(
            Assert.Single(overview.Bodies).RewardBands).IsHighlighted);

        viewModel.HighlightRegionalFirsts = true;

        overview = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.True(Assert.Single(
            Assert.Single(overview.Bodies).RewardBands).IsHighlighted);

        viewModel.UpdateCommanderCodexContext(
            globalOtherLocation,
            emptyRegional with
            {
                Firsts = new Dictionary<long, CommanderCodexFirst>
                {
                    [2310101] = new(
                        DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
                        88,
                        6),
                },
            },
            18);

        overview = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        Assert.False(Assert.Single(
            Assert.Single(overview.Bodies).RewardBands).IsHighlighted);
    }

    [Fact]
    public void BiologySystemOverviewHighlightsPredictedRegionalFirstCandidates()
    {
        var reference = ExobiologyReferenceCatalog.LoadEmbedded()
            .FindByDisplayName("Aleoida Coronamus - Lime");
        Assert.NotNull(reference);
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""),
                Parse(PredictableAleoidaScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.SystemMap });
        viewModel.UpdateCommanderCodexContext(
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                0,
                null,
                new Dictionary<long, CommanderCodexFirst>
                {
                    [reference.EntryId] = new(
                        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                        99,
                        7),
                }),
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                18,
                "Inner Orion Spur",
                new Dictionary<long, CommanderCodexFirst>()),
            18);

        var overview = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        var band = Assert.Single(Assert.Single(overview.Bodies).RewardBands);
        Assert.True(band.IsPrediction);
        Assert.False(band.IsHighlighted);

        viewModel.HighlightRegionalFirsts = true;

        overview = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        band = Assert.Single(Assert.Single(overview.Bodies).RewardBands);
        Assert.True(band.IsHighlighted);
    }

    [Fact]
    public void BiologySurveyHonorsNearBodySelectionPreference()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.1,"Landable":true}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                BodyName = "Test 1",
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 2,
                    Name = "Test 2",
                },
            });

        Assert.True(viewModel.BiologySurvey!.IsSystemOverview);
        Assert.False(viewModel.ShouldShowBioSystem);

        viewModel.DrawBodyBiosOnlyWhenNear = false;

        Assert.True(viewModel.BiologySurvey!.IsBodyDetail);
        Assert.Equal("Test 2 biology", viewModel.BiologySurvey.Heading);
        Assert.True(viewModel.ShouldShowBioSystem);
    }

    [Fact]
    public void BiologySurveyShowsCanonnHintOnlyForNonlocalSelectedBody()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"PlanetClass":"Rocky body","MassEM":0.1,"Landable":true}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 2","BodyID":2,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                BodyName = "Test 1",
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 2,
                    Name = "Test 2",
                },
            });
        viewModel.UseExternalData = true;
        viewModel.AutoShowPriorScans = true;
        viewModel.DrawBodyBiosOnlyWhenNear = false;
        viewModel.UpdateCanonnSystemPoi(new CanonnSystemPoiResult(
            "Test",
            [new CanonnSurfaceBiologySignal(
                "2",
                "Aleoida Arcus - Green",
                2310101,
                new SurfaceCoordinate(1, 2),
                false)]));

        Assert.Equal(2, viewModel.BiologySurvey!.SelectedBodyId);
        Assert.True(viewModel.HasCanonnBiologyHint);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.Supercruise,
        });

        var rows = viewModel.BiologySurvey!.Bodies;
        Assert.False(rows.Single(row => row.BodyId == 1).HasCanonnSignals);
        Assert.True(rows.Single(row => row.BodyId == 2).HasCanonnSignals);

        viewModel.UseExternalData = false;
        Assert.All(viewModel.BiologySurvey!.Bodies, row =>
            Assert.False(row.HasCanonnSignals));
        viewModel.UseExternalData = true;
        Assert.True(viewModel.BiologySurvey!.Bodies.Single(row =>
            row.BodyId == 2).HasCanonnSignals);

        viewModel.AutoShowPriorScans = false;
        Assert.All(viewModel.BiologySurvey!.Bodies, row =>
            Assert.False(row.HasCanonnSignals));
        viewModel.AutoShowPriorScans = true;
        Assert.True(viewModel.BiologySurvey!.Bodies.Single(row =>
            row.BodyId == 2).HasCanonnSignals);

        viewModel.ApplyUpdate([], new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            BodyName = "Test 2",
        });

        Assert.False(viewModel.HasCanonnBiologyHint);

        viewModel.UpdateCanonnSystemPoi(new CanonnSystemPoiResult(
            "Different system",
            [new CanonnSurfaceBiologySignal(
                "2",
                null,
                2310101,
                new SurfaceCoordinate(1, 2),
                false)]));
        Assert.False(viewModel.HasCanonnBiologyHint);
    }

    [Fact]
    public void BiologySurveyShowsExactCriteriaPredictionsAndHonorsDisableSetting()
    {
        var reference = ExobiologyReferenceCatalog.LoadEmbedded()
            .FindByDisplayName("Aleoida Coronamus - Lime");
        Assert.NotNull(reference);
        var globalRegionalCandidates = RegionalCodexCandidateCatalog.FromEntries(
        [
            new(
                18,
                "Inner Orion Spur",
                reference.EntryId,
                reference.DisplayName ?? "Aleoida Coronamus - Lime"),
        ]);
        var viewModel = CreateViewModel(globalRegionalCandidates);
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0]}"""),
                Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":0,"StarType":"L","StellarMass":1,"Radius":695700000,"SurfaceTemperature":5000}"""),
                Parse(PredictableAleoidaScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        var systemSurvey = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        var bodySummary = Assert.Single(systemSurvey.Bodies);
        Assert.True(bodySummary.HasPredictedReward);
        var systemBand = Assert.Single(bodySummary.RewardBands);
        Assert.True(systemBand.IsPrediction);
        Assert.True(systemBand.MinimumReward > 0);
        Assert.True(systemBand.MaximumReward >= systemBand.MinimumReward);
        Assert.StartsWith("Estimated reward:", systemSurvey.RewardSummary);
        var bodyInformation = Assert.IsType<BodyInformationViewModel>(
            viewModel.BodyInformation);
        Assert.StartsWith(
            "Estimated reward:",
            bodyInformation.BiologicalReward);
        Assert.True(bodyInformation.HasBiologicalReward);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.Fss });

        var bodySurvey = Assert.IsType<BiologySurveyViewModel>(
            viewModel.BiologySurvey);
        var prediction = Assert.Single(bodySurvey.Organisms);
        Assert.Equal("Aleoida Coronamus - Lime", prediction.DisplayName);
        Assert.Equal("Aleoida", prediction.GenusName);
        Assert.Equal("Coronamus", prediction.SpeciesName);
        Assert.Equal("Lime", prediction.VariantName);
        Assert.True(prediction.IsPrediction);
        Assert.False(prediction.IsGenusIdentified);
        Assert.True(prediction.HasReward);
        Assert.False(bodySurvey.HasPredictionStatus);
        Assert.StartsWith("Estimated reward:", bodySurvey.RewardSummary);

        viewModel.UpdateCommanderCodexContext(
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                0,
                null,
                new Dictionary<long, CommanderCodexFirst>()),
            new CommanderCodexData(
                "fid",
                "Cmdr Test",
                18,
                "Inner Orion Spur",
                new Dictionary<long, CommanderCodexFirst>()),
            18);
        prediction = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.True(prediction.IsGlobalRegionalFirst);
        Assert.False(prediction.IsCommanderFirst);
        Assert.False(prediction.IsRegionalFirst);
        Assert.True(prediction.IsHighlightedFirst);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.SystemMap });
        var candidateBand = Assert.Single(Assert.Single(
            viewModel.BiologySurvey!.Bodies).RewardBands);
        Assert.True(candidateBand.IsPrediction);
        Assert.True(candidateBand.IsHighlighted);
        Assert.True(candidateBand.IsGlobalRegionalFirst);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.Fss });

        viewModel.DisableBioPredictions = true;

        var genus = Assert.Single(viewModel.BiologySurvey!.Organisms);
        Assert.Equal("Aleoida", genus.DisplayName);
        Assert.False(genus.IsPrediction);
        Assert.True(genus.IsGenusIdentified);
        Assert.Equal("Reward pending identification", viewModel.BiologySurvey.RewardSummary);
    }

    [Fact]
    public void FssTuningStateMatchesLegacyScanAndIndicatorTransitions()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var viewModel = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json")),
            utcNow: () => now);
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":4}"""),
            ],
            new EliteStatus { GuiFocus = GuiFocus.Fss });

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test A","BodyID":1,"StarType":"K","Parents":[{"Star":0}]}""")],
            null);

        Assert.Equal(FssTuningDetectionState.Waiting, viewModel.FssTuningState);
        Assert.Equal("⏳", viewModel.FssTuningIndicator);
        var waiting = Assert.IsType<FssTuningCaptureRequest>(
            viewModel.CreateFssTuningCaptureRequest());
        viewModel.ApplyFssTuningAnalysis(
            waiting.Revision,
            new FssTuningAnalysis(
                FssTuningDetectionState.White,
                new FssPixelRegion(1, 1, 1, 1),
                30,
                0,
                null));
        Assert.Equal(FssTuningDetectionState.White, viewModel.FssTuningState);
        Assert.Equal("⏳", viewModel.FssTuningIndicator);

        now = now.AddMilliseconds(300);
        viewModel.RefreshTransientState();
        Assert.False(viewModel.HasFssTuningIndicator);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":2,"PlanetClass":"Rocky body","Parents":[{"Star":0}]}""")],
            null);
        viewModel.ApplyUpdate(
            [Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 2","BodyID":3,"PlanetClass":"Rocky body","Parents":[{"Star":0}]}""")],
            null);
        Assert.Equal(FssTuningDetectionState.Skipped, viewModel.FssTuningState);
        var skipped = Assert.IsType<FssTuningCaptureRequest>(
            viewModel.CreateFssTuningCaptureRequest());

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Scan","SystemAddress":42,"BodyName":"Test 2 A Ring","BodyID":4,"PlanetClass":"Rocky body","Parents":[{"Planet":2},{"Ring":3}]}""")],
            null);
        Assert.Equal(
            skipped.Revision,
            viewModel.CreateFssTuningCaptureRequest()?.Revision);

        viewModel.ApplyFssTuningAnalysis(
            skipped.Revision,
            new FssTuningAnalysis(
                FssTuningDetectionState.Yellow,
                new FssPixelRegion(1, 1, 1, 1),
                30,
                8,
                null));
        now = now.AddMilliseconds(300);
        viewModel.RefreshTransientState();
        Assert.Equal("📡", viewModel.FssTuningIndicator);

        viewModel.ApplyUpdate([], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        Assert.Equal(FssTuningDetectionState.None, viewModel.FssTuningState);
        Assert.Null(viewModel.CreateFssTuningCaptureRequest());
    }

    [Fact]
    public void FssTuningCapabilityStatusIsOnlyShownWhileEnabled()
    {
        var viewModel = CreateViewModel();
        viewModel.UpdateFssTuningDetectorStatus("Wayland capture unavailable.");

        Assert.True(viewModel.HasFssTuningDetectorStatus);
        viewModel.FssTuningDetectorEnabled = false;
        Assert.False(viewModel.HasFssTuningDetectorStatus);
        Assert.False(
            new SystemSurveySettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json"))
                .Load()
                .FssTuningDetector
                .Enabled);
    }

    [Fact]
    public void GuiFocusOverridesPhysicalModeForSurveyVisibility()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":1,"NonBodyCount":0}"""),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
                GuiFocus = GuiFocus.InternalPanel,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });

        Assert.False(viewModel.ShouldShowBodyInfo);
        Assert.False(viewModel.ShouldShowBioSystem);
        Assert.False(viewModel.ShouldShowSystemStatus);
    }

    [Fact]
    public void ActiveBuildProjectsSuppressLegacySurveyOverlayGroup()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse("""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}"""),
            ],
            new EliteStatus
            {
                GuiFocus = GuiFocus.SystemMap,
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Test 1",
                },
            });
        viewModel.SuppressForActiveBuildProjects = true;

        Assert.True(viewModel.ShouldShowBodyInfo);
        Assert.True(viewModel.ShouldShowBioSystem);

        viewModel.SetActiveBuildProjects(true);

        Assert.False(viewModel.ShouldShowBodyInfo);
        Assert.False(viewModel.ShouldShowBioSystem);
        Assert.True(viewModel.ShouldSuppressForActiveBuildProjects);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private SystemSurveyViewModel CreateViewModel(
        RegionalCodexCandidateCatalog? regionalCodexCandidates = null)
    {
        return new SystemSurveyViewModel(new SystemSurveySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json")),
            regionalCodexCandidates: regionalCodexCandidates);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private const string TerraformableScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "TerraformState":"Terraformable",
          "PlanetClass":"High metal content body",
          "MassEM":1.2,
          "DistanceFromArrivalLS":100,
          "Landable":true,
          "WasDiscovered":false,
          "WasMapped":false
        }
        """;

    private const string BodyInformationScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "StarSystem":"Test",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "DistanceFromArrivalLS":123.4,
          "TerraformState":"Terraformable",
          "PlanetClass":"High metal content body",
          "Atmosphere":"thin carbon dioxide atmosphere",
          "AtmosphereType":"CarbonDioxide",
          "AtmosphereComposition":[
            {"Name":"CarbonDioxide","Percent":99.0},
            {"Name":"SulphurDioxide","Percent":1.0}
          ],
          "Volcanism":"minor silicate vapour geysers volcanism",
          "MassEM":1.2,
          "Radius":6000000,
          "SurfaceGravity":12.0,
          "SurfaceTemperature":300,
          "SurfacePressure":1000,
          "Landable":true,
          "Materials":[
            {"Name":"iron","Percent":20.0},
            {"Name":"yttrium","Percent":1.0}
          ],
          "Rings":[
            {"Name":"Test 1 A Ring","RingClass":"eRingClass_Rocky","InnerRad":1,"OuterRad":2}
          ],
          "WasDiscovered":false,
          "WasMapped":false,
          "WasFootfalled":false
        }
        """;

    private const string PredictableAleoidaScan = """
        {
          "event":"Scan",
          "ScanType":"Detailed",
          "StarSystem":"Test",
          "SystemAddress":42,
          "BodyName":"Test 1",
          "BodyID":1,
          "Parents":[{"Star":0}],
          "DistanceFromArrivalLS":500,
          "PlanetClass":"Rocky body",
          "Atmosphere":"thin carbon dioxide atmosphere",
          "AtmosphereType":"CarbonDioxide",
          "AtmosphereComposition":[
            {"Name":"CarbonDioxide","Percent":100}
          ],
          "Volcanism":"",
          "MassEM":0.1,
          "Radius":6000000,
          "SurfaceGravity":2,
          "SurfaceTemperature":185,
          "SurfacePressure":3000,
          "SemiMajorAxis":100000,
          "Landable":true,
          "Materials":[{"Name":"Iron","Percent":20}]
        }
        """;
}
