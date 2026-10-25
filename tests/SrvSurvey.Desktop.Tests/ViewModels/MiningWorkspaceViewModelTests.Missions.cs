using System.Globalization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MiningWorkspaceViewModelTests
{
    [Fact]
    public void MissionOverlayRetainsItsListAcrossIdleTicksAndRemovesExpiredMissions()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Accept(1);
        DateTimeOffset firstExpiry = fixture.Now.AddDays(1);
        fixture.Now = fixture.Now.AddHours(1);
        fixture.Accept(2);
        using var overlay = new MiningMissionsOverlayViewModel(fixture.Workspace);
        IReadOnlyList<MiningMissionCardViewModel> original = overlay.Missions;
        MiningMissionCardViewModel first = original.Single(card => card.Id == 1);
        string remaining = first.TimeRemaining;

        fixture.Now = fixture.Now.AddMinutes(1);
        fixture.Workspace.Tick();

        Assert.Same(original, overlay.Missions);
        Assert.NotEqual(remaining, first.TimeRemaining);

        fixture.Now = firstExpiry;
        fixture.Workspace.Tick();

        IReadOnlyList<MiningMissionCardViewModel> afterExpiry = overlay.Missions;
        Assert.NotSame(original, afterExpiry);
        Assert.Equal(2, Assert.Single(afterExpiry).Id);
        Assert.False(first.IsActive);
        Assert.True(fixture.Workspace.ShouldShowMissions);

        fixture.Now = fixture.Now.AddMinutes(1);
        fixture.Workspace.Tick();

        Assert.Same(afterExpiry, overlay.Missions);

        fixture.Now = firstExpiry.AddHours(1);
        fixture.Workspace.Tick();

        IReadOnlyList<MiningMissionCardViewModel> empty = overlay.Missions;
        Assert.Empty(empty);
        Assert.False(fixture.Workspace.ShouldShowMissions);
        fixture.Workspace.Tick();
        Assert.Same(empty, overlay.Missions);
    }

    [Fact]
    public void MissionCardsTrackDeliveriesCargoAndPersistCommanderChoices()
    {
        using var fixture = new MiningMissionFixture();
        MiningWorkspaceViewModel vm = fixture.Workspace;
        using var overlay = new MiningMissionsOverlayViewModel(vm);
        var changed = new List<string?>();
        overlay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(vm.HasMissionCards);
        Assert.False(overlay.HasMissions);
        fixture.Accept(1);
        fixture.Accept(2);
        Assert.Null(vm.Current);
        Assert.True(vm.ShouldShowMissions);
        Assert.True(vm.HasMissionCards);
        Assert.Equal(2, overlay.Missions.Count);
        fixture.Feed(
            """{"event":"CargoDepot","MissionID":1,"UpdateType":"Deliver","ItemsDelivered":4}""",
            cargo: new(fixture.Now, "Cargo", "Ship", 8, [new("platinum", null, 8, 0)])
        );
        Assert.Equal(6, vm.MissionCards[0].OnBoard);
        Assert.Equal(2, vm.MissionCards[1].OnBoard);
        Assert.Equal(4, vm.MissionCards[0].Delivered);
        MiningMissionCardViewModel first = vm.MissionCards[0];
        first.IsTracked = false;
        first.IsTracked = false;
        Assert.Single(overlay.Missions);
        Assert.Equal(2, overlay.Missions[0].Id);
        Assert.False(new MiningStore(fixture.Directory).Load("F1").Missions[0].IsTracked);
        IReadOnlyList<MiningMissionCardViewModel> stableCards = vm.MissionCards;
        vm.Tick();
        Assert.Same(stableCards, vm.MissionCards);
        fixture.Accept(1);
        Assert.False(vm.MissionCards[0].IsTracked);
        Assert.Contains(nameof(overlay.Missions), changed);
        Assert.Contains(nameof(overlay.HasMissions), changed);
        vm.MissionCards[1].FindHotspotsCommand.Execute(null);
        Assert.Equal(4, vm.SelectedTab);
        Assert.Equal("Platinum", vm.Search.Mineral);
        Assert.Contains("Platinum", vm.Search.RingMineralChips.Selected);
        vm.MissionsSortCommand.Execute("Id");
        vm.MissionsSortCommand.Execute("Id");
        Assert.Equal(2, vm.MissionCards[0].Id);
        fixture.Feed("""{"event":"LoadGame","FID":"F2","Commander":"Other"}""");
        Assert.Empty(vm.MissionCards);
        first.IsTracked = true; // A stale card cannot modify another commander's state.
        Assert.Empty(new MiningStore(fixture.Directory).Load("F2").Missions);
        fixture.Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test"}""");
        Assert.False(vm.MissionCards.Single(card => card.Id == 1).IsTracked);
        using var recovered = new MiningWorkspaceViewModel(
            fixture.Directory,
            new MiningMissionFixture.Resolver(),
            new BookmarksViewModel(fixture.Directory)
        );
        var context = new JournalSessionState();
        JournalEventEnvelope load = MiningMissionFixture.Event(
            """{"event":"LoadGame","FID":"F1","Commander":"Test"}"""
        );
        context.Apply(load);
        recovered.Apply(new(null, [load], null, null, null, null, [], true), context, null, null);
        Assert.False(recovered.MissionCards.Single(card => card.Id == 1).IsTracked);
        Assert.False(recovered.ShouldShowMissions);
        overlay.Dispose();
        changed.Clear();
        fixture.Accept(3);
        Assert.Empty(changed);
    }

    [Fact]
    public void MissionOverlayRemainsAvailableDuringTravelAndHidesOnFootOrAfterExpiry()
    {
        using var fixture = new MiningMissionFixture();
        MiningWorkspaceViewModel vm = fixture.Workspace;
        fixture.Accept(1);
        vm.Settings.OverlaysOnlyDuringSession = true;
        fixture.Feed(
            """{"event":"SupercruiseEntry"}""",
            new() { Flags = StatusFlags.InMainShip | StatusFlags.Supercruise }
        );
        Assert.True(vm.ShouldShowMissions);
        fixture.Feed("""{"event":"Disembark"}""", new() { Flags2 = StatusFlags2.OnFoot });
        Assert.False(vm.ShouldShowMissions);
        fixture.Feed("""{"event":"Embark"}""");
        Assert.True(vm.ShouldShowMissions);
        fixture.Feed("""{"event":"CargoDepot","MissionID":1,"UpdateType":"Deliver","ItemsDelivered":10}""");
        Assert.True(vm.MissionCards[0].IsComplete);
        Assert.True(vm.ShouldShowMissions); // Ready to hand in, still an active mission.
        fixture.Feed("""{"event":"MissionCompleted","MissionID":1}""");
        Assert.False(vm.ShouldShowMissions);
        fixture.Accept(2);
        fixture.Now = fixture.Now.AddDays(2);
        vm.Tick();
        Assert.False(vm.ShouldShowMissions);
        Assert.Equal("Expired", vm.MissionCards.Single(card => card.Id == 2).Status);
    }

    [Fact]
    public void MissionShortcutIsTheSameBindingInMiningAndMasterInput()
    {
        using var fixture = new MiningMissionFixture();
        var store = new GlobalInputSettingsStore(Path.Combine(fixture.Directory, "ui.json"));
        var input = new GlobalInputSettingsViewModel(
            store,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows),
            new SessionInputDevices()
        );
        var panels = new OverlayPanelVisibilityViewModel(
            new OverlayPanelVisibilitySettingsStore(Path.Combine(fixture.Directory, "ui.json")),
            input,
            new OverlayWindowRegistry()
        );
        OverlayPanelVisibilityEntryViewModel mission = panels
            .ForCategory(OverlaySettingsCategory.Mining)
            .Single(panel => panel.PlotterName == "PlotMiningMissions");
        InputBindingViewModel master = input.Bindings.Single(binding =>
            binding.Definition.Action == GlobalInputAction.ToggleMiningMissionsVisibility
        );
        Assert.Same(master, mission.Shortcut);
        mission.Shortcut.Chord = "CTRL M";
        Assert.Equal("CTRL M", store.Load().Bindings[GlobalInputAction.ToggleMiningMissionsVisibility]);
        master.Chord = "CTRL J";
        Assert.Equal("CTRL J", mission.Shortcut.Chord);
        input.ResetBindingsCommand.Execute(null);
        Assert.Equal(string.Empty, mission.Shortcut.Chord);
        Assert.Equal(string.Empty, store.Load().Bindings[GlobalInputAction.ToggleMiningMissionsVisibility]);
    }

    [Theory]
    [InlineData("", "Active", "No expiry reported", true)]
    [InlineData("invalid", "Failed", "No expiry reported", false)]
    [InlineData("2026-10-25T14:30:00Z", "Active", "2h 30m remaining", true)]
    [InlineData("2026-10-27T15:00:00Z", "Active", "2d 3h remaining", true)]
    [InlineData("2026-10-25T11:00:00Z", "Active", "Expired", false)]
    public void CardFormatsDestinationProgressExpiryAndRewards(
        string expiry,
        string status,
        string expected,
        bool active
    )
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z", CultureInfo.InvariantCulture);
        bool tracked = true;
        int finds = 0;
        var mission = new MiningMission
        {
            Id = 42,
            Commodity = "platinum",
            Required = 10,
            OnBoard = 3,
            Delivered = 4,
            Destination = "Sol / Galileo",
            Reward = 1000000,
            Expiry = expiry,
            Status = status,
        };
        var card = new MiningMissionCardViewModel(mission, now, value => tracked = value, () => finds++);
        Assert.Equal(42, card.Id);
        Assert.Equal("Platinum", card.Commodity);
        Assert.Equal("Galileo @ Sol", card.Destination);
        Assert.Equal("Sol", card.System);
        Assert.True(card.HasSystem);
        Assert.Equal(expected, card.TimeRemaining);
        Assert.Equal(active, card.IsActive);
        Assert.Equal(expiry.Contains("11:00", StringComparison.Ordinal) ? "Expired" : status, card.Status);
        Assert.Equal($"{1000000:N0} CR", card.Reward);
        Assert.Equal($"{100000:N0} CR/t", card.RewardPerTon);
        Assert.Equal("On board: 3/10 t", card.CargoProgress);
        Assert.Equal("Delivered: 4/10 t", card.DeliveryProgress);
        Assert.Equal("3 t needed", card.Needed);
        Assert.Equal(30, card.CargoPercentage);
        Assert.Equal(40, card.DeliveryPercentage);
        Assert.False(card.IsComplete);
        card.IsTracked = false;
        Assert.False(tracked);
        card.FindHotspotsCommand.Execute(null);
        Assert.Equal(1, finds);
        var empty = new MiningMissionCardViewModel(new() { Status = "Completed" }, now, _ => { }, () => { });
        Assert.False(empty.HasSystem);
        Assert.Equal("", empty.Destination);
        Assert.Equal("", empty.RewardPerTon);
        Assert.Equal(0, empty.CargoPercentage);
        Assert.Equal(0, empty.DeliveryPercentage);
        Assert.True(empty.IsComplete);
    }
}

