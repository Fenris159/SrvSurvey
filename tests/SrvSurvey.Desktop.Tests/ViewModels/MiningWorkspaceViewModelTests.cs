using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningWorkspaceViewModelTests
{
    [Fact]
    public void JournalContextDefaultsSearchLocationAndPledgedPower()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            JournalEventEnvelope[] entries =
            [
                FiregroupsWorkspaceViewModelTests.Event(
                    """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}"""
                ),
                FiregroupsWorkspaceViewModelTests.Event(
                    """{"event":"Location","StarSystem":"Harma","StarPos":[1,2,3]}"""
                ),
                FiregroupsWorkspaceViewModelTests.Event("""{"event":"PowerplayJoin","Power":"Archon Delaine"}"""),
            ];
            foreach (JournalEventEnvelope entry in entries)
            {
                context.Apply(entry);
            }

            vm.Apply(new(null, entries, ship, null, null, null, [], true), context, null, ship);

            Assert.Equal("Harma", vm.Search.Reference);
            Assert.Equal("Harma", vm.Search.CurrentSystem);
            Assert.Equal("Archon Delaine", vm.Search.PledgedPower);
            vm.Search.Reference = "Sol";
            vm.SelectedTab = 3;
            Assert.Equal("Harma", vm.Search.Reference);
            Assert.Equal("Reinforce", vm.Search.Objective);
            vm.Search.Reference = "Sol";
            vm.SelectedTab = 4;
            vm.SelectedTab = 3;
            Assert.Equal("Sol", vm.Search.Reference);
            vm.Search.UpdateCurrentLocation("Wille");
            Assert.Equal("Sol", vm.Search.Reference);
            vm.Search.UseCurrentLocation();
            Assert.Equal("Wille", vm.Search.Reference);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void PledgeRecoveryUsesSuppliedJournalsForTheActiveCommander()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        try
        {
            File.WriteAllLines(
                Path.Combine(journals, "Journal.2026-09-20T000000.01.log"),
                [
                    """{"event":"Commander","Name":"Test","FID":"F1"}""",
                    """{"event":"Powerplay","Power":"Archon Delaine"}""",
                    """{"event":"Commander","Name":"Other","FID":"F2"}""",
                    """{"event":"Powerplay","Power":"Felicia Winters"}""",
                ]
            );
            var context = new JournalSessionState();
            JournalEventEnvelope login = FiregroupsWorkspaceViewModelTests.Event(
                """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}"""
            );
            context.Apply(login);
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };

            using var live = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            live.UseJournalDirectories([journals]);
            live.Apply(new(null, [login], ship, null, null, null, [], true), context, null, ship);
            Assert.Equal("Archon Delaine", live.Search.PledgedPower);

            using var replay = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory)
            );
            replay.UseJournalDirectories([]);
            replay.Apply(new(null, [login], ship, null, null, null, [], true), context, null, ship);
            Assert.Equal("Any", replay.Search.PledgedPower);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PersistentProspectsChimeAndCargoOverlayFollowMiningSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var speech = new SpeechRecorder();
        var chime = new ChimeRecorder();
        try
        {
            using var vm = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: new Clock(),
                announcementOutputs: new MiningAnnouncementOutputs(speech, chime)
            );
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, CargoSnapshot? cargo = null, bool bootstrap = false)
            {
                JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, cargo, ship);
            }

            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", bootstrap: true);
            Feed("""{"event":"Loadout","CargoCapacity":960,"Ship":"python"}""");
            vm.StartCommand.Execute(null);
            vm.Settings.PersistentProspectSlots = 2;
            vm.Settings.PlayProspectChime = true;
            vm.Settings.SpeakAnnouncements = true;
            vm.Settings.Thresholds["platinum"] = 30;
            vm.Settings.Thresholds["osmium"] = 30;

            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:30Z","Materials":[{"Name":"Bertrandite","Proportion":25}],"Remaining":100}"""
            );
            Assert.False(vm.HasPersistentProspects);
            Assert.False(vm.ShouldShowNotifications);

            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:01:00Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""
            );
            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:00Z","MotherlodeMaterial":"Monazite","Materials":[{"Name":"Osmium","Proportion":32}],"Remaining":100}"""
            );
            CargoSnapshot cargo = new(
                DateTimeOffset.UtcNow,
                "Cargo",
                "Ship",
                410,
                [new CargoItem("drones", "Limpets", 167, 0), new CargoItem("platinum", "Platinum", 243, 0)]
            );
            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:03:00Z","Materials":[{"Name":"Bertrandite","Proportion":25}],"Remaining":100}""",
                cargo
            );

            Assert.Equal(2, vm.PersistentProspects.Count);
            Assert.DoesNotContain(vm.PersistentProspects, result => result.Summary.Contains("Platinum"));
            Assert.Contains(vm.PersistentProspects, result => result.Qualifies && result.Summary.Contains("Osmium"));
            Assert.Contains(vm.PersistentProspects, result => result.Summary.Contains("Core: Monazite"));
            using var activityOverlay = new MiningActivityOverlayViewModel(vm, false);
            MiningProspectOverlayRowViewModel visibleProspect = Assert.Single(activityOverlay.Prospects);
            Assert.Contains("Core: Monazite", visibleProspect.Summary);
            Assert.DoesNotContain(activityOverlay.Prospects, result => result.Summary.Contains("Bertrandite"));
            Assert.Equal(2, chime.PlayedVolumes.Length);
            Assert.Equal(2, speech.Messages.Count);
            Assert.True(vm.ShouldShowCargo);
            using var cargoOverlay = new MiningCargoOverlayViewModel(vm);
            Assert.Equal("410 / 960 T", cargoOverlay.Capacity);
            Assert.Equal("550 T REMAINING", cargoOverlay.Remaining);
            Assert.Equal("Limpets", cargoOverlay.Items[0].Name);
            Assert.True(cargoOverlay.Items.Single(item => item.Name == "Platinum").IsTarget);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ChimePreviewUsesSelectedPortableCueAndVolume()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var chime = new ChimeRecorder();
        try
        {
            using var vm = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                announcementOutputs: new MiningAnnouncementOutputs(new SpeechRecorder(), chime)
            );
            Assert.Equal(["Two-tone", "High-low", "Crystal"], vm.ChimeOptions);
            vm.Settings.Chime = "Crystal";
            vm.Settings.ChimeVolume = 42;

            vm.PreviewChime();

            Assert.Equal(("Crystal", 42), Assert.Single(chime.Played));
            Assert.Contains("Crystal", vm.Status);
            Assert.Contains("42%", vm.Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void RingReferenceProvidesLaserAndCorePricesForEveryRingType()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));

            Assert.Equal(
                ["Icy rings", "Metallic rings", "Metal-rich rings", "Rocky rings"],
                vm.RingReferences.Select(ring => ring.Name)
            );
            Assert.All(
                vm.RingReferences,
                ring =>
                {
                    Assert.NotEmpty(ring.Laser);
                    Assert.NotEmpty(ring.Core);
                    Assert.All(
                        ring.Laser.Concat(ring.Core),
                        commodity =>
                        {
                            Assert.True(commodity.AverageSellPrice > 0);
                            Assert.EndsWith(" CR/t", commodity.AverageSellPriceLabel);
                        }
                    );
                }
            );
            Assert.Equal(
                70136,
                vm.RingReferences.Single(ring => ring.Name == "Metallic rings")
                    .Laser.Single(item => item.Name == "Platinum")
                    .AverageSellPrice
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void CargoOverlayCoversPreviewUnknownCapacityAndLivePropertyChanges()
    {
        using (var preview = new MiningCargoOverlayViewModel(null))
        {
            Assert.Equal("431 / 960 T", preview.Capacity);
            Assert.Equal("529 T REMAINING", preview.Remaining);
            Assert.Equal(44.9, preview.FillPercentage);
            Assert.True(preview.HasItems);
            Assert.Equal("167", preview.Items[0].CountLabel);
        }

        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            JournalEventEnvelope load = FiregroupsWorkspaceViewModelTests.Event(
                """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}"""
            );
            context.Apply(load);
            vm.Apply(new(null, [load], ship, null, null, null, [], true), context, null, ship);
            var overlay = new MiningCargoOverlayViewModel(vm);
            var changed = new List<string?>();
            overlay.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            CargoSnapshot cargo = new(
                DateTimeOffset.UtcNow,
                "Cargo",
                "Ship",
                3,
                [new CargoItem("bertrandite", "", 3, 0), new CargoItem("gold", "Gold", 0, 0)]
            );
            vm.Apply(new(null, [], ship, null, null, null, [], false), context, cargo, ship);

            Assert.Equal("3 T / UNKNOWN", overlay.Capacity);
            Assert.Equal("0 T REMAINING", overlay.Remaining);
            Assert.Equal(0, overlay.FillPercentage);
            Assert.Single(overlay.Items);
            Assert.Equal("Bertrandite", overlay.Items[0].Name);
            Assert.False(overlay.Items[0].IsTarget);
            Assert.Contains(nameof(MiningCargoOverlayViewModel.Items), changed);
            Assert.Contains(nameof(MiningCargoOverlayViewModel.HasItems), changed);

            changed.Clear();
            vm.TargetMaterial = "Bertrandite";
            vm.ThresholdText = "10";
            vm.SetThreshold(false);
            Assert.True(Assert.Single(overlay.Items).IsTarget);
            Assert.Contains(nameof(MiningCargoOverlayViewModel.Items), changed);

            changed.Clear();
            vm.SelectedThreshold = Assert.Single(vm.Thresholds);
            vm.DeleteSelectedThreshold();
            Assert.False(Assert.Single(overlay.Items).IsTarget);
            Assert.Contains(nameof(MiningCargoOverlayViewModel.Items), changed);

            changed.Clear();
            vm.Status = "Unrelated";
            Assert.Empty(changed);

            CargoSnapshot empty = cargo with { Count = 0, Inventory = [] };
            vm.Apply(new(null, [], ship, null, null, null, [], false), context, empty, ship);
            Assert.False(overlay.HasItems);

            overlay.Dispose();
            changed.Clear();
            vm.Apply(new(null, [], ship, null, null, null, [], false), context, cargo, ship);
            Assert.Empty(changed);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void AnnouncementEditorsSelectRenameAndDeleteIndependentRows()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            vm.TargetMaterial = "Platinum";
            vm.ThresholdText = "30";
            vm.SetThreshold(false);
            vm.TargetMaterial = "Osmium";
            vm.ThresholdText = "25";
            vm.SetThreshold(false);

            Assert.Equal(2, vm.Thresholds.Count);
            vm.SelectedThreshold = vm.Thresholds.Single(row => row.Name == "platinum");
            vm.DeleteSelectedThreshold();
            Assert.Single(vm.Thresholds);
            Assert.Equal("osmium", vm.Thresholds[0].Name);

            vm.PresetName = "Laser mining";
            vm.SaveAnnouncementPreset();
            vm.PresetName = "High yield";
            vm.SaveAnnouncementPreset();
            Assert.Single(vm.AnnouncementPresets);
            Assert.Equal("High yield", vm.AnnouncementPresets[0].Name);
            vm.PresetName = "HIGH YIELD";
            vm.SaveAnnouncementPreset();
            Assert.Single(vm.Settings.AnnouncementPresets);
            Assert.True(vm.Settings.AnnouncementPresets.ContainsKey("HIGH YIELD"));
            Assert.False(vm.Settings.AnnouncementPresets.ContainsKey("High yield"));
            vm.DeleteAnnouncementPreset();
            Assert.Empty(vm.AnnouncementPresets);
            Assert.Contains("deleted", vm.Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void AnnouncementEditorsExplainInvalidAndEmptyActions()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            vm.NewThreshold();
            Assert.Equal("20", vm.ThresholdText);
            vm.SetThreshold(false);
            Assert.Contains("mineral name", vm.Status);
            vm.TargetMaterial = "Platinum";
            vm.ThresholdText = "101";
            vm.SetThreshold(false);
            Assert.Contains("0 to 100", vm.Status);
            vm.DeleteSelectedThreshold();
            Assert.Contains("Select", vm.Status);

            vm.NewAnnouncementPreset();
            vm.SaveAnnouncementPreset();
            Assert.Empty(vm.AnnouncementPresets);
            vm.DeleteAnnouncementPreset();
            Assert.Contains("Select", vm.Status);

            vm.PresetName = "Missing";
            vm.LoadAnnouncementPreset();
            Assert.Empty(vm.AnnouncementPresets);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void AnnouncementPresetRowsSummarizeEveryAsteroidModeAndCanBeApplied()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            foreach (
                (string Name, bool Cores, bool Lasers, string Label) in new[]
                {
                    ("Both", true, true, "core + laser"),
                    ("Core", true, false, "core only"),
                    ("Laser", false, true, "laser only"),
                    ("Muted", false, false, "muted"),
                }
            )
            {
                vm.Settings.AnnounceCores = Cores;
                vm.Settings.AnnounceNonCores = Lasers;
                vm.PresetName = Name;
                vm.SaveAnnouncementPreset();
                Assert.Contains(Label, vm.AnnouncementPresets.Single(row => row.Name == Name).Summary);
                vm.SelectedAnnouncementPreset = null;
            }

            vm.SelectedAnnouncementPreset = vm.AnnouncementPresets.Single(row => row.Name == "Both");
            vm.LoadAnnouncementPreset();
            Assert.True(vm.Settings.AnnounceCores);
            Assert.True(vm.Settings.AnnounceNonCores);
            Assert.Contains("applied", vm.Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Theory]
    [InlineData(false, false, false, "No local speech service")]
    [InlineData(true, false, false, "No local voices")]
    [InlineData(true, true, false, "Local voices loaded")]
    [InlineData(true, false, true, "Speech unavailable")]
    public async Task VoiceLoadingReportsProviderState(
        bool supported,
        bool hasVoices,
        bool throws,
        string expectedStatus
    )
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var speech = new SpeechRecorder
        {
            Supported = supported,
            Voices = hasVoices ? ["Test voice"] : [],
            Failure = throws ? new TimeoutException("test timeout") : null,
        };
        try
        {
            using var vm = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                announcementOutputs: new MiningAnnouncementOutputs(speech, new ChimeRecorder())
            );

            await vm.LoadVoicesAsync();

            Assert.Contains(expectedStatus, vm.Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void OverlayRowLabelsAndPreviewProspectsCoverActiveAndDepletedStates()
    {
        var active = new MiningProspectOverlayRowViewModel("12:00", "Platinum 35%", 42.5, true);
        MiningProspectOverlayRowViewModel depleted = active with { Remaining = 0 };
        Assert.Equal("42.5% remaining", active.RemainingLabel);
        Assert.Equal("DEPLETED", depleted.RemainingLabel);
        Assert.Equal("Platinum", new MiningThresholdRowViewModel("platinum", 30).DisplayName);
        Assert.Equal("≥ 30.0%", new MiningThresholdRowViewModel("platinum", 30).MinimumLabel);

        using var overlay = new MiningActivityOverlayViewModel(null, false);
        Assert.True(overlay.HasProspectReport);
        Assert.True(overlay.HasQualifyingProspect);
        Assert.Contains("Remaining", overlay.ProspectReport);
    }

    [Fact]
    public void MiningTablesExposeIndependentSortIndicators()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var viewModel = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory)
            );

            viewModel.CargoSortCommand.Execute("Name");
            viewModel.MaterialsSortCommand.Execute("Name");
            viewModel.MaterialsSortCommand.Execute("Name");

            Assert.Equal("↑", viewModel.CargoSortIndicators["Name"]);
            Assert.Equal("↓", viewModel.MaterialsSortIndicators["Name"]);
            Assert.Equal(string.Empty, viewModel.ProspectsSortIndicators["Name"]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void MiningNotificationsRemainShipOnlyAndRecoveryIsPaused()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            var vm = new MiningWorkspaceViewModel(directory, new Resolver(), bookmarks);
            var context = new JournalSessionState();
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""",
                    out JournalEventEnvelope? entry,
                    out _
                )
            );
            context.Apply(entry!);
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            vm.Apply(new JournalMonitorUpdate(null, [entry!], ship, null, null, null, [], true), context, null, ship);
            vm.Settings.OverlaysOnlyDuringSession = false;
            vm.Settings.HideInSupercruise = true;
            vm.Settings.OverlaysOnlyDuringSession = true;
            foreach (
                EliteStatus? status in new[]
                {
                    new EliteStatus { Flags = StatusFlags.InSrv },
                    new EliteStatus { Flags = StatusFlags.InFighter },
                    new EliteStatus { Flags = StatusFlags.InMainShip | StatusFlags.Supercruise },
                    new EliteStatus { Flags = StatusFlags.InMainShip, Flags2 = StatusFlags2.OnFoot },
                    new EliteStatus(),
                }
            )
            {
                vm.Apply(
                    new JournalMonitorUpdate(null, [], status, null, null, null, [], false),
                    context,
                    null,
                    status
                );
                Assert.False(vm.ShouldShowNotifications);
            }
            vm.StartCommand.Execute(null);
            var recovered = new MiningWorkspaceViewModel(directory, new Resolver(), bookmarks);
            recovered.Apply(new JournalMonitorUpdate(null, [], ship, null, null, null, [], true), context, null, ship);
            Assert.NotNull(recovered.Current?.PausedAt);
            vm.Dispose();
            recovered.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void FullCargoWaitsForOneMinuteAndDoesNotRepeatOrSurviveCommanderLoss()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var clock = new Clock();
        try
        {
            using var vm = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: clock
            );
            var context = new JournalSessionState();
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""",
                    out JournalEventEnvelope? load,
                    out _
                )
            );
            context.Apply(load!);
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"event":"Loadout","CargoCapacity":10,"Ship":"python"}""",
                    out JournalEventEnvelope? capacity,
                    out _
                )
            );
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            vm.Apply(
                new JournalMonitorUpdate(null, [load!, capacity!], ship, null, null, null, [], true),
                context,
                new CargoSnapshot(clock.GetUtcNow(), "Cargo", "Ship", 10, [new CargoItem("platinum", null, 10, 0)]),
                ship
            );
            vm.StartCommand.Execute(null);
            vm.Tick();
            clock.Now += TimeSpan.FromSeconds(59);
            vm.Tick();
            Assert.Empty(vm.Notices);
            clock.Now += TimeSpan.FromSeconds(1);
            vm.Tick();
            Assert.Single(vm.Notices);
            clock.Now += TimeSpan.FromMinutes(2);
            vm.Tick();
            Assert.Single(vm.Notices);
            vm.Apply(
                new JournalMonitorUpdate(null, [], ship, null, null, null, [], false),
                new JournalSessionState(),
                null,
                ship
            );
            Assert.False(vm.StartCommand.CanExecute(null));
            Assert.False(vm.ShouldShowNotifications);
            Assert.NotNull(vm.Current?.PausedAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ProspectReportPersistsAndCollectedMaterialsAreNotDisplacedByRefining()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var clock = new Clock();
        try
        {
            using var vm = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: clock
            );
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, null, ship);
            }
            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", true);
            vm.StartCommand.Execute(null);
            using var overlay = new MiningActivityOverlayViewModel(vm, false);
            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:01Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""
            );
            Feed(
                """{"event":"MaterialCollected","timestamp":"2026-09-06T12:00:02Z","Category":"Raw","Name":"chromium","Count":3}"""
            );
            for (int second = 3; second <= 9; second++)
            {
                Feed(
                    $$"""{"event":"MiningRefined","timestamp":"2026-09-06T12:00:0{{second}}Z","Type":"platinum","Type_Localised":"Platinum"}"""
                );
            }

            Assert.Contains(
                vm.VisibleNotices,
                notice =>
                    notice.Kind == "Collected" && notice.Text.Contains("chromium", StringComparison.OrdinalIgnoreCase)
            );
            clock.Now += TimeSpan.FromMinutes(2);
            vm.Tick();
            Assert.Empty(vm.VisibleNotices);
            Assert.True(vm.ShouldShowNotifications);
            Assert.Equal("Platinum 35.0% · Remaining 100%", vm.CurrentProspectText);
            Assert.True(overlay.HasProspectReport);
            Assert.Equal(vm.CurrentProspectText, overlay.ProspectReport);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void LiveProspectorResumesPausedSessionBeforeShowingItsReport()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: new Clock()
            );
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, null, ship);
            }

            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", true);
            vm.StartCommand.Execute(null);
            vm.PauseCommand.Execute(null);

            Feed("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:02:00Z","Type":"Prospector"}""");

            Assert.Null(vm.Current!.PausedAt);
            Assert.Equal(1, vm.Current.ProspectorLimpets);

            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:01Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}"""
            );
            Assert.True(vm.ShouldShowNotifications);
            Assert.Equal("Platinum 35.0% · Remaining 100%", vm.CurrentProspectText);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task MiningBackupRestoresNamedFiregroupsAndKeepsOldArchivesCompatible()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string restoredDirectory = Path.Combine(directory, "restored");
        try
        {
            var context = new JournalSessionState();
            JournalEventEnvelope[] events = new[]
            {
                FiregroupsWorkspaceViewModelTests.Event(
                    """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python","ShipID":1}"""
                ),
                FiregroupsWorkspaceViewModelTests.Loadout(1, "Survey Python"),
            };
            foreach (JournalEventEnvelope? entry in events)
            {
                context.Apply(entry);
            }

            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            var update = new JournalMonitorUpdate(null, events, ship, null, null, null, [], true);
            var firegroups = new FiregroupsWorkspaceViewModel(directory);
            firegroups.Apply(update, context, ship);
            firegroups.Primary[0].SelectedModule = firegroups.Primary[0].Options[0];
            firegroups.ConfigurationName = "Backup configuration";
            firegroups.SaveCommand.Execute(null);
            using var source = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                firegroups: firegroups
            );
            source.Apply(update, context, null, ship);
            byte[] bytes = await source.BackupPackageAsync();
            var targetFiregroups = new FiregroupsWorkspaceViewModel(restoredDirectory);
            targetFiregroups.Apply(update, context, ship);
            using var target = new MiningWorkspaceViewModel(
                restoredDirectory,
                new Resolver(),
                new BookmarksViewModel(restoredDirectory),
                firegroups: targetFiregroups
            );
            target.Apply(update, context, null, ship);
            await target.RestorePackageAsync(bytes);
            Assert.Equal("Backup configuration", Assert.Single(targetFiregroups.SavedProfiles).Name);
            Assert.Equal("Survey Python", targetFiregroups.ActiveProfile!.Ship.Name);
            Assert.NotNull(targetFiregroups.Primary[0].SelectedModule);
            Assert.Single(Directory.GetFiles(Path.Combine(restoredDirectory, "firegroups"), "*.before-restore"));
            var reloaded = new FiregroupsWorkspaceViewModel(restoredDirectory);
            reloaded.Apply(update, context, ship);
            Assert.Equal("Backup configuration", reloaded.ActiveProfile!.Name);
            byte[] oldArchive = SrvSurvey.Core.Mining.MiningBackup.Create(new(), "[]");
            await target.RestorePackageAsync(oldArchive);
            Assert.Single(targetFiregroups.SavedProfiles);
            Assert.False(targetFiregroups.Restore("OtherCommander", firegroups.Backup("F1")));
            Assert.Single(targetFiregroups.SavedProfiles);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task MiningTripCanBeConfiguredRecordedBookmarkedAndReviewedWithoutLosingHistory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var clock = new Clock();
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), bookmarks, clock: clock);
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, null, ship);
            }
            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", true);
            Feed(
                """{"timestamp":"2026-09-06T12:00:00Z","event":"Location","StarSystem":"Wille","StarPos":[1,2,3],"Body":"Wille A Ring"}""",
                true
            );
            vm.TargetMaterial = "Platinum";
            vm.ThresholdText = "25";
            vm.SetThreshold(false);
            vm.PresetName = "High yield";
            vm.SaveAnnouncementPreset();
            vm.SetThreshold(true);
            Assert.Empty(vm.Settings.Thresholds);
            vm.LoadAnnouncementPreset();
            Assert.Equal(25, vm.Settings.Thresholds["platinum"]);
            Assert.Contains("High yield", vm.PresetNames);
            vm.ThresholdText = "101";
            vm.SetThreshold(false);
            Assert.Contains("0 to 100", vm.Status);
            vm.StartCommand.Execute(null);
            Feed(
                """{"timestamp":"2026-09-06T12:00:01Z","event":"ProspectedAsteroid","Materials":[{"Name":"Platinum","Proportion":42}],"Content":"High"}"""
            );
            Feed("""{"timestamp":"2026-09-06T12:00:02Z","event":"MiningRefined","Type":"platinum"}""");
            Feed(
                """{"timestamp":"2026-09-06T12:00:03Z","event":"MaterialCollected","Category":"Raw","Name":"iron","Count":3}"""
            );
            Assert.Equal(1, vm.Current!.RefinedTons);
            Assert.Single(vm.Prospects);
            Assert.Single(vm.EngineeringMaterials);
            Assert.Equal(3, vm.Notices.Count);
            vm.AdjustQuality(-1);
            Assert.Equal(-1, vm.Current.QualityAdjustments["platinum"]);
            vm.AddAsteroidCommand.Execute(null);
            Assert.Equal(2, vm.Current.Asteroids);
            vm.RemoveAsteroidCommand.Execute(null);
            Assert.Equal(1, vm.Current.Asteroids);
            vm.RefineryMineral = "Platinum";
            vm.RefineryTons = 2;
            vm.SaveRefineryEstimate();
            Assert.Contains("2 t", vm.RefinerySummary);
            Assert.Equal(1, vm.Current.RefinedTons);
            vm.RefineryTons = 0;
            vm.SaveRefineryEstimate();
            Assert.Empty(vm.Current.RefineryEstimates);
            vm.PauseCommand.Execute(null);
            Assert.NotNull(vm.Current.PausedAt);
            vm.PauseCommand.Execute(null);
            Assert.Null(vm.Current.PausedAt);
            clock.Now += TimeSpan.FromMinutes(10);
            vm.StopCommand.Execute(null);
            Assert.Null(vm.Current);
            MiningSession report = Assert.Single(vm.History);
            vm.SelectedSession = report;
            vm.Notes = "Keep the ring context";
            vm.SaveNotesCommand.Execute(null);
            Assert.Equal(vm.Notes, report.Notes);
            vm.DeleteSelectedReport();
            Assert.Empty(vm.History);
            vm.UndoDeleteReport();
            Assert.Same(report, Assert.Single(vm.History));
            string exported = SrvSurvey.Core.Mining.MiningReport.Csv(vm.History);
            vm.ImportReports(exported);
            Assert.Single(vm.History);
            Assert.Contains("Imported 0", vm.Status);
            string journalPath = Path.Combine(directory, "import.log");
            await File.WriteAllLinesAsync(
                journalPath,
                [
                    "not a journal event",
                    """{"event":"LoadGame","FID":"OTHER","Ship":"python"}""",
                    """{"event":"Scan","BodyName":"Ignore me","Rings":[{"Name":"Wrong A Ring"}]}""",
                    """{"event":"LoadGame","FID":"F1","Ship":"python"}""",
                    """{"event":"Location","StarSystem":"Wille","StarPos":[1,2,3]}""",
                    """{"timestamp":"2026-09-06T12:10:00Z","event":"Scan","BodyName":"Wille","ReserveLevel":"Pristine","DistanceFromArrivalLS":400,"Rings":[{"Name":"Wille A Ring","RingClass":"eRingClass_Metallic"}]}""",
                    """{"timestamp":"2026-09-06T12:10:01Z","event":"SAASignalsFound","BodyName":"Wille A Ring","Signals":[{"Type_Localised":"Platinum","Count":2}]}""",
                ]
            );
            await vm.ImportJournalsAsync([journalPath]);
            MiningRing ring = Assert.Single(vm.Rings);
            Assert.Equal("Wille A Ring", ring.Body);
            Assert.Equal(2, ring.Hotspots["Platinum"]);
            vm.SelectedRing = ring;
            vm.BookmarkRingCommand.Execute(null);
            Assert.Equal(ring.Body, Assert.Single(bookmarks.All).CombinedBodyAndRing);
            vm.Filter = "no match";
            Assert.Empty(vm.Rings);
            vm.Filter = "Platinum";
            Assert.Single(vm.Rings);
            await vm.ImportJournalsAsync([journalPath]);
            Assert.Single(vm.Rings);
            vm.Destination = "Unknown";
            await vm.CalculateDistanceAsync();
            Assert.Equal("System not found.", vm.DistanceResult);
            string backup = vm.Backup();
            Assert.True(vm.Restore(backup));
            Assert.Single(vm.History);
            Assert.Single(vm.Rings);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ThresholdGroupsShareOnePercentageAndDeleteTogether()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory));
            vm.ThresholdText = "30";
            vm.ThresholdMinerals.Add("Platinum");
            vm.ThresholdMinerals.Add("Osmium");
            vm.AddThresholdGroup();

            Assert.Equal(30, vm.Settings.Thresholds["platinum"]);
            Assert.Equal(30, vm.Settings.Thresholds["osmium"]);
            Assert.Single(vm.ThresholdGroups);
            Assert.Empty(vm.ThresholdMinerals.Selected);

            vm.ThresholdText = "10";
            vm.ThresholdMinerals.Add("Painite");
            vm.AddThresholdGroup();
            Assert.Equal(2, vm.ThresholdGroups.Count);

            vm.SelectedThresholdGroup = vm.ThresholdGroups.First(group => group.Names.Contains("platinum"));
            vm.DeleteThresholdGroup();
            Assert.False(vm.Settings.Thresholds.ContainsKey("platinum"));
            Assert.False(vm.Settings.Thresholds.ContainsKey("osmium"));
            Assert.Equal(10, Assert.Single(vm.Settings.Thresholds).Value);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            DateTimeOffset.Parse("2026-09-06T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class SpeechRecorder : SrvSurvey.Desktop.Platform.IMiningSpeechOutput
    {
        public List<string> Messages { get; } = [];
        public bool Supported { get; init; } = true;
        public IReadOnlyList<string> Voices { get; init; } = ["Test"];
        public Exception? Failure { get; init; }
        public bool IsSupported => Supported;
        public string ProviderName => "Test";

        public Task<IReadOnlyList<string>> GetVoicesAsync() =>
            Failure is null ? Task.FromResult(Voices) : Task.FromException<IReadOnlyList<string>>(Failure);

        public void Speak(string text, string voice, int volume, int rate) => Messages.Add(text);

        public void Dispose() { }
    }

    private sealed class ChimeRecorder : SrvSurvey.Desktop.Platform.IMiningChimeOutput
    {
        public List<(string Chime, int Volume)> Played { get; } = [];
        public int[] PlayedVolumes => Played.Select(entry => entry.Volume).ToArray();

        public void Play(string chime, int volume) => Played.Add((chime, volume));

        public void Dispose() { }
    }
}
