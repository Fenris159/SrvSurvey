using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class JournalEventEnvelopeTests
{
    [Fact]
    public void TryParsePreservesUnknownEventPayload()
    {
        const string json = """
            {"timestamp":"2026-07-24T10:00:00Z","event":"FutureEvent","Nested":{"Value":42}}
            """;

        var parsed = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(journalEvent);
        Assert.Equal("FutureEvent", journalEvent.EventName);
        Assert.Equal(42, journalEvent.Payload.GetProperty("Nested").GetProperty("Value").GetInt32());
        Assert.Equal(json, journalEvent.RawJson);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("[]")]
    [InlineData("{\"timestamp\":\"2026-07-24T10:00:00Z\"}")]
    public void TryParseReportsInvalidLines(string line)
    {
        Assert.False(JournalEventEnvelope.TryParse(line, out var journalEvent, out var error));
        Assert.Null(journalEvent);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
