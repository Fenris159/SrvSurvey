using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class PulseOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-pulse-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LiveFileActivityPulsesForTenSecondsButBootstrapDoesNot()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        var viewModel = CreateViewModel(time);
        var journalEvent = Parse("{\"event\":\"FSDJump\"}");

        viewModel.ApplyUpdate([journalEvent], null, true);
        Assert.Equal(0, viewModel.PulseHeight);

        viewModel.ApplyUpdate([journalEvent], null, false);
        Assert.Equal(20, viewModel.PulseHeight);

        time.Advance(TimeSpan.FromSeconds(5));
        viewModel.Refresh();
        Assert.Equal(10, viewModel.PulseHeight);

        time.Advance(TimeSpan.FromSeconds(5));
        viewModel.Refresh();
        Assert.Equal(0, viewModel.PulseHeight);
    }

    [Fact]
    public void MapsHideTheOverlayWithoutDiscardingThePulse()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = CreateViewModel(time);

        viewModel.ApplyUpdate(
            [],
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap },
            false);

        Assert.False(viewModel.ShouldShow);
        Assert.Equal(20, viewModel.PulseHeight);

        viewModel.ApplyUpdate(
            [],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                GuiFocus = GuiFocus.NoFocus,
            },
            false);

        Assert.True(viewModel.ShouldShow);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Music","MusicTrack":"SystemMap"}""")],
            null,
            false);
        Assert.False(viewModel.ShouldShow);

        viewModel.ApplyUpdate(
            [Parse("""{"event":"Music","MusicTrack":"Exploration"}""")],
            null,
            false);
        Assert.True(viewModel.ShouldShow);
    }

    [Fact]
    public void ScoIndicatorTracksActiveCooldownAndReadyStates()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = CreateViewModel(time);
        var active = new EliteStatus
        {
            Flags2 = StatusFlags2.SupercruiseOverdrive,
        };

        viewModel.ApplyUpdate([], active, false);
        Assert.True(viewModel.IsScoActive);
        Assert.False(viewModel.IsScoCoolingDown);

        viewModel.ApplyUpdate([], new EliteStatus(), false);
        Assert.False(viewModel.IsScoActive);
        Assert.True(viewModel.IsScoCoolingDown);
        Assert.Equal(0, viewModel.ScoIndicatorTop);

        time.Advance(TimeSpan.FromSeconds(9));
        viewModel.Refresh();
        Assert.False(viewModel.IsScoCoolingDown);
        Assert.True(viewModel.IsScoReady);
        Assert.Equal(18, viewModel.ScoIndicatorTop);

        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.Refresh();
        Assert.False(viewModel.IsScoReady);
    }

    [Fact]
    public void DisabledPreferencePersistsAndSuppressesOverlay()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = CreateViewModel(time);

        viewModel.Enabled = false;

        Assert.False(viewModel.ShouldShow);
        Assert.False(new PulseOverlaySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"))
            .Load().Enabled);
    }

    [Fact]
    public void HideJournalWriteTimerInvertsEnabledAndPersists()
    {
        var viewModel = CreateViewModel(new MutableTimeProvider(DateTimeOffset.UtcNow));
        Assert.True(viewModel.Enabled);
        Assert.False(viewModel.HideJournalWriteTimer);

        viewModel.HideJournalWriteTimer = true;

        Assert.True(viewModel.HideJournalWriteTimer);
        Assert.False(viewModel.Enabled);
        Assert.False(viewModel.ShouldShow);
        Assert.False(new PulseOverlaySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"))
            .Load().Enabled);

        viewModel.HideJournalWriteTimer = false;

        Assert.False(viewModel.HideJournalWriteTimer);
        Assert.True(viewModel.Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private PulseOverlayViewModel CreateViewModel(TimeProvider timeProvider)
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new PulseOverlayViewModel(
            new PulseOverlaySettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            timeProvider);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error), error);
        return journalEvent!;
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public void Advance(TimeSpan duration)
        {
            value += duration;
        }
    }
}
