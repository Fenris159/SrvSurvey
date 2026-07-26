using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationFleetCarrierIdentityTrackerTests
{
    [Theory]
    [InlineData(
        "ReceiveText",
        "\"From\":\"Supply carrier | ABC-123\"",
        "Supply carrier")]
    [InlineData(
        "FSSSignalDiscovered",
        "\"SignalType\":\"FleetCarrier\","
            + "\"SignalName\":\"Rescue Wing ABC-123\"",
        "Rescue Wing")]
    public void ResolvesLegacyCarrierDisplayName(
        string eventName,
        string properties,
        string expected)
    {
        var tracker = new ColonizationFleetCarrierIdentityTracker();
        tracker.Apply(Event(eventName, properties));

        Assert.Equal(expected, tracker.ResolveDisplayName("ABC-123"));
    }

    [Fact]
    public void IgnoresNonCarrierFssSignals()
    {
        var tracker = new ColonizationFleetCarrierIdentityTracker();
        tracker.Apply(Event(
            "FSSSignalDiscovered",
            "\"SignalType\":\"Installation\","
                + "\"SignalName\":\"Not a carrier ABC-123\""));

        Assert.Equal(string.Empty, tracker.ResolveDisplayName("ABC-123"));
    }

    private static JournalEventEnvelope Event(
        string eventName,
        string properties)
    {
        var json = $$"""
            {"event":"{{eventName}}",{{properties}}}
            """;
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }
}
