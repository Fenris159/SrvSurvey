using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FleetCarrierJumpCountdownTrackerTests
{
    [Fact]
    public void JumpRequestMovesThroughInitiationLockdownAndCooldownPhases()
    {
        var tracker = new FleetCarrierJumpCountdownTracker();
        var start = DateTimeOffset.Parse("2026-08-01T12:00:00Z");

        Assert.True(tracker.Apply(Parse(
            """
            {"timestamp":"2026-08-01T12:00:00Z","event":"CarrierJumpRequest","CarrierID":123,"SystemName":"Colonia","DepartureTime":"2026-08-01T12:15:00Z"}
            """), start));

        Assert.Equal("DEPARTURE TO COLONIA", tracker.Current.Title);
        Assert.Equal("15:00", tracker.Current.Countdown);
        Assert.Equal("JUMP INITIATION IN", tracker.Current.PhaseLabel);
        Assert.Equal("5:00", tracker.Current.PhaseCountdown);

        tracker.Refresh(start.AddMinutes(7));
        Assert.Equal("8:00", tracker.Current.Countdown);
        Assert.Equal("PAD LOCKDOWN IN", tracker.Current.PhaseLabel);
        Assert.Equal("4:40", tracker.Current.PhaseCountdown);

        tracker.Refresh(start.AddMinutes(12));
        Assert.Equal("3:00", tracker.Current.Countdown);
        Assert.Equal("LANDING PADS LOCKED", tracker.Current.PhaseLabel);
        Assert.False(tracker.Current.HasPhaseCountdown);

        tracker.Refresh(start.AddMinutes(15));
        Assert.Equal("JUMP COOLDOWN", tracker.Current.Title);
        Assert.Equal("5:00", tracker.Current.Countdown);
        Assert.Equal("CARRIER ARRIVED", tracker.Current.PhaseLabel);

        tracker.Refresh(start.AddMinutes(20));
        Assert.Equal(FleetCarrierJumpCountdownState.Inactive, tracker.Current);
    }

    [Fact]
    public void CancellationUsesJournalTimeAndIgnoresAnotherCarrier()
    {
        var tracker = new FleetCarrierJumpCountdownTracker();
        var start = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        tracker.Apply(Parse(
            """
            {"timestamp":"2026-08-01T12:00:00Z","event":"CarrierJumpRequest","CarrierID":123,"SystemName":"Colonia","DepartureTime":"2026-08-01T12:15:00Z"}
            """), start);

        Assert.False(tracker.Apply(Parse(
            """
            {"timestamp":"2026-08-01T12:01:00Z","event":"CarrierJumpCancelled","CarrierID":456}
            """), start));
        Assert.Equal("DEPARTURE TO COLONIA", tracker.Current.Title);

        Assert.True(tracker.Apply(Parse(
            """
            {"timestamp":"2026-08-01T12:01:00Z","event":"CarrierJumpCancelled","CarrierID":123}
            """), start.AddMinutes(1)));
        Assert.Equal("CANCELLATION COOLDOWN", tracker.Current.Title);
        Assert.Equal("1:00", tracker.Current.Countdown);
        Assert.Equal("JUMP CANCELLED", tracker.Current.PhaseLabel);
    }

    [Fact]
    public void CarrierJumpStartsPostJumpCooldownWithoutPriorRequest()
    {
        var tracker = new FleetCarrierJumpCountdownTracker();
        var observed = DateTimeOffset.Parse("2026-08-01T12:15:25Z");

        tracker.Apply(Parse(
            """
            {"timestamp":"2026-08-01T12:15:25Z","event":"CarrierJump","StarSystem":"Colonia"}
            """), observed);

        Assert.True(tracker.Current.IsActive);
        Assert.Equal("JUMP COOLDOWN", tracker.Current.Title);
        Assert.Equal("4:35", tracker.Current.Countdown);
        Assert.Equal("Colonia", tracker.Current.Destination);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }
}
