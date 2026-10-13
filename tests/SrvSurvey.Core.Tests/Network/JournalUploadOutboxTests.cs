using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

/// <summary>Exercises real durable storage boundaries shared by journal senders.</summary>
[Trait("Category", "JournalUploadScenario")]
public sealed class JournalUploadOutboxTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-upload-store-" + Guid.NewGuid().ToString("N")
    );
    private readonly List<string> messages = [];

    /// <summary>Retains source receipts, cooldowns, owner isolation, and exclusive ownership across reopening.</summary>
    [Fact]
    public void RecoveryIsOwnedAndPreservesReceiptsAndCooldowns()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddHours(1);
        using (JournalUploadOutbox<Payload> first = Create())
        {
            Assert.True(first.TryOpen("F123", out IReadOnlyList<Payload>? empty));
            Assert.Empty(empty!);
            Assert.True(first.TryOpen("F123", out IReadOnlyList<Payload>? unchanged));
            Assert.Null(unchanged);
            Assert.Single(first.Enqueue([new("one", "Sol"), new("one", "Sol")]));
            first.DeferUntil(deadline);
            first.DeferUntil(deadline);
            using JournalUploadOutbox<Payload> competing = Create();
            Assert.False(competing.TryOpen("F123", out _));
        }
        using (JournalUploadOutbox<Payload> recovered = Create())
        {
            Assert.True(recovered.TryOpen("F123", out IReadOnlyList<Payload>? retained));
            Assert.Equal("Sol", Assert.Single(retained!).Text);
            Assert.Equal(deadline, recovered.NotBefore);
            recovered.Complete(["one", "unknown"]);
            recovered.Complete(["one"]);
            Assert.Empty(recovered.Enqueue([new("one", "Sol")]));
            Assert.True(recovered.TryOpen("F456", out IReadOnlyList<Payload>? other));
            Assert.Empty(other!);
            Assert.Single(recovered.Enqueue([new("one", "Sirius")]));
        }
        using JournalUploadOutbox<Payload> final = Create();
        Assert.True(final.TryOpen("F123", out IReadOnlyList<Payload>? acknowledged));
        Assert.Empty(acknowledged!);
        Assert.Empty(final.Enqueue([new("one", "Sol")]));
        Assert.NotEmpty(messages);
    }

    /// <summary>Discards an unfinished last append and skips malformed entries without losing adjacent valid records.</summary>
    [Fact]
    public void TornAndMalformedRecordsDoNotDiscardOtherUploads()
    {
        using (JournalUploadOutbox<Payload> first = Create())
        {
            first.TryOpen("F123", out _);
            first.Enqueue([new("one", "Sol"), new("two", "Sirius")]);
        }
        string path = Assert.Single(Directory.GetFiles(Path.Combine(directory, "test"), "*.jsonl"));
        var row = JObject.Parse(File.ReadAllText(path));
        ((JArray)row["Change"]!["Added"]!).Insert(1, JObject.Parse("{\"Id\":null,\"Text\":\"broken\"}"));
        File.WriteAllText(path, row.ToString(Newtonsoft.Json.Formatting.None) + "\n{malformed}\n{\"unfinished\":");
        using JournalUploadOutbox<Payload> recovered = Create();
        Assert.True(recovered.TryOpen("F123", out IReadOnlyList<Payload>? pending));
        Assert.Equal(["Sol", "Sirius"], pending!.Select(value => value.Text));
        Assert.EndsWith("\n", File.ReadAllText(path), StringComparison.Ordinal);
        recovered.Enqueue([new("three", "Lave")]);
        Assert.Equal(3, recovered.ReadPending().Count);
        Assert.Equal(2, messages.Count);
    }

    /// <summary>A storage failure stops new sends while a known acknowledgement waits for a replacement checkpoint.</summary>
    [Fact]
    public void FailedAcknowledgementCheckpointDoesNotRequeueAcceptedPayloadInMemory()
    {
        using (JournalUploadOutbox<Payload> outbox = Create())
        {
            outbox.TryOpen("F123", out _);
            outbox.Enqueue([new("one", "Sol")]);
            string path = Assert.Single(Directory.GetFiles(Path.Combine(directory, "test"), "*.jsonl"));
            File.Move(path, path + ".held");
            Directory.CreateDirectory(path);
            outbox.Complete(["one"]);
            Assert.Empty(outbox.ReadPending());
            Exception? failure = Record.Exception(() => outbox.Enqueue([new("two", "Sirius")]));
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Directory.Delete(path);
            File.Move(path + ".held", path);
            Assert.Single(outbox.Enqueue([new("two", "Sirius")]));
        }
        using JournalUploadOutbox<Payload> recovered = Create();
        Assert.True(recovered.TryOpen("F123", out IReadOnlyList<Payload>? pending));
        Assert.Equal("Sirius", Assert.Single(pending!).Text);
        Assert.Empty(recovered.Enqueue([new("one", "Sol")]));
        Assert.NotEmpty(messages);
    }

    /// <summary>Revocation before commander selection prevents old retained reports from returning after opt-in.</summary>
    [Fact]
    public void RevocationInvalidatesOtherLeasesAndUnselectedOwners()
    {
        using JournalUploadOutbox<Payload> active = Create();
        active.TryOpen("F123", out _);
        active.Enqueue([new("one", "Sol")]);
        using JournalUploadOutbox<Payload> revoke = Create();
        revoke.ClearAll();
        Assert.False(active.HasConsent);
        Assert.False(active.TryOpen("F123", out _));
        Assert.Throws<IOException>(() => active.Enqueue([new("two", "Sirius")]));
        active.AcceptConsent();
        Assert.True(active.TryOpen("F123", out IReadOnlyList<Payload>? afterConsent));
        Assert.Empty(afterConsent!);
        active.Enqueue([new("two", "Sirius")]);
        active.Clear();
        Assert.Empty(active.ReadPending());
        active.Complete(["two"]);
        Assert.Single(active.Enqueue([new("two", "Sirius")]));
        Assert.NotEmpty(messages);
    }

    /// <summary>Compacts a grown log without changing retained payloads or completed record identities.</summary>
    [Fact]
    public void CompactionPreservesPendingPayloadsAndReceipts()
    {
        using (JournalUploadOutbox<Payload> outbox = Create())
        {
            outbox.TryOpen("F123", out _);
            outbox.Enqueue([new("large", new string('x', 9 * 1024 * 1024))]);
            outbox.Complete(["large"]);
            outbox.Enqueue([new("small", "Sol")]);
        }
        using JournalUploadOutbox<Payload> recovered = Create();
        Assert.True(recovered.TryOpen("F123", out IReadOnlyList<Payload>? pending));
        Assert.Equal("Sol", Assert.Single(pending!).Text);
        Assert.Empty(recovered.Enqueue([new("large", "same source")]));
    }

    /// <summary>Compaction after bounded receipt eviction must not move a recent acknowledgement into the oldest slot.</summary>
    [Fact]
    public void CompactionPreservesReceiptEvictionOrderAcrossRestart()
    {
        using (JournalUploadOutbox<Payload> seed = Create())
        {
            seed.TryOpen("F123", out _);
            seed.Enqueue([new("seed", "Sol")]);
        }
        string path = Assert.Single(Directory.GetFiles(Path.Combine(directory, "test"), "*.jsonl"));
        var row = JObject.Parse(File.ReadAllText(path));
        row["Change"]!["Added"] = new JArray();
        row["Change"]!["Receipts"] = new JArray(Enumerable.Range(0, 65536).Select(index => "old-" + index));
        File.WriteAllText(path, row.ToString(Newtonsoft.Json.Formatting.None) + "\n");
        using (JournalUploadOutbox<Payload> outbox = Create())
        {
            Assert.True(outbox.TryOpen("F123", out _));
            outbox.Enqueue([new("recent-one", "Sol")]);
            outbox.Complete(["recent-one"]);
            outbox.Enqueue([new("recent-two", "Sol")]);
            outbox.Complete(["recent-two"]);
            outbox.Enqueue([new("large", new string('x', 9 * 1024 * 1024))]);
            outbox.Complete(["large"]);
        }
        using JournalUploadOutbox<Payload> reopened = Create();
        Assert.True(reopened.TryOpen("F123", out _));
        Assert.Empty(reopened.Enqueue([new("recent-two", "Sol")]));
        Assert.Single(reopened.Enqueue([new("old-2", "Sol")]));
    }

    /// <summary>Rejects capacity overflow instead of dropping the oldest reports to make room.</summary>
    [Fact]
    public void FullQueueRetainsPreviouslyCapturedReports()
    {
        using JournalUploadOutbox<Payload> outbox = Create();
        Assert.Throws<IOException>(() => outbox.Enqueue([new("unowned", "Sol")]));
        outbox.TryOpen("F123", out _);
        Payload[] full = Enumerable
            .Range(0, 4096)
            .Select(index => new Payload(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Sol"))
            .ToArray();
        Assert.Equal(4096, outbox.Enqueue(full).Count);
        Assert.Throws<IOException>(() => outbox.Enqueue([new("extra", "Sirius")]));
        Assert.Equal(4096, outbox.ReadPending().Count);
        outbox.Dispose();
        Assert.Throws<ObjectDisposedException>(() => outbox.TryOpen("F123", out _));
    }

    /// <summary>A long-deferred payload remains protected when newer acknowledged source receipts roll out of the bounded ledger.</summary>
    [Fact]
    public void LongPendingPayloadReceivesAFreshReceiptWhenItFinallyCompletes()
    {
        using (JournalUploadOutbox<Payload> outbox = Create())
        {
            outbox.TryOpen("F123", out _);
            outbox.Enqueue([new("waiting", "Sol")]);
            for (int batch = 0; batch < 17; batch++)
            {
                Payload[] current = Enumerable
                    .Range(batch * 4095, 4095)
                    .Select(index => new Payload(
                        "later-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "Sirius"
                    ))
                    .ToArray();
                outbox.Enqueue(current);
                outbox.Complete(current.Select(item => item.Id));
            }
            Assert.Equal("Sol", Assert.Single(outbox.ReadPending()).Text);
            outbox.Complete(["waiting"]);
        }
        using JournalUploadOutbox<Payload> recovered = Create();
        Assert.True(recovered.TryOpen("F123", out IReadOnlyList<Payload>? pending));
        Assert.Empty(pending!);
        Assert.Empty(recovered.Enqueue([new("waiting", "Sol")]));
        Assert.Single(recovered.Enqueue([new("later-0", "Sirius")]));
    }

    /// <summary>Recovers other valid records when a committed row contains invalid UTF-8 bytes.</summary>
    [Fact]
    public void InvalidUtf8RowDoesNotBlockIndependentValidUploads()
    {
        using (JournalUploadOutbox<Payload> first = Create())
        {
            first.TryOpen("F123", out _);
            first.Enqueue([new("one", "Sol")]);
        }
        string path = Assert.Single(Directory.GetFiles(Path.Combine(directory, "test"), "*.jsonl"));
        using (var file = new FileStream(path, FileMode.Append, FileAccess.Write))
        {
            file.Write([0xff, (byte)'\n']);
        }
        using JournalUploadOutbox<Payload> recovered = Create();
        Assert.True(recovered.TryOpen("F123", out IReadOnlyList<Payload>? pending));
        Assert.Equal("Sol", Assert.Single(pending!).Text);
        Assert.NotEmpty(messages);
    }

    /// <summary>Creates real isolated leased storage with explicit validation of retained provider values.</summary>
    private JournalUploadOutbox<Payload> Create() =>
        new(
            directory,
            "test",
            value => value.Id,
            value => !string.IsNullOrWhiteSpace(value.Id) && value.Text is not null,
            messages.Add
        );

    /// <summary>Removes this fixture's isolated storage after all leases have closed.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Models an immutable captured payload at the durable storage boundary.</summary>
    private sealed record Payload(string Id, string Text);
}
