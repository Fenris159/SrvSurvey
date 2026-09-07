using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningWorkspaceViewModelTests
{
    [Fact]
    public void MiningNotificationsRemainShipOnlyAndRecoveryIsPaused()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            var vm = new MiningWorkspaceViewModel(directory, new Resolver(), bookmarks);
            var context = new JournalSessionState();
            Assert.True(JournalEventEnvelope.TryParse("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", out var entry, out _));
            context.Apply(entry!);
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            vm.Apply(new JournalMonitorUpdate(null, [entry!], ship, null, null, null, [], true), context, null, ship);
            vm.Settings.OverlaysOnlyDuringSession = false;
            vm.Settings.HideInSupercruise = true;
            vm.Settings.OverlaysOnlyDuringSession = true;
            foreach (var status in new[] { new EliteStatus { Flags = StatusFlags.InSrv }, new EliteStatus { Flags = StatusFlags.InFighter }, new EliteStatus { Flags = StatusFlags.InMainShip | StatusFlags.Supercruise }, new EliteStatus { Flags = StatusFlags.InMainShip, Flags2 = StatusFlags2.OnFoot }, new EliteStatus() })
            {
                vm.Apply(new JournalMonitorUpdate(null, [], status, null, null, null, [], false), context, null, status);
                Assert.False(vm.ShouldShowNotifications);
            }
            vm.StartCommand.Execute(null);
            var recovered = new MiningWorkspaceViewModel(directory, new Resolver(), bookmarks);
            recovered.Apply(new JournalMonitorUpdate(null, [], ship, null, null, null, [], true), context, null, ship);
            Assert.NotNull(recovered.Current?.PausedAt);
            vm.Dispose(); recovered.Dispose();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void FullCargoWaitsForOneMinuteAndDoesNotRepeatOrSurviveCommanderLoss()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var clock = new Clock();
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory), clock: clock);
            var context = new JournalSessionState();
            Assert.True(JournalEventEnvelope.TryParse("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", out var load, out _));
            context.Apply(load!);
            Assert.True(JournalEventEnvelope.TryParse("""{"event":"Loadout","CargoCapacity":10,"Ship":"python"}""", out var capacity, out _));
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            vm.Apply(new JournalMonitorUpdate(null, [load!, capacity!], ship, null, null, null, [], true), context, new CargoSnapshot(clock.GetUtcNow(), "Cargo", "Ship", 10, [new CargoItem("platinum", null, 10, 0)]), ship);
            vm.StartCommand.Execute(null);
            vm.Tick(); clock.Now += TimeSpan.FromSeconds(59); vm.Tick(); Assert.Empty(vm.Notices);
            clock.Now += TimeSpan.FromSeconds(1); vm.Tick(); Assert.Single(vm.Notices);
            clock.Now += TimeSpan.FromMinutes(2); vm.Tick(); Assert.Single(vm.Notices);
            vm.Apply(new JournalMonitorUpdate(null, [], ship, null, null, null, [], false), new JournalSessionState(), null, ship);
            Assert.False(vm.StartCommand.CanExecute(null));
            Assert.False(vm.ShouldShowNotifications);
            Assert.NotNull(vm.Current?.PausedAt);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ProspectReportPersistsAndCollectedMaterialsAreNotDisplacedByRefining()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var clock = new Clock();
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory), clock: clock);
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                var entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, null, ship);
            }
            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", true);
            vm.StartCommand.Execute(null);
            using var overlay = new MiningActivityOverlayViewModel(vm, false);
            Feed("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:01Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}""");
            Feed("""{"event":"MaterialCollected","timestamp":"2026-09-06T12:00:02Z","Category":"Raw","Name":"chromium","Count":3}""");
            for (var second = 3; second <= 9; second++)
                Feed($$"""{"event":"MiningRefined","timestamp":"2026-09-06T12:00:0{{second}}Z","Type":"platinum","Type_Localised":"Platinum"}""");

            Assert.Contains(vm.VisibleNotices, notice => notice.Kind == "Collected" && notice.Text.Contains("chromium", StringComparison.OrdinalIgnoreCase));
            clock.Now += TimeSpan.FromMinutes(2);
            vm.Tick();
            Assert.Empty(vm.VisibleNotices);
            Assert.True(vm.ShouldShowNotifications);
            Assert.Equal("Platinum 35.0% · Remaining 100%", vm.CurrentProspectText);
            Assert.True(overlay.HasProspectReport);
            Assert.Equal(vm.CurrentProspectText, overlay.ProspectReport);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void LiveProspectorResumesPausedSessionBeforeShowingItsReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory), clock: new Clock());
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                var entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, null, ship);
            }

            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", true);
            vm.StartCommand.Execute(null);
            vm.PauseCommand.Execute(null);

            Feed("""{"event":"LaunchDrone","timestamp":"2026-09-06T12:02:00Z","Type":"Prospector"}""");

            Assert.Null(vm.Current!.PausedAt);
            Assert.Equal(1, vm.Current.ProspectorLimpets);

            Feed("""{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:02:01Z","Materials":[{"Name":"Platinum","Proportion":35}],"Remaining":100}""");
            Assert.True(vm.ShouldShowNotifications);
            Assert.Equal("Platinum 35.0% · Remaining 100%", vm.CurrentProspectText);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MiningBackupRestoresNamedFiregroupsAndKeepsOldArchivesCompatible()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var restoredDirectory = Path.Combine(directory, "restored");
        try
        {
            var context = new JournalSessionState();
            var events = new[] { FiregroupsWorkspaceViewModelTests.Event("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python","ShipID":1}"""), FiregroupsWorkspaceViewModelTests.Loadout(1, "Survey Python") };
            foreach (var entry in events) context.Apply(entry);
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            var update = new JournalMonitorUpdate(null, events, ship, null, null, null, [], true);
            var firegroups = new FiregroupsWorkspaceViewModel(directory);
            firegroups.Apply(update, context, ship);
            firegroups.Primary[0].SelectedModule = firegroups.Primary[0].Options[0];
            firegroups.ConfigurationName = "Backup configuration";
            firegroups.SaveCommand.Execute(null);
            using var source = new MiningWorkspaceViewModel(directory, new Resolver(), new BookmarksViewModel(directory), firegroups: firegroups);
            source.Apply(update, context, null, ship);
            var bytes = await source.BackupPackageAsync();
            var targetFiregroups = new FiregroupsWorkspaceViewModel(restoredDirectory);
            targetFiregroups.Apply(update, context, ship);
            using var target = new MiningWorkspaceViewModel(restoredDirectory, new Resolver(), new BookmarksViewModel(restoredDirectory), firegroups: targetFiregroups);
            target.Apply(update, context, null, ship);
            await target.RestorePackageAsync(bytes);
            Assert.Equal("Backup configuration", Assert.Single(targetFiregroups.SavedProfiles).Name);
            Assert.Equal("Survey Python", targetFiregroups.ActiveProfile!.Ship.Name);
            Assert.NotNull(targetFiregroups.Primary[0].SelectedModule);
            Assert.Single(Directory.GetFiles(Path.Combine(restoredDirectory, "firegroups"), "*.before-restore"));
            var reloaded = new FiregroupsWorkspaceViewModel(restoredDirectory);
            reloaded.Apply(update, context, ship);
            Assert.Equal("Backup configuration", reloaded.ActiveProfile!.Name);
            var oldArchive = SrvSurvey.Core.Mining.MiningBackup.Create(new(), "[]");
            await target.RestorePackageAsync(oldArchive);
            Assert.Single(targetFiregroups.SavedProfiles);
            Assert.False(targetFiregroups.Restore("OtherCommander", firegroups.Backup("F1")));
            Assert.Single(targetFiregroups.SavedProfiles);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MiningTripCanBeConfiguredRecordedBookmarkedAndReviewedWithoutLosingHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var clock = new Clock();
        try
        {
            var bookmarks = new BookmarksViewModel(directory);
            using var vm = new MiningWorkspaceViewModel(directory, new Resolver(), bookmarks, clock: clock);
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                var entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                vm.Apply(new(null, [entry], ship, null, null, null, [], bootstrap), context, null, ship);
            }
            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", true);
            Feed("""{"timestamp":"2026-09-06T12:00:00Z","event":"Location","StarSystem":"Wille","StarPos":[1,2,3],"Body":"Wille A Ring"}""", true);
            vm.TargetMaterial = "Platinum"; vm.ThresholdText = "25"; vm.SetThreshold(false);
            vm.PresetName = "High yield"; vm.SaveAnnouncementPreset();
            vm.SetThreshold(true); Assert.Empty(vm.Settings.Thresholds);
            vm.LoadAnnouncementPreset(); Assert.Equal(25, vm.Settings.Thresholds["platinum"]);
            Assert.Contains("High yield", vm.PresetNames);
            vm.ThresholdText = "101"; vm.SetThreshold(false); Assert.Contains("0 to 100", vm.Status);
            vm.StartCommand.Execute(null);
            Feed("""{"timestamp":"2026-09-06T12:00:01Z","event":"ProspectedAsteroid","Materials":[{"Name":"Platinum","Proportion":42}],"Content":"High"}""");
            Feed("""{"timestamp":"2026-09-06T12:00:02Z","event":"MiningRefined","Type":"platinum"}""");
            Feed("""{"timestamp":"2026-09-06T12:00:03Z","event":"MaterialCollected","Category":"Raw","Name":"iron","Count":3}""");
            Assert.Equal(1, vm.Current!.RefinedTons);
            Assert.Single(vm.Prospects); Assert.Single(vm.EngineeringMaterials); Assert.Equal(3, vm.Notices.Count);
            vm.AdjustQuality(-1); Assert.Equal(-1, vm.Current.QualityAdjustments["platinum"]);
            vm.AddAsteroidCommand.Execute(null); Assert.Equal(2, vm.Current.Asteroids);
            vm.RemoveAsteroidCommand.Execute(null); Assert.Equal(1, vm.Current.Asteroids);
            vm.RefineryMineral = "Platinum"; vm.RefineryTons = 2; vm.SaveRefineryEstimate();
            Assert.Contains("2 t", vm.RefinerySummary); Assert.Equal(1, vm.Current.RefinedTons);
            vm.RefineryTons = 0; vm.SaveRefineryEstimate(); Assert.Empty(vm.Current.RefineryEstimates);
            vm.PauseCommand.Execute(null); Assert.NotNull(vm.Current.PausedAt);
            vm.PauseCommand.Execute(null); Assert.Null(vm.Current.PausedAt);
            clock.Now += TimeSpan.FromMinutes(10); vm.StopCommand.Execute(null);
            Assert.Null(vm.Current); var report = Assert.Single(vm.History);
            vm.SelectedSession = report; vm.Notes = "Keep the ring context"; vm.SaveNotesCommand.Execute(null);
            Assert.Equal(vm.Notes, report.Notes);
            vm.DeleteSelectedReport(); Assert.Empty(vm.History);
            vm.UndoDeleteReport(); Assert.Same(report, Assert.Single(vm.History));
            var exported = SrvSurvey.Core.Mining.MiningReport.Csv(vm.History);
            vm.ImportReports(exported); Assert.Single(vm.History);
            Assert.Contains("Imported 0", vm.Status);
            var journalPath = Path.Combine(directory, "import.log");
            await File.WriteAllLinesAsync(journalPath, [
                "not a journal event",
                """{"event":"LoadGame","FID":"OTHER","Ship":"python"}""",
                """{"event":"Scan","BodyName":"Ignore me","Rings":[{"Name":"Wrong A Ring"}]}""",
                """{"event":"LoadGame","FID":"F1","Ship":"python"}""",
                """{"event":"Location","StarSystem":"Wille","StarPos":[1,2,3]}""",
                """{"timestamp":"2026-09-06T12:10:00Z","event":"Scan","BodyName":"Wille","ReserveLevel":"Pristine","DistanceFromArrivalLS":400,"Rings":[{"Name":"Wille A Ring","RingClass":"eRingClass_Metallic"}]}""",
                """{"timestamp":"2026-09-06T12:10:01Z","event":"SAASignalsFound","BodyName":"Wille A Ring","Signals":[{"Type_Localised":"Platinum","Count":2}]}"""
            ]);
            await vm.ImportJournalsAsync([journalPath]);
            var ring = Assert.Single(vm.Rings);
            Assert.Equal("Wille A Ring", ring.Body); Assert.Equal(2, ring.Hotspots["Platinum"]);
            vm.SelectedRing = ring; vm.BookmarkRingCommand.Execute(null);
            Assert.Equal(ring.Body, Assert.Single(bookmarks.All).Body);
            vm.Filter = "no match"; Assert.Empty(vm.Rings);
            vm.Filter = "Platinum"; Assert.Single(vm.Rings);
            await vm.ImportJournalsAsync([journalPath]); Assert.Single(vm.Rings);
            vm.Destination = "Unknown"; await vm.CalculateDistanceAsync(); Assert.Equal("System not found.", vm.DistanceResult);
            var backup = vm.Backup(); Assert.True(vm.Restore(backup));
            Assert.Single(vm.History); Assert.Single(vm.Rings);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
