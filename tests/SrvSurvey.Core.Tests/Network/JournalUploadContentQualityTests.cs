using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

[Trait("Category", "JournalUploadScenario")]
public sealed class JournalUploadContentQualityTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "srv-content-receipts-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void ObservationReceiptsSurviveRestartAndKeepNewTimesAndOwnersDistinct()
    {
        const string first = """{"event":"Scan","timestamp":"2026-11-10T12:00:00Z","BodyID":4,"MassEM":3560.053223}""";
        const string reordered =
            """{"MassEM":3560.053223,"BodyID":4,"timestamp":"2026-11-10T12:00:00Z","event":"Scan"}""";
        using (JournalUploadOutbox<Report> outbox = Create())
        {
            Assert.True(outbox.TryOpen("F1:First", out _));
            Assert.Equal(
                ["record-a"],
                outbox.Enqueue([new("record-a", first), new("record-b", reordered)]).Select(item => item.Identity)
            );
            outbox.Complete(["record-a"], contentAccepted: true);
        }
        using JournalUploadOutbox<Report> reopened = Create();
        Assert.True(reopened.TryOpen("F1:First", out IReadOnlyList<Report>? recovered));
        Assert.Empty(recovered!);
        Assert.Empty(reopened.Enqueue([new("record-c", reordered)]));
        Assert.Equal(
            ["record-d"],
            reopened
                .Enqueue([new("record-d", first.Replace("12:00:00", "12:00:01", StringComparison.Ordinal))])
                .Select(item => item.Identity)
        );
        Assert.True(reopened.TryOpen("F2:Other", out _));
        Assert.Equal(["record-e"], reopened.Enqueue([new("record-e", first)]).Select(item => item.Identity));
    }

    [Fact]
    public void RejectedReportsDoNotBecomeAcceptedContentReceipts()
    {
        const string scan = """{"event":"Scan","timestamp":"2026-11-10T12:00:00Z","BodyID":4}""";
        using JournalUploadOutbox<Report> outbox = Create();
        Assert.True(outbox.TryOpen("F1:First", out _));
        Assert.Single(outbox.Enqueue([new("rejected", scan)]));
        outbox.Complete(["rejected"], contentAccepted: false);
        Assert.Equal(["new-source"], outbox.Enqueue([new("new-source", scan)]).Select(item => item.Identity));
        outbox.Complete(["new-source"], contentAccepted: true);
        Assert.Empty(outbox.Enqueue([new("duplicate", scan)]));
    }

    [Fact]
    public void IdenticalPersonalTransactionsRetainTheirDistinctJournalEvidence()
    {
        const string transaction =
            """{"event":"MarketSell","timestamp":"2026-11-10T12:00:00Z","Type":"gold","Count":2,"SellPrice":1000}""";
        using JournalUploadOutbox<Report> outbox = Create();
        Assert.True(outbox.TryOpen("F1:First", out _));
        Assert.Equal(
            ["trade-a", "trade-b"],
            outbox.Enqueue([new("trade-a", transaction), new("trade-b", transaction)]).Select(item => item.Identity)
        );
        outbox.Complete(["trade-a", "trade-b"]);
        Assert.Equal(["trade-c"], outbox.Enqueue([new("trade-c", transaction)]).Select(item => item.Identity));
    }

    [Fact]
    public void FailedPersistenceDoesNotConsumeTheObservationFingerprint()
    {
        using JournalUploadOutbox<Report> outbox = Create();
        Assert.True(outbox.TryOpen("F1:First", out _));
        Assert.Single(outbox.Enqueue([new("first", """{"event":"Scan","timestamp":"2026-11-10T12:00:00Z"}""")]));
        string file = Assert.Single(Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories));
        var next = new Report("next", """{"event":"Scan","timestamp":"2026-11-10T12:00:01Z"}""");
        using (var blocked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => outbox.Enqueue([next]));
        }
        Assert.Equal(["next"], outbox.Enqueue([next]).Select(item => item.Identity));
    }

    private JournalUploadOutbox<Report> Create() =>
        new(
            root,
            "test",
            item => item.Identity,
            item => !string.IsNullOrWhiteSpace(item.Json),
            _ => { },
            item =>
                JournalUploadIdentity.CaptureObservation(JObject.Parse(item.Json).Value<string>("event")!, item.Json)
        );

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private sealed record Report(string Identity, string Json);
}
