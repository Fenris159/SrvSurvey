using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningWorkspaceViewModelTests
{
    [Fact]
    public void ShipMiningPanelsNeverAppearInRhinoOrOnFootAndRecoveryIsPaused()
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
            vm.SaveFiregroup();
            Assert.True(vm.ShouldShowFiregroups);
            foreach (var status in new[] { new EliteStatus { Flags = StatusFlags.InSrv }, new EliteStatus { Flags = StatusFlags.InMainShip, Flags2 = StatusFlags2.OnFoot }, new EliteStatus() })
            {
                vm.Apply(new JournalMonitorUpdate(null, [], status, null, null, null, [], false), context, null, status);
                Assert.False(vm.ShouldShowFiregroups);
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
