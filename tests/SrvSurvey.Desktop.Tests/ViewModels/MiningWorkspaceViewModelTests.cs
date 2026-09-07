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
            JournalEventEnvelope.TryParse("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", out var entry, out _);
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
            JournalEventEnvelope.TryParse("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"python"}""", out var load, out _);
            context.Apply(load!);
            JournalEventEnvelope.TryParse("""{"event":"Loadout","CargoCapacity":10,"Ship":"python"}""", out var capacity, out _);
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
