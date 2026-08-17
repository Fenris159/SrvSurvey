using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelCompletionAuditorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-boxel-audit-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task AuditAppliesLegacySkipRulesAndSkipsCurrentAndEmptyBoxels()
    {
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        var localBoxel = top.Children[0];
        var spanshBoxel = top.Children[1];
        var emptyBoxel = top.Children[2];
        await WriteLocalSystemAsync(
            localBoxel.WithSystemNumber(3),
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var resolver = new StubResolver(boxel =>
            string.Equals(boxel.Prefix, spanshBoxel.Prefix, StringComparison.Ordinal)
                ?
                [
                    Observation(
                        spanshBoxel.WithSystemNumber(5),
                        spanshUpdated: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                        hasKnownBodies: true),
                ]
                : []);
        var auditor = new BoxelCompletionAuditor(
            new LegacySystemDataReader(temporaryDirectory),
            resolver);

        var result = await auditor.AuditAsync(new BoxelCompletionAuditRequest(
            "F123",
            [top, localBoxel, spanshBoxel, emptyBoxel],
            new HashSet<string>(StringComparer.Ordinal) { emptyBoxel.Prefix },
            top.Prefix,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            true,
            true,
            BoxelCompletionMode.EnterSystem,
            []));

        Assert.False(result.WasCancelled);
        Assert.Equal(4, result.Processed);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(2, resolver.Requests.Count);
        Assert.DoesNotContain(
            result.Entries,
            entry => string.Equals(entry.Boxel.Prefix, top.Prefix, StringComparison.Ordinal));
        Assert.Contains(
            result.Entries,
            entry => entry.Boxel.Prefix == localBoxel.Prefix
                && entry.SystemCount == 4
                && entry.IsComplete);
        Assert.Contains(
            result.Entries,
            entry => entry.Boxel.Prefix == spanshBoxel.Prefix
                && entry.SystemCount == 6
                && entry.IsComplete);
        Assert.Contains(
            result.Entries,
            entry => entry.Boxel.Prefix == emptyBoxel.Prefix
                && entry.SystemCount == -1
                && entry.IsEmpty);
    }

    [Fact]
    public async Task AuditReturnsCompletedPartialResultWhenCancelled()
    {
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        var boxels = top.Children.Take(2).ToArray();
        using var cancellation = new CancellationTokenSource();
        var resolver = new StubResolver(boxel =>
        {
            cancellation.Cancel();
            return [Observation(boxel.WithSystemNumber(0))];
        });
        var auditor = new BoxelCompletionAuditor(
            new LegacySystemDataReader(temporaryDirectory),
            resolver);

        var result = await auditor.AuditAsync(
            new BoxelCompletionAuditRequest(
                "F123",
                boxels,
                new HashSet<string>(StringComparer.Ordinal),
                null,
                DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                false,
                false,
                BoxelCompletionMode.EnterSystem,
                []),
            cancellationToken: cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(1, result.Processed);
        Assert.Single(result.Entries);
        Assert.Single(resolver.Requests);
    }

    [Fact]
    public async Task AuditRetainsLocalResultWhenSpanshFails()
    {
        var boxel = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        await WriteLocalSystemAsync(
            boxel,
            DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
        var auditor = new BoxelCompletionAuditor(
            new LegacySystemDataReader(temporaryDirectory),
            new StubResolver(_ => throw new HttpRequestException("offline")));

        var result = await auditor.AuditAsync(new BoxelCompletionAuditRequest(
            "F123",
            [boxel],
            new HashSet<string>(StringComparer.Ordinal),
            null,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            []));

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.IsComplete);
        Assert.Single(result.Errors);
        Assert.Contains("offline", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditContinuesAfterInvalidSpanshResponse()
    {
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        var invalidBoxel = top.Children[0];
        var validBoxel = top.Children[1];
        var auditor = new BoxelCompletionAuditor(
            new LegacySystemDataReader(temporaryDirectory),
            new StubResolver(boxel =>
                boxel.Prefix == invalidBoxel.Prefix
                    ? throw new InvalidDataException("response exceeded the limit")
                    : [Observation(boxel.WithSystemNumber(2))]));

        var result = await auditor.AuditAsync(new BoxelCompletionAuditRequest(
            "F123",
            [invalidBoxel, validBoxel],
            new HashSet<string>(StringComparer.Ordinal),
            null,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            false,
            BoxelCompletionMode.EnterSystem,
            []));

        Assert.Equal(2, result.Processed);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(3, Assert.Single(result.Entries, entry =>
            entry.Boxel.Prefix == validBoxel.Prefix).SystemCount);
        Assert.Contains(
            result.Errors,
            error => error.Contains("response exceeded the limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FssAuditRequiresAllBodiesAfterSearchStart()
    {
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        var beforeStart = top.Children[0];
        var afterStart = top.Children[1];
        await WriteLocalSystemAsync(
            beforeStart,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            true);
        await WriteLocalSystemAsync(
            afterStart,
            DateTimeOffset.Parse("2026-07-20T00:00:00Z"),
            true);
        var auditor = new BoxelCompletionAuditor(
            new LegacySystemDataReader(temporaryDirectory),
            new StubResolver(_ => []));

        var result = await auditor.AuditAsync(new BoxelCompletionAuditRequest(
            "F123",
            [beforeStart, afterStart],
            new HashSet<string>(StringComparer.Ordinal),
            null,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            true,
            false,
            BoxelCompletionMode.FssAllBodies,
            []));

        Assert.False(result.Entries[0].IsComplete);
        Assert.True(result.Entries[1].IsComplete);
    }

    [Fact]
    public async Task FssAuditStillAppliesTheOlderSpanshBodyRule()
    {
        var boxel = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var auditor = new BoxelCompletionAuditor(
            new LegacySystemDataReader(temporaryDirectory),
            new StubResolver(_ =>
            [
                Observation(
                    boxel,
                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    hasKnownBodies: true),
            ]));

        var result = await auditor.AuditAsync(new BoxelCompletionAuditRequest(
            "F123",
            [boxel],
            new HashSet<string>(StringComparer.Ordinal),
            null,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            true,
            BoxelCompletionMode.FssAllBodies,
            []));

        Assert.True(Assert.Single(result.Entries).IsComplete);
    }

    private async Task WriteLocalSystemAsync(
        BoxelAddress boxel,
        DateTimeOffset visitedAt,
        bool fssAllBodies = false)
    {
        var directory = Path.Combine(temporaryDirectory, "systems", "F123");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, boxel.GeneratedName + ".json"),
            $$"""
            {
              "name": "{{boxel.GeneratedName}}",
              "address": 0,
              "lastVisited": "{{visitedAt:O}}",
              "fssAllBodies": {{fssAllBodies.ToString().ToLowerInvariant()}}
            }
            """);
    }

    private static BoxelSystemObservation Observation(
        BoxelAddress boxel,
        DateTimeOffset? spanshUpdated = null,
        bool hasKnownBodies = false)
    {
        return new BoxelSystemObservation(
            boxel,
            null,
            null,
            spanshUpdated,
            hasKnownBodies);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class StubResolver(
        Func<BoxelAddress, IReadOnlyList<BoxelSystemObservation>> resolve)
        : IBoxelSystemResolver
    {
        public List<BoxelAddress> Requests { get; } = [];

        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(boxel);
            return Task.FromResult(resolve(boxel));
        }
    }
}
