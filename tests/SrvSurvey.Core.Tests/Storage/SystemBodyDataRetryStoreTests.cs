using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class SystemBodyDataRetryStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SystemBodyDataRetry-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StateRoundTripsAcrossStoreInstancesAndIdCasing()
    {
        var visitedAt = DateTimeOffset.Parse("2026-07-24T10:00:02Z");
        var retryAt = DateTimeOffset.Parse("2026-07-24T10:01:02Z");
        var expected = new SystemBodyDataRetryState(
            "F123",
            42,
            visitedAt,
            AttemptCount: 2,
            retryAt,
            StandardDataComplete: false,
            BiologicalDataComplete: false);
        var first = new SystemBodyDataRetryStore(temporaryDirectory);

        await first.SaveAsync(expected);
        var second = new SystemBodyDataRetryStore(temporaryDirectory);
        var restored = await second.LoadAsync("f123");

        Assert.Equal(expected, restored);
    }

    [Fact]
    public async Task DifferentCommanderHasIndependentState()
    {
        var store = new SystemBodyDataRetryStore(temporaryDirectory);
        await store.SaveAsync(new SystemBodyDataRetryState(
            "F123",
            42,
            DateTimeOffset.Parse("2026-07-24T10:00:02Z"),
            AttemptCount: 4,
            null,
            StandardDataComplete: false,
            BiologicalDataComplete: false));

        Assert.Null(await store.LoadAsync("F456"));
    }

    [Fact]
    public async Task MalformedStateIsRejectedWithoutBeingOverwritten()
    {
        var store = new SystemBodyDataRetryStore(temporaryDirectory);
        await store.SaveAsync(new SystemBodyDataRetryState(
            "F123",
            42,
            DateTimeOffset.Parse("2026-07-24T10:00:02Z"),
            AttemptCount: 1,
            DateTimeOffset.Parse("2026-07-24T10:00:32Z"),
            StandardDataComplete: false,
            BiologicalDataComplete: false));
        var path = Assert.Single(Directory.GetFiles(
            temporaryDirectory,
            "*.json",
            SearchOption.AllDirectories));
        await File.WriteAllTextAsync(path, "{");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync("F123"));
        Assert.Equal("{", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
