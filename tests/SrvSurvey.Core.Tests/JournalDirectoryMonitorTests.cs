using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests;

public sealed class JournalDirectoryMonitorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-monitor-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task PollReadsAppendsPartialWritesStatusAndRotation()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var firstJournal = Path.Combine(
            temporaryDirectory,
            "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\"}\n");
        File.SetLastWriteTimeUtc(
            firstJournal,
            new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        var statusPath = Path.Combine(temporaryDirectory, StatusFileReader.FileName);
        await File.WriteAllTextAsync(
            statusPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Status\",\"Flags\":67108864,\"Flags2\":0}");
        var navRoutePath = Path.Combine(
            temporaryDirectory,
            NavRouteFileReader.FileName);
        await File.WriteAllTextAsync(
            navRoutePath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"NavRoute\","
                + "\"Route\":[{\"StarSystem\":\"Praea Euq IL-P c5-2\","
                + "\"SystemAddress\":102,\"StarPos\":[1,2,3],\"StarClass\":\"M\"}]}");
        var cargoPath = Path.Combine(
            temporaryDirectory,
            CargoFileReader.FileName);
        await File.WriteAllTextAsync(
            cargoPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Cargo\","
                + "\"Vessel\":\"SRV\",\"Count\":1,\"Inventory\":[{\"Name\":"
                + "\"ancientorb\",\"Count\":1,\"Stolen\":0}]}");
        var marketPath = Path.Combine(
            temporaryDirectory,
            MarketFileReader.FileName);
        await File.WriteAllTextAsync(
            marketPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Market\","
                + "\"MarketId\":3700123456,\"StationName\":\"Raven's Rest\","
                + "\"StationType\":\"FleetCarrier\",\"StarSystem\":\"Facece\","
                + "\"Items\":[{\"Name\":\"$Steel_Name;\",\"Stock\":125}]}");
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);

        var initial = await monitor.PollAsync();

        Assert.Equal("Commander", Assert.Single(initial.JournalEvents).EventName);
        Assert.NotNull(initial.Status);
        Assert.True(initial.Status.InSrv);
        Assert.NotNull(initial.NavRoute);
        Assert.Equal(
            "Praea Euq IL-P c5-2",
            Assert.Single(initial.NavRoute.Route).StarSystem);
        Assert.Equal(1, initial.Cargo?.GetCount("ancientorb"));
        Assert.Equal(125, initial.Market?.FindItem("steel")?.Stock);
        Assert.Empty(initial.Errors);
        Assert.True(initial.IsBootstrapRead);

        await File.AppendAllTextAsync(
            firstJournal,
            "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Future");
        var partial = await monitor.PollAsync();
        Assert.Empty(partial.JournalEvents);
        Assert.Null(partial.NavRoute);
        Assert.Empty(partial.Errors);

        await File.AppendAllTextAsync(firstJournal, "Event\",\"Value\":42}\n");
        var completed = await monitor.PollAsync();
        Assert.Equal("FutureEvent", Assert.Single(completed.JournalEvents).EventName);
        Assert.Null(completed.Cargo);
        Assert.Null(completed.Market);
        Assert.Empty(completed.Errors);

        await File.WriteAllTextAsync(
            cargoPath,
            "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"Cargo\","
                + "\"Vessel\":\"SRV\",\"Count\":2,\"Inventory\":[{\"Name\":"
                + "\"ancientorb\",\"Count\":2,\"Stolen\":0}]}");
        var cargoChanged = await monitor.PollAsync();
        Assert.Equal(2, cargoChanged.Cargo?.GetCount("ancientorb"));
        Assert.Equal(2, monitor.CurrentCargo?.Count);

        await File.WriteAllTextAsync(
            marketPath,
            "{\"timestamp\":\"2026-07-24T10:00:03Z\",\"event\":\"Market\","
                + "\"MarketId\":3700123456,\"StationName\":\"Raven's Rest\","
                + "\"StationType\":\"FleetCarrier\",\"StarSystem\":\"Facece\","
                + "\"Items\":[{\"Name\":\"$Steel_Name;\",\"Stock\":200}]}");
        var marketChanged = await monitor.PollAsync();
        Assert.Equal(200, marketChanged.Market?.FindItem("steel")?.Stock);
        Assert.Equal(200, monitor.CurrentMarket?.FindItem("steel")?.Stock);

        await File.WriteAllTextAsync(
            navRoutePath,
            "{\"timestamp\":\"2026-07-24T10:01:00Z\",\"event\":\"NavRouteClear\",\"Route\":[]}");
        var routeCleared = await monitor.PollAsync();
        Assert.Equal("NavRouteClear", routeCleared.NavRoute?.EventName);
        Assert.Empty(Assert.IsType<NavRouteSnapshot>(routeCleared.NavRoute).Route);

        var secondJournal = Path.Combine(
            temporaryDirectory,
            "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            secondJournal,
            "{\"timestamp\":\"2026-07-24T11:00:00Z\",\"event\":\"Fileheader\"}\n");
        File.SetLastWriteTimeUtc(
            secondJournal,
            new DateTime(2030, 7, 24, 11, 0, 0, DateTimeKind.Utc));

        var rotated = await monitor.PollAsync();

        Assert.Equal(secondJournal, rotated.JournalPath);
        Assert.Equal("Fileheader", Assert.Single(rotated.JournalEvents).EventName);
        Assert.False(rotated.IsBootstrapRead);
    }

    [Fact]
    public async Task RunAsyncStopsWhenCancelled()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => monitor.RunAsync(TimeSpan.FromMilliseconds(1), cancellation.Token));
    }

    [Fact]
    public async Task PollSelectsNewestJournalForRequestedCommander()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var requestedJournal = Path.Combine(
            temporaryDirectory,
            "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(
            requestedJournal,
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
        File.SetLastWriteTimeUtc(
            requestedJournal,
            new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        var otherJournal = Path.Combine(
            temporaryDirectory,
            "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            otherJournal,
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Other\",\"FID\":\"F999\"}\n");
        File.SetLastWriteTimeUtc(
            otherJournal,
            new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(
            temporaryDirectory,
            "f123");

        var update = await monitor.PollAsync();

        Assert.Equal(requestedJournal, update.JournalPath);
        Assert.Equal(
            ["Fileheader", "Commander"],
            update.JournalEvents.Select(entry => entry.EventName));
    }

    [Fact]
    public async Task PollMovesToNewRequestedCommanderJournalAfterIdentityArrives()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var firstJournal = Path.Combine(
            temporaryDirectory,
            "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
        File.SetLastWriteTimeUtc(
            firstJournal,
            new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F123");
        _ = await monitor.PollAsync();
        var nextJournal = Path.Combine(
            temporaryDirectory,
            "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            nextJournal,
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n");
        File.SetLastWriteTimeUtc(
            nextJournal,
            new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));

        var beforeIdentity = await monitor.PollAsync();
        await File.AppendAllTextAsync(
            nextJournal,
            "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
        var afterIdentity = await monitor.PollAsync();

        Assert.Equal(firstJournal, beforeIdentity.JournalPath);
        Assert.Empty(beforeIdentity.JournalEvents);
        Assert.Equal(nextJournal, afterIdentity.JournalPath);
        Assert.Equal(
            ["Fileheader", "Commander"],
            afterIdentity.JournalEvents.Select(entry => entry.EventName));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