internal sealed class MiningMissionFixture : IDisposable
{
    private readonly JournalSessionState context = new();
    private readonly MissionClock clock = new();
    public string Directory { get; } =
        Path.Combine(Path.GetTempPath(), "SrvSurvey-missions-" + Guid.NewGuid().ToString("N"));
    public MiningWorkspaceViewModel Workspace { get; }
    public DateTimeOffset Now
    {
        get => clock.Now;
        set => clock.Now = value;
    }

    public MiningMissionFixture()
    {
        Workspace = new(Directory, new Resolver(), new BookmarksViewModel(Directory), clock: clock);
        Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}""");
        Feed("""{"event":"Location","Docked":true,"StarSystem":"Ngolona","StationName":"Huss Port"}""");
    }

    public void Accept(int id) =>
        Feed(
            $$"""{"event":"MissionAccepted","Name":"Mission_Mining_Wing","MissionID":{{id}},"Commodity":"$platinum_name;","Count":10,"Reward":1000000,"Expiry":"{{Now.AddDays(1):O}}"}"""
        );

    public void Feed(string json, EliteStatus? status = null, CargoSnapshot? cargo = null)
    {
        JournalEventEnvelope entry = Event(json);
        context.Apply(entry);
        status ??= new() { Flags = StatusFlags.InMainShip };
        Workspace.Apply(new(null, [entry], status, null, null, null, [], false), context, cargo, status);
    }

    public static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }

    public void Dispose()
    {
        Workspace.Dispose();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }

    internal sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class MissionClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            DateTimeOffset.Parse("2026-10-25T12:00:00Z", CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
