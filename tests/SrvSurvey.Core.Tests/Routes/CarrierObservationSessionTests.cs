using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class CarrierObservationSessionTests
{
    [Theory]
    [InlineData(FollowRouteCarrierKind.Personal)]
    [InlineData(FollowRouteCarrierKind.Squadron)]
    public void UnsupportedTypesCannotAdvanceRoutesOrTeachACarrierIdentity(FollowRouteCarrierKind kind)
    {
        var session = new CarrierObservationSession(false);
        var hop = new FollowRouteHop("Sol", 42, null, null, false, false);
        var now = new DateTimeOffset(2026, 11, 3, 5, 0, 0, TimeSpan.Zero);
        session.Apply(
            [Parse("""{"event":"CarrierStats","CarrierID":999,"CarrierType":"Starport"}""")],
            null,
            kind,
            [hop],
            now
        );
        Assert.False(
            session.AcceptsArrival(
                Parse("""{"event":"CarrierJump","CarrierType":"Starport","StarSystem":"Sol","SystemAddress":42}"""),
                hop
            )
        );
        string supported = kind == FollowRouteCarrierKind.Personal ? "FleetCarrier" : "SquadronCarrier";
        Assert.True(
            session.AcceptsArrival(
                Parse(
                    $$"""{"event":"CarrierJump","CarrierID":123,"CarrierType":"{{supported}}","StarSystem":"Sol","SystemAddress":42}"""
                ),
                hop
            )
        );
        session.Apply(
            [
                Parse(
                    """{"timestamp":"2026-11-03T05:00:00Z","event":"CarrierJumpRequest","CarrierID":999,"CarrierType":"Starport","SystemName":"Sol","DepartureTime":"2026-11-03T05:15:00Z"}"""
                ),
            ],
            null,
            kind,
            [hop],
            now
        );
        Assert.False(session.Countdown.IsActive);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }
}
