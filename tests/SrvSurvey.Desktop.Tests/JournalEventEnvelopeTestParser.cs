using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Tests;

internal static class JournalEventEnvelopeTestParser
{
    public static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error),
            error
        );
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
