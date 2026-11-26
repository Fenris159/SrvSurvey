using System.Text;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests;

public sealed partial class JournalDirectoryMonitorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-monitor-tests-{Guid.NewGuid():N}"
    );

    /// <summary>Checks separate commander monitors stay on their Steam or Epic source and never replay alias history.</summary>
    [Fact]
    public async Task CancellationAfterJournalConsumptionReturnsTheBatchForDurableCapture()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string journal = Path.Combine(temporaryDirectory, "Journal.2026-10-20T120000.01.log");
        await File.WriteAllTextAsync(journal, "{\"event\":\"Commander\",\"Name\":\"Test\",\"FID\":\"F123\"}\n");
        using var cancellation = new CancellationTokenSource();
        bool stopDuringCompanions = false;
        var monitor = new JournalDirectoryMonitor(
            temporaryDirectory,
            "F123",
            _ =>
            {
                if (stopDuringCompanions)
                {
                    cancellation.Cancel();
                }
                return default;
            }
        );
        await monitor.PollAsync();
        await File.AppendAllTextAsync(
            journal,
            "{\"event\":\"MarketBuy\",\"MarketID\":42,\"Type\":\"steel\",\"Count\":5}\n"
        );
        stopDuringCompanions = true;

        JournalMonitorUpdate consumed = await monitor.PollAsync(cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal("MarketBuy", Assert.Single(consumed.JournalEvents).EventName);
        Assert.False(consumed.IsBootstrapRead);
        Assert.Empty((await monitor.PollAsync()).JournalEvents);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.PollAsync(cancellation.Token));
    }

    [Fact]
    public async Task CommanderInstancesKeepDistinctPhysicalSourcesWithoutReplayingApiEvents()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string steam = Path.Combine(temporaryDirectory, "steam");
        string epic = Path.Combine(temporaryDirectory, "epic");
        string steamAlias = Path.Combine(temporaryDirectory, "z-steam-alias");
        string epicAlias = Path.Combine(temporaryDirectory, "z-epic-alias");
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(epic);
        string steamJournal = Path.Combine(steam, "Journal.2026-10-03T090000.01.log");
        string epicJournal = Path.Combine(epic, "Journal.2026-10-03T100000.01.log");
        await File.WriteAllTextAsync(steamJournal, "{\"event\":\"Commander\",\"Name\":\"Steam\",\"FID\":\"F123\"}\n");
        await File.WriteAllTextAsync(epicJournal, "{\"event\":\"Commander\",\"Name\":\"Epic\",\"FID\":\"F456\"}\n");
        string[] sources = [steam, epic, steamAlias, epicAlias];
        var steamMonitor = new JournalDirectoryMonitor(sources, "F123");
        var epicMonitor = new JournalDirectoryMonitor(sources, "F456");
        Assert.Equal(steamJournal, (await steamMonitor.PollAsync()).JournalPath);
        Assert.Equal(epicJournal, (await epicMonitor.PollAsync()).JournalPath);

        const string steamCommand = "{\"event\":\"SendText\",\"Message\":\".mining survey\"}\n";
        const string epicCommand = "{\"event\":\"SendText\",\"Message\":\".mine rigs 3\"}\n";
        await File.AppendAllTextAsync(
            steamJournal,
            steamCommand + "{\"event\":\"MarketBuy\",\"MarketID\":1,\"Type\":\"gold\",\"Count\":2}\n"
        );
        await File.AppendAllTextAsync(
            epicJournal,
            epicCommand + "{\"event\":\"ColonisationContribution\",\"MarketID\":2,\"Contributions\":[]}\n"
        );
        JournalMonitorUpdate steamLive = await steamMonitor.PollAsync();
        JournalMonitorUpdate epicLive = await epicMonitor.PollAsync();
        Assert.Equal(["SendText", "MarketBuy"], steamLive.JournalEvents.Select(entry => entry.EventName));
        Assert.Equal(["SendText", "ColonisationContribution"], epicLive.JournalEvents.Select(entry => entry.EventName));

        Directory.CreateSymbolicLink(steamAlias, steam);
        Directory.CreateSymbolicLink(epicAlias, epic);
        Assert.Empty((await steamMonitor.PollAsync()).JournalEvents);
        Assert.Empty((await epicMonitor.PollAsync()).JournalEvents);
        Assert.Equal(steamJournal, steamMonitor.CurrentJournalPath);
        Assert.Equal(epicJournal, epicMonitor.CurrentJournalPath);

        await File.AppendAllTextAsync(steamJournal, steamCommand);
        await File.AppendAllTextAsync(epicJournal, epicCommand);
        JournalEventEnvelope nextSteam = Assert.Single((await steamMonitor.PollAsync()).JournalEvents);
        JournalEventEnvelope nextEpic = Assert.Single((await epicMonitor.PollAsync()).JournalEvents);
        Assert.Equal(".mining survey", nextSteam.Payload.GetProperty("Message").GetString());
        Assert.Equal(".mine rigs 3", nextEpic.Payload.GetProperty("Message").GetString());
        Assert.Empty((await steamMonitor.PollAsync()).JournalEvents);
        Assert.Empty((await epicMonitor.PollAsync()).JournalEvents);
    }

    /// <summary>Checks a Steam folder alias cannot replay a command and restart a completed survey guide.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SteamFolderAliasDoesNotRestartCompletedSurveyGuide(bool linkJournalFolder)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string realRoot = Path.Combine(temporaryDirectory, "steam-real");
        string aliasRoot = Path.Combine(temporaryDirectory, "z-steam-alias");
        const string relativeDirectory = "prefix/Saved Games/Elite Dangerous";
        string realDirectory = Path.Combine(realRoot, relativeDirectory);
        string aliasDirectory = Path.Combine(aliasRoot, relativeDirectory);
        Directory.CreateDirectory(realDirectory);
        string journal = Path.Combine(realDirectory, "Journal.2026-10-03T090000.01.log");
        await File.WriteAllTextAsync(journal, "{\"event\":\"Commander\",\"Name\":\"Probe\",\"FID\":\"F123\"}\n");
        var monitor = new JournalDirectoryMonitor([realDirectory, aliasDirectory], "F123");
        await monitor.PollAsync();
        using var service = new MineMapService(temporaryDirectory);
        var context = new MineMapCommandContext(
            "F123",
            "Probe",
            "Test",
            1,
            new GalacticCoordinate(1, 2, 3),
            1,
            "Test 1",
            "Rocky body",
            1,
            740136,
            new SurfaceCoordinate(10, 20)
        );
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 1", context)).Succeeded);
        const string command =
            "{\"timestamp\":\"2026-10-03T14:05:00Z\",\"event\":\"SendText\",\"Message\":\".mining survey\"}\n";
        await File.AppendAllTextAsync(journal, command);
        JournalMonitorUpdate live = await monitor.PollAsync();
        Assert.Single(await service.ApplyJournalEventsAsync(live.JournalEvents, context, !live.IsBootstrapRead));
        Assert.True((await service.ExecuteAsync(".mining survey complete", context)).Succeeded);
        MineMapSurveyGuideState completed = Assert.IsType<MineMapSurveyGuideState>(service.SurveyGuide);
        Assert.Equal(MineMapSurveyGuidePhase.Complete, completed.Phase);

        if (linkJournalFolder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(aliasDirectory)!);
            Directory.CreateSymbolicLink(aliasDirectory, realDirectory);
        }
        else
        {
            Directory.CreateSymbolicLink(aliasRoot, realRoot);
        }
        JournalMonitorUpdate reread = await monitor.PollAsync();
        await service.ApplyJournalEventsAsync(reread.JournalEvents, context, !reread.IsBootstrapRead);

        Assert.Equal(completed, service.SurveyGuide);
        Assert.DoesNotContain(reread.JournalEvents, entry => entry.EventName == "SendText");
        Assert.Equal(journal, monitor.CurrentJournalPath);

        await File.AppendAllTextAsync(journal, command);
        JournalMonitorUpdate next = await monitor.PollAsync();
        Assert.Single(next.JournalEvents, entry => entry.EventName == "SendText");
        Assert.Single(await service.ApplyJournalEventsAsync(next.JournalEvents, context, !next.IsBootstrapRead));
        Assert.Equal(MineMapSurveyGuidePhase.Waypoint, service.SurveyGuide.Phase);
        Assert.Empty((await monitor.PollAsync()).JournalEvents);
    }

    /// <summary>Checks returning to an already read journal cannot restart a completed deposit trace.</summary>
    [Fact]
    public async Task RereadingOldJournalDoesNotRestartCompletedSplatTrace()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string firstJournal = Path.Combine(temporaryDirectory, "Journal.2026-10-03T090000.01.log");
        const string identity = "{\"event\":\"Commander\",\"Name\":\"Probe\",\"FID\":\"F123\"}\n";
        await File.WriteAllTextAsync(firstJournal, identity);
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F123");
        await monitor.PollAsync();
        using var service = new MineMapService(temporaryDirectory);
        var context = new MineMapCommandContext(
            "F123",
            "Probe",
            "Test",
            1,
            new GalacticCoordinate(1, 2, 3),
            1,
            "Test 1",
            "Rocky body",
            1,
            740136,
            new SurfaceCoordinate(10, 20)
        );
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 1", context)).Succeeded);
        SurfaceCoordinate deposit = service.ActiveSurvey!.Center;
        Assert.True(
            (
                await service.ExecuteAsync(".mine ruby high/high here", context with { PlayerLocation = deposit })
            ).Succeeded
        );
        context = context with
        {
            PlayerLocation = MineMapService.GetDestination(deposit, 0, 100, context.PlanetRadiusMeters),
        };
        await File.AppendAllTextAsync(
            firstJournal,
            "{\"timestamp\":\"2026-10-03T14:05:00Z\",\"event\":\"SendText\",\"Message\":\".mine splat\"}\n"
        );
        JournalMonitorUpdate live = await monitor.PollAsync();
        Assert.Single(live.JournalEvents);
        await service.ApplyJournalEventsAsync(live.JournalEvents, context, !live.IsBootstrapRead);
        for (int bearing = 30; bearing <= 360; bearing += 30)
        {
            service.UpdateContext(
                context with
                {
                    PlayerLocation = MineMapService.GetDestination(
                        deposit,
                        bearing % 360,
                        100,
                        context.PlanetRadiusMeters
                    ),
                }
            );
        }
        MineMapMarker completed = Assert.Single(service.ActiveSurvey.Markers);
        Assert.False(completed.IsSplatTraceActive);
        Assert.True(completed.SplatBoundary.Count > 1);

        string nextJournal = Path.Combine(temporaryDirectory, "Journal.2026-10-03T100000.01.log");
        await File.WriteAllTextAsync(nextJournal, identity);
        File.SetLastWriteTimeUtc(nextJournal, DateTime.UtcNow.AddMinutes(1));
        await monitor.PollAsync();
        File.SetLastWriteTimeUtc(firstJournal, DateTime.UtcNow.AddMinutes(2));
        JournalMonitorUpdate reread = await monitor.PollAsync();
        await service.ApplyJournalEventsAsync(reread.JournalEvents, context, !reread.IsBootstrapRead);

        MineMapMarker after = Assert.Single(service.ActiveSurvey.Markers);
        Assert.False(after.IsSplatTraceActive);
        Assert.Equal(completed.SplatBoundary, after.SplatBoundary);
        Assert.Equal(completed.SuggestedRigLocations, after.SuggestedRigLocations);
    }

    /// <summary>Checks rereads suppress old commands while identical newly appended commands remain distinct.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RereadSkipsOldTextEventsAndAllowsNewIdenticalText(bool truncate)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string journal = Path.Combine(temporaryDirectory, "Journal.2026-10-03T090000.01.log");
        const string identity = "{\"event\":\"Commander\",\"Name\":\"Probe\",\"FID\":\"F123\"}\n";
        const string command =
            "{\"timestamp\":\"2026-10-03T14:05:00Z\",\"event\":\"SendText\",\"Message\":\".alignment\"}\n";
        await File.WriteAllTextAsync(journal, identity + command + "{\"event\":\"Music\"}\n");
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F123");
        Assert.Single((await monitor.PollAsync()).JournalEvents, entry => entry.EventName == "SendText");

        if (truncate)
        {
            await File.WriteAllTextAsync(journal, identity + command);
        }
        else
        {
            string next = Path.Combine(temporaryDirectory, "Journal.2026-10-03T100000.01.log");
            await File.WriteAllTextAsync(next, identity);
            File.SetLastWriteTimeUtc(next, DateTime.UtcNow.AddMinutes(1));
            await monitor.PollAsync();
            File.SetLastWriteTimeUtc(journal, DateTime.UtcNow.AddMinutes(2));
        }
        JournalMonitorUpdate reread = await monitor.PollAsync();
        Assert.DoesNotContain(reread.JournalEvents, entry => entry.EventName == "SendText");
        Assert.Contains(reread.JournalEvents, entry => entry.EventName == "Commander");

        await File.AppendAllTextAsync(journal, command);
        File.SetLastWriteTimeUtc(journal, DateTime.UtcNow.AddMinutes(3));
        Assert.Single((await monitor.PollAsync()).JournalEvents, entry => entry.EventName == "SendText");
        Assert.Empty((await monitor.PollAsync()).JournalEvents);
    }

    /// <summary>Checks partial UTF-8 writes and a pending CRLF line retain the same identity on reread.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialTextLineKeepsStableSourceIdentity(bool flushBeforeNewline)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string journal = Path.Combine(temporaryDirectory, "Journal.2026-10-03T090000.01.log");
        await File.WriteAllTextAsync(journal, "{\"event\":\"Commander\",\"Name\":\"Pröbe\",\"FID\":\"F123\"}\n");
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F123");
        await monitor.PollAsync();
        byte[] command = Encoding.UTF8.GetBytes(
            "{\"event\":\"SendText\",\"Message\":\".alignment\",\"Note\":\"é\"}\r\n"
        );
        int split = Array.IndexOf(command, (byte)0xc3) + 1;
        await using (var writer = new FileStream(journal, FileMode.Append))
        {
            await writer.WriteAsync(command.AsMemory(0, split));
        }
        Assert.Empty((await monitor.PollAsync()).JournalEvents);
        await using (var writer = new FileStream(journal, FileMode.Append))
        {
            await writer.WriteAsync(command.AsMemory(split, command.Length - split - (flushBeforeNewline ? 1 : 0)));
        }

        string next = Path.Combine(temporaryDirectory, "Journal.2026-10-03T100000.01.log");
        if (flushBeforeNewline)
        {
            Assert.Empty((await monitor.PollAsync()).JournalEvents);
            await File.WriteAllTextAsync(next, "{\"event\":\"Commander\",\"FID\":\"F123\"}\n");
            File.SetLastWriteTimeUtc(next, DateTime.UtcNow.AddMinutes(1));
        }
        Assert.Single((await monitor.PollAsync()).JournalEvents, entry => entry.EventName == "SendText");
        if (!flushBeforeNewline)
        {
            await File.WriteAllTextAsync(next, "{\"event\":\"Commander\",\"FID\":\"F123\"}\n");
            File.SetLastWriteTimeUtc(next, DateTime.UtcNow.AddMinutes(1));
            await monitor.PollAsync();
        }
        else
        {
            await File.AppendAllTextAsync(journal, "\n");
        }
        File.SetLastWriteTimeUtc(journal, DateTime.UtcNow.AddMinutes(2));
        Assert.DoesNotContain((await monitor.PollAsync()).JournalEvents, entry => entry.EventName == "SendText");
    }

    /// <summary>Checks identical chat text in a new journal represents a separate command.</summary>
    [Fact]
    public async Task NewJournalDoesNotDiscardIdenticalTextEvent()
    {
        Directory.CreateDirectory(temporaryDirectory);
        const string contents = "{\"event\":\"SendText\",\"Message\":\".alignment\"}\n";
        string first = Path.Combine(temporaryDirectory, "Journal.2026-10-03T090000.01.log");
        await File.WriteAllTextAsync(first, contents);
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);
        Assert.Single((await monitor.PollAsync()).JournalEvents);
        string next = Path.Combine(temporaryDirectory, "Journal.2026-10-03T100000.01.log");
        await File.WriteAllTextAsync(next, contents);
        File.SetLastWriteTimeUtc(next, DateTime.UtcNow.AddMinutes(1));

        Assert.Single((await monitor.PollAsync()).JournalEvents);
    }

    /// <summary>Checks a replacement line at an old offset can still contain a genuinely different command.</summary>
    [Fact]
    public async Task ReplacingSourceLineAllowsChangedTextEvent()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string journal = Path.Combine(temporaryDirectory, "Journal.2026-10-03T090000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"event\":\"SendText\",\"Message\":\".mine rigs 1\"}\n{\"event\":\"Music\"}\n"
        );
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);
        await monitor.PollAsync();
        await File.WriteAllTextAsync(journal, "{\"event\":\"SendText\",\"Message\":\".mine rigs 2\"}\n");

        JournalEventEnvelope changed = Assert.Single((await monitor.PollAsync()).JournalEvents);
        Assert.Equal(".mine rigs 2", changed.Payload.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task PollReadsAppendsPartialWritesStatusAndRotation()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string firstJournal = Path.Combine(temporaryDirectory, "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\"}\n"
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        string statusPath = Path.Combine(temporaryDirectory, StatusFileReader.FileName);
        await File.WriteAllTextAsync(
            statusPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Status\",\"Flags\":67108864,\"Flags2\":0}"
        );
        string navRoutePath = Path.Combine(temporaryDirectory, NavRouteFileReader.FileName);
        await File.WriteAllTextAsync(
            navRoutePath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"NavRoute\","
                + "\"Route\":[{\"StarSystem\":\"Praea Euq IL-P c5-2\","
                + "\"SystemAddress\":102,\"StarPos\":[1,2,3],\"StarClass\":\"M\"}]}"
        );
        string cargoPath = Path.Combine(temporaryDirectory, CargoFileReader.FileName);
        await File.WriteAllTextAsync(
            cargoPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Cargo\","
                + "\"Vessel\":\"SRV\",\"Count\":1,\"Inventory\":[{\"Name\":"
                + "\"ancientorb\",\"Count\":1,\"Stolen\":0}]}"
        );
        string shipLockerPath = Path.Combine(temporaryDirectory, ShipLockerFileReader.FileName);
        await File.WriteAllTextAsync(
            shipLockerPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"ShipLocker\","
                + "\"Items\":[{\"Name\":\"healthmonitor\",\"Count\":2}],"
                + "\"Components\":[],\"Consumables\":[],\"Data\":[]}"
        );
        string marketPath = Path.Combine(temporaryDirectory, MarketFileReader.FileName);
        await File.WriteAllTextAsync(
            marketPath,
            "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Market\","
                + "\"MarketId\":3700123456,\"StationName\":\"Raven's Rest\","
                + "\"StationType\":\"FleetCarrier\",\"StarSystem\":\"Facece\","
                + "\"Items\":[{\"Name\":\"$Steel_Name;\",\"Stock\":125}]}"
        );
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);

        JournalMonitorUpdate initial = await monitor.PollAsync();

        Assert.Equal("Commander", Assert.Single(initial.JournalEvents).EventName);
        Assert.NotNull(initial.Status);
        Assert.True(initial.Status.InSrv);
        Assert.NotNull(initial.NavRoute);
        Assert.Equal("Praea Euq IL-P c5-2", Assert.Single(initial.NavRoute.Route).StarSystem);
        Assert.Equal(1, initial.Cargo?.GetCount("ancientorb"));
        Assert.Equal(2, Assert.Single(initial.ShipLocker!.Items).Count);
        Assert.Equal(125, initial.Market?.FindItem("steel")?.Stock);
        Assert.Empty(initial.Errors);
        Assert.True(initial.IsBootstrapRead);
        Assert.True(initial.HasChanges);

        JournalMonitorUpdate unchanged;
        await using (
            var unchangedMarketLock = new FileStream(marketPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
        )
        {
            unchanged = await monitor.PollAsync();
        }
        Assert.False(unchanged.HasChanges);
        Assert.Empty(unchanged.JournalEvents);
        Assert.Null(unchanged.Status);
        Assert.Null(unchanged.NavRoute);
        Assert.Null(unchanged.Cargo);
        Assert.Null(unchanged.ShipLocker);
        Assert.Null(unchanged.Market);

        await File.AppendAllTextAsync(firstJournal, "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Future");
        JournalMonitorUpdate partial = await monitor.PollAsync();
        Assert.False(partial.HasChanges);
        Assert.Empty(partial.JournalEvents);
        Assert.Null(partial.NavRoute);
        Assert.Empty(partial.Errors);

        await File.AppendAllTextAsync(firstJournal, "Event\",\"Value\":42}\n");
        JournalMonitorUpdate completed = await monitor.PollAsync();
        Assert.Equal("FutureEvent", Assert.Single(completed.JournalEvents).EventName);
        Assert.Null(completed.Cargo);
        Assert.Null(completed.ShipLocker);
        Assert.Null(completed.Market);
        Assert.Empty(completed.Errors);

        await File.WriteAllTextAsync(
            cargoPath,
            "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"Cargo\","
                + "\"Vessel\":\"SRV\",\"Count\":2,\"Inventory\":[{\"Name\":"
                + "\"ancientorb\",\"Count\":2,\"Stolen\":0}]}"
        );
        JournalMonitorUpdate cargoChanged = await monitor.PollAsync();
        Assert.Equal(2, cargoChanged.Cargo?.GetCount("ancientorb"));
        Assert.Equal(2, monitor.CurrentCargo?.Count);

        await File.WriteAllTextAsync(
            shipLockerPath,
            "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"ShipLocker\","
                + "\"Items\":[{\"Name\":\"healthmonitor\",\"Count\":5}],"
                + "\"Components\":[],\"Consumables\":[],\"Data\":[]}"
        );
        JournalMonitorUpdate shipLockerChanged = await monitor.PollAsync();
        Assert.Equal(5, Assert.Single(shipLockerChanged.ShipLocker!.Items).Count);
        Assert.Equal(5, Assert.Single(monitor.CurrentShipLocker!.Items).Count);

        await File.WriteAllTextAsync(
            marketPath,
            "{\"timestamp\":\"2026-07-24T10:00:03Z\",\"event\":\"Market\","
                + "\"MarketId\":3700123456,\"StationName\":\"Raven's Rest\","
                + "\"StationType\":\"FleetCarrier\",\"StarSystem\":\"Facece\","
                + "\"Items\":[{\"Name\":\"$Steel_Name;\",\"Stock\":200}]}"
        );
        JournalMonitorUpdate marketChanged = await monitor.PollAsync();
        Assert.Equal(200, marketChanged.Market?.FindItem("steel")?.Stock);
        Assert.Equal(200, monitor.CurrentMarket?.FindItem("steel")?.Stock);

        await File.WriteAllTextAsync(
            navRoutePath,
            "{\"timestamp\":\"2026-07-24T10:01:00Z\",\"event\":\"NavRouteClear\",\"Route\":[]}"
        );
        JournalMonitorUpdate routeCleared = await monitor.PollAsync();
        Assert.Equal("NavRouteClear", routeCleared.NavRoute?.EventName);
        Assert.Empty(Assert.IsType<NavRouteSnapshot>(routeCleared.NavRoute).Route);

        string secondJournal = Path.Combine(temporaryDirectory, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            secondJournal,
            "{\"timestamp\":\"2026-07-24T11:00:00Z\",\"event\":\"Fileheader\"}\n"
        );
        File.SetLastWriteTimeUtc(secondJournal, new DateTime(2030, 7, 24, 11, 0, 0, DateTimeKind.Utc));

        JournalMonitorUpdate rotated = await monitor.PollAsync();

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
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            monitor.RunAsync(TimeSpan.FromMilliseconds(1), cancellation.Token)
        );
    }

    [Fact]
    public async Task PollDefersTransientStatusReadErrorAndSignalsRecovery()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string statusPath = Path.Combine(temporaryDirectory, StatusFileReader.FileName);
        await File.WriteAllTextAsync(statusPath, "{\"event\":\"Status\",\"Flags\":1}");
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);
        JournalMonitorUpdate initial = await monitor.PollAsync();

        await File.WriteAllTextAsync(statusPath, string.Empty);
        JournalMonitorUpdate transientFailure = await monitor.PollAsync();
        JournalMonitorUpdate persistentFailure = await monitor.PollAsync();
        JournalMonitorUpdate repeatedFailure = await monitor.PollAsync();

        Assert.False(transientFailure.HasChanges);
        Assert.Empty(transientFailure.Errors);
        Assert.Null(transientFailure.Status);
        Assert.Same(initial.Status, monitor.CurrentStatus);
        Assert.Contains("after 3 attempts", Assert.Single(persistentFailure.Errors));
        Assert.False(persistentFailure.StatusReadErrorRecovered);
        Assert.Empty(repeatedFailure.Errors);
        Assert.False(repeatedFailure.HasChanges);
        Assert.Null(repeatedFailure.Status);
        Assert.False(repeatedFailure.StatusReadErrorRecovered);
        Assert.Same(initial.Status, monitor.CurrentStatus);

        await File.WriteAllTextAsync(statusPath, "{\"event\":\"Status\",\"Flags\":0,\"GuiFocus\":1}");
        JournalMonitorUpdate recovered = await monitor.PollAsync();

        Assert.Empty(recovered.Errors);
        Assert.NotNull(recovered.Status);
        Assert.Equal(GuiFocus.InternalPanel, recovered.Status.GuiFocus);
        Assert.True(recovered.StatusReadErrorRecovered);
        Assert.True(recovered.HasChanges);
    }

    [Fact]
    public async Task PollReportsCompanionStampFailuresOnceUntilRecovery()
    {
        Directory.CreateDirectory(temporaryDirectory);
        bool failStatusStamp = true;
        const string error = "Status.json metadata is unavailable.";
        var monitor = new JournalDirectoryMonitor(
            temporaryDirectory,
            targetFrontierId: null,
            path =>
                Path.GetFileName(path) == StatusFileReader.FileName && failStatusStamp
                    ? new JournalDirectoryMonitor.CompanionFileStampReadResult(Stamp: null, error)
                    : default
        );

        JournalMonitorUpdate failed = await monitor.PollAsync();
        JournalMonitorUpdate repeated = await monitor.PollAsync();
        failStatusStamp = false;
        JournalMonitorUpdate recovered = await monitor.PollAsync();
        failStatusStamp = true;
        JournalMonitorUpdate failedAgain = await monitor.PollAsync();

        Assert.Equal(error, Assert.Single(failed.Errors));
        Assert.Empty(repeated.Errors);
        Assert.Empty(recovered.Errors);
        Assert.Equal(error, Assert.Single(failedAgain.Errors));
    }

    [Fact]
    public async Task PollSelectsNewestJournalForRequestedCommander()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string requestedJournal = Path.Combine(temporaryDirectory, "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(
            requestedJournal,
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
        );
        File.SetLastWriteTimeUtc(requestedJournal, new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        string otherJournal = Path.Combine(temporaryDirectory, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            otherJournal,
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Other\",\"FID\":\"F999\"}\n"
        );
        File.SetLastWriteTimeUtc(otherJournal, new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "f123");

        JournalMonitorUpdate update = await monitor.PollAsync();

        Assert.Equal(requestedJournal, update.JournalPath);
        Assert.Equal(["Fileheader", "Commander"], update.JournalEvents.Select(entry => entry.EventName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticStartupRestoresLastCommanderThenFollowsNewestJournal(bool menuSessionClosed)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string previous = Path.Combine(temporaryDirectory, "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(
            previous,
            "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Drew\",\"FID\":\"F123\"}\n"
                + "{\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(previous, new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        string newest = Path.Combine(temporaryDirectory, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            newest,
            "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                + (menuSessionClosed ? "{\"event\":\"Shutdown\"}\n" : string.Empty)
        );
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);
        var state = new JournalSessionState();

        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        foreach (JournalEventEnvelope entry in bootstrap.JournalEvents)
        {
            state.Apply(entry);
        }

        Assert.Equal("Drew", state.CommanderName);
        Assert.Equal("F123", state.FrontierId);
        Assert.True(bootstrap.IsBootstrapRead);
        Assert.True(bootstrap.IsAwaitingCommanderIdentity);
        JournalMonitorUpdate current = await monitor.PollAsync();
        foreach (JournalEventEnvelope entry in current.JournalEvents)
        {
            state.Apply(entry);
        }

        Assert.Equal(newest, current.JournalPath);
        Assert.Equal("Drew", state.CommanderName);
        Assert.Equal(menuSessionClosed, state.IsShutdown);
        Assert.True(state.IsAtMainMenu || state.IsShutdown);
        Assert.False(current.IsBootstrapRead);
        Assert.Empty((await monitor.PollAsync()).JournalEvents);

        string loginJournal = menuSessionClosed
            ? Path.Combine(temporaryDirectory, "Journal.2026-07-24T120000.01.log")
            : newest;
        if (menuSessionClosed)
        {
            await File.WriteAllTextAsync(loginJournal, "{\"event\":\"Fileheader\",\"Odyssey\":true}\n");
        }
        await File.AppendAllTextAsync(
            loginJournal,
            "{\"event\":\"Commander\",\"Name\":\"Other\",\"FID\":\"F999\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Other\",\"FID\":\"F999\"}\n"
        );
        JournalMonitorUpdate login = await monitor.PollAsync();
        foreach (JournalEventEnvelope entry in login.JournalEvents)
        {
            state.Apply(entry);
        }

        Assert.Equal(loginJournal, login.JournalPath);
        Assert.Equal("Other", state.CommanderName);
        Assert.Equal("F999", state.FrontierId);
        Assert.False(state.IsShutdown);
        Assert.False(state.IsAtMainMenu);
        Assert.False(login.IsAwaitingCommanderIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticStartupUsesNewestWhenItHasIdentityOrNoPreviousIdentityExists(bool newestHasIdentity)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string previous = Path.Combine(temporaryDirectory, "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(previous, "{\"event\":\"Fileheader\"}\n{\"event\":\"Shutdown\"}\n");
        File.SetLastWriteTimeUtc(previous, new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        string newest = Path.Combine(temporaryDirectory, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(
            newest,
            "{\"event\":\"Fileheader\"}\n"
                + (newestHasIdentity ? "{\"event\":\"Commander\",\"Name\":\"Other\",\"FID\":\"F999\"}\n" : string.Empty)
        );
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);

        JournalMonitorUpdate bootstrap = await monitor.PollAsync();

        Assert.Equal(newest, bootstrap.JournalPath);
        Assert.Equal(newestHasIdentity ? 2 : 1, bootstrap.JournalEvents.Count);
        Assert.False(bootstrap.IsAwaitingCommanderIdentity);
    }

    [Fact]
    public async Task AutomaticStartupRetriesUnidentifiedJournalsAfterFirstPoll()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string previous = Path.Combine(temporaryDirectory, "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(previous, "{\"event\":\"Fileheader\"}\n");
        DateTime previousWriteTime = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(previous, previousWriteTime);
        string newest = Path.Combine(temporaryDirectory, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(newest, "{\"event\":\"Fileheader\"}\n");
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory);

        JournalMonitorUpdate initial = await monitor.PollAsync();
        Assert.Equal(newest, initial.JournalPath);

        await File.AppendAllTextAsync(previous, "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
        File.SetLastWriteTimeUtc(previous, previousWriteTime);

        JournalMonitorUpdate recovered = await monitor.PollAsync();
        Assert.Equal(previous, recovered.JournalPath);
        Assert.Contains(recovered.JournalEvents, entry => entry.EventName == "Commander");

        JournalMonitorUpdate current = await monitor.PollAsync();
        Assert.Equal(newest, current.JournalPath);
    }

    [Fact]
    public async Task PollMovesToNewRequestedCommanderJournalAfterIdentityArrives()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string firstJournal = Path.Combine(temporaryDirectory, "Journal.2026-07-24T100000.01.log");
        await File.WriteAllTextAsync(firstJournal, "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F123");
        _ = await monitor.PollAsync();
        string nextJournal = Path.Combine(temporaryDirectory, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(nextJournal, "{\"event\":\"Fileheader\",\"Odyssey\":true}\n");
        File.SetLastWriteTimeUtc(nextJournal, new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));

        JournalMonitorUpdate beforeIdentity = await monitor.PollAsync();
        await File.AppendAllTextAsync(nextJournal, "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
        JournalMonitorUpdate afterIdentity = await monitor.PollAsync();

        Assert.Equal(firstJournal, beforeIdentity.JournalPath);
        Assert.Empty(beforeIdentity.JournalEvents);
        Assert.True(beforeIdentity.IsAwaitingCommanderIdentity);
        Assert.True(beforeIdentity.SessionContextChanged);
        Assert.Equal(nextJournal, afterIdentity.JournalPath);
        Assert.False(afterIdentity.IsAwaitingCommanderIdentity);
        Assert.True(afterIdentity.SessionContextChanged);
        Assert.Equal(["Fileheader", "Commander"], afterIdentity.JournalEvents.Select(entry => entry.EventName));
    }

    [Fact]
    public async Task PollSelectsRequestedCommanderAcrossJournalDirectories()
    {
        string steam = Path.Combine(temporaryDirectory, "steam");
        string epic = Path.Combine(temporaryDirectory, "epic");
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(epic);
        string steamJournal = Path.Combine(steam, "Journal.2026-07-24T100000.01.log");
        string epicJournal = Path.Combine(epic, "Journal.2026-07-24T110000.01.log");
        await File.WriteAllTextAsync(steamJournal, "{\"event\":\"Commander\",\"Name\":\"Steam\",\"FID\":\"F123\"}\n");
        await File.WriteAllTextAsync(epicJournal, "{\"event\":\"Commander\",\"Name\":\"Epic\",\"FID\":\"F456\"}\n");
        await File.WriteAllTextAsync(
            Path.Combine(steam, StatusFileReader.FileName),
            "{\"event\":\"Status\",\"Flags\":0,\"GuiFocus\":0}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(epic, StatusFileReader.FileName),
            "{\"event\":\"Status\",\"Flags\":0,\"GuiFocus\":1}"
        );
        File.SetLastWriteTimeUtc(steamJournal, new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(epicJournal, new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor([steam, epic], "F456");

        JournalMonitorUpdate update = await monitor.PollAsync();

        Assert.Equal(epicJournal, update.JournalPath);
        Assert.Equal("Epic", Assert.Single(update.JournalEvents).Payload.GetProperty("Name").GetString());
        Assert.Equal(GuiFocus.InternalPanel, update.Status?.GuiFocus);
    }

    /// <summary>Targeted polls report another commander's newer journal without leaking that journal's events.</summary>
    [Fact]
    public async Task TargetedPollReportsNewerAlternateCommanderJournalWithoutReadingItsEvents()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string firstJournal = Path.Combine(temporaryDirectory, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            "{\"event\":\"Commander\",\"Name\":\"First Cmdr\",\"FID\":\"F13228986\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"First Cmdr\",\"FID\":\"F13228986\"}\n"
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));
        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F13228986");

        JournalMonitorUpdate initial = await monitor.PollAsync();
        Assert.Equal("F13228986", initial.NewestIdentifiedJournal?.FrontierId);
        Assert.Equal(firstJournal, initial.NewestIdentifiedJournal?.JournalPath);

        string secondJournal = Path.Combine(temporaryDirectory, "Journal.2026-11-23T073400.01.log");
        await File.WriteAllTextAsync(secondJournal, "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n");
        File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 34, 0, DateTimeKind.Utc));

        JournalMonitorUpdate atMenu = await monitor.PollAsync();
        Assert.Equal(firstJournal, atMenu.JournalPath);
        Assert.True(atMenu.IsAwaitingCommanderIdentity);
        Assert.Null(atMenu.NewestIdentifiedJournal);

        await File.AppendAllTextAsync(
            secondJournal,
            "{\"event\":\"Commander\",\"Name\":\"Second Cmdr\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second Cmdr\",\"FID\":\"F14004033\"}\n"
        );
        File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 34, 15, DateTimeKind.Utc));
        await File.AppendAllTextAsync(firstJournal, "{\"event\":\"Shutdown\"}\n");
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 7, 33, 50, DateTimeKind.Utc));

        JournalMonitorUpdate switched = await monitor.PollAsync();
        Assert.Equal(firstJournal, switched.JournalPath);
        Assert.Equal(["Shutdown"], switched.JournalEvents.Select(entry => entry.EventName));
        Assert.False(switched.IsAwaitingCommanderIdentity);
        Assert.Equal(secondJournal, switched.NewestIdentifiedJournal?.JournalPath);
        Assert.Equal("F14004033", switched.NewestIdentifiedJournal?.FrontierId);

        string continuation = Path.Combine(temporaryDirectory, "Journal.2026-11-23T073400.02.log");
        await File.WriteAllTextAsync(continuation, "{\"event\":\"Fileheader\",\"part\":2,\"Odyssey\":true}\n");
        File.SetLastWriteTimeUtc(continuation, new DateTime(2026, 11, 23, 7, 35, 0, DateTimeKind.Utc));

        JournalMonitorUpdate continued = await monitor.PollAsync();
        Assert.Equal(continuation, continued.NewestIdentifiedJournal?.JournalPath);
        Assert.Equal("F14004033", continued.NewestIdentifiedJournal?.FrontierId);
        Assert.False(continued.NewestIdentifiedJournal?.IsShutdown);
    }

    [Fact]
    public async Task NewestIdentifiedJournalDetectsShutdownInSmallAndLargeJournalsAndCachesResult()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string activeJournal = Path.Combine(temporaryDirectory, "Journal.2026-11-23T060000.01.log");
        await File.WriteAllTextAsync(
            activeJournal,
            "{\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"First\",\"FID\":\"F13228986\"}\n"
        );
        File.SetLastWriteTimeUtc(activeJournal, new DateTime(2026, 11, 23, 6, 0, 0, DateTimeKind.Utc));

        string closedJournal = Path.Combine(temporaryDirectory, "Journal.2026-11-23T070000.01.log");
        var largeContent = new System.Text.StringBuilder();
        largeContent.AppendLine("{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}");
        largeContent.AppendLine("{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}");
        for (int i = 0; i < 200; i++)
        {
            largeContent.AppendLine(
                global::System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"event\":\"FSDJump\",\"StarSystem\":\"System {i}\",\"SystemAddress\":{1000 + i}}}"
            );
        }
        largeContent.AppendLine("{\"event\":\"Shutdown\"}");
        await File.WriteAllTextAsync(closedJournal, largeContent.ToString());
        File.SetLastWriteTimeUtc(closedJournal, new DateTime(2026, 11, 23, 7, 0, 0, DateTimeKind.Utc));

        var monitor = new JournalDirectoryMonitor(temporaryDirectory, "F13228986");

        JournalMonitorUpdate first = await monitor.PollAsync();
        Assert.Equal("F14004033", first.NewestIdentifiedJournal?.FrontierId);
        Assert.True(first.NewestIdentifiedJournal?.IsShutdown);

        // Unchanged file reuses the cached shutdown result cleanly.
        JournalMonitorUpdate cached = await monitor.PollAsync();
        Assert.Equal("F14004033", cached.NewestIdentifiedJournal?.FrontierId);
        Assert.True(cached.NewestIdentifiedJournal?.IsShutdown);

        // Small active journal without Shutdown reports IsShutdown == false.
        string smallJournal = Path.Combine(temporaryDirectory, "Journal.2026-11-23T080000.01.log");
        await File.WriteAllTextAsync(
            smallJournal,
            "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
        );
        File.SetLastWriteTimeUtc(smallJournal, new DateTime(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc));

        JournalMonitorUpdate active = await monitor.PollAsync();
        Assert.Equal("F14004033", active.NewestIdentifiedJournal?.FrontierId);
        Assert.False(active.NewestIdentifiedJournal?.IsShutdown);
    }

    [Fact]
    public async Task CommanderSwitchResumeTreatsNewJournalsAsLiveAndSplitsPersistedPrefixes()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var baselineTime = new DateTimeOffset(2026, 11, 25, 9, 55, 0, TimeSpan.Zero);
        var emptyBaseline = JournalBaselineSnapshot.Capture([temporaryDirectory], baselineTime);

        string brandNew = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100000.01.log");
        string prefix =
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string tail =
            "{\"timestamp\":\"2026-11-25T10:00:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n";
        await File.WriteAllTextAsync(brandNew, prefix + tail);

        // Without a baseline, a missing checkpoint cannot prove the file is new and fails safe to bootstrap.
        var unverifiedMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume()
        );
        JournalMonitorUpdate unverified = await unverifiedMonitor.PollAsync();
        Assert.True(unverified.IsBootstrapRead);
        Assert.False(unverified.HasPendingLiveTail);

        // Brand-new journal created after baseline capture runs as live immediately.
        var newMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(Baseline: emptyBaseline)
        );
        JournalMonitorUpdate liveSwitch = await newMonitor.PollAsync();
        Assert.False(liveSwitch.IsBootstrapRead);
        Assert.False(liveSwitch.HasPendingLiveTail);
        Assert.Equal(4, liveSwitch.JournalEvents.Count);
        Assert.Equal(new JournalCheckpoint(brandNew, new FileInfo(brandNew).Length), newMonitor.CurrentCheckpoint);

        // A journal that already existed at baseline replays its pre-existing prefix as bootstrap and only new tail bytes as live.
        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(prefix);
        JournalBaselineSnapshot historicalBaseline = emptyBaseline.WithFloor(brandNew, prefixBytes);
        var baselineSplitMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(Baseline: historicalBaseline)
        );
        JournalMonitorUpdate historicalPrefix = await baselineSplitMonitor.PollAsync();
        Assert.True(historicalPrefix.IsBootstrapRead);
        Assert.True(historicalPrefix.HasPendingLiveTail);
        Assert.Equal(3, historicalPrefix.JournalEvents.Count);
        JournalMonitorUpdate postBaselineTail = await baselineSplitMonitor.PollAsync();
        Assert.False(postBaselineTail.IsBootstrapRead);
        Assert.False(postBaselineTail.HasPendingLiveTail);
        Assert.Single(postBaselineTail.JournalEvents);
        Assert.Equal("ScanOrganic", postBaselineTail.JournalEvents[0].EventName);

        // Pre-existing journal with persisted checkpoint splits bootstrap prefix from live tail cleanly.
        var resumedMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(new JournalCheckpoint(brandNew, prefixBytes), emptyBaseline)
        );
        JournalMonitorUpdate bootstrapPrefix = await resumedMonitor.PollAsync();
        Assert.True(bootstrapPrefix.IsBootstrapRead);
        Assert.True(bootstrapPrefix.HasPendingLiveTail);
        Assert.Equal(3, bootstrapPrefix.JournalEvents.Count);
        Assert.Equal(prefixBytes, resumedMonitor.CurrentCheckpoint!.ByteOffset);

        JournalMonitorUpdate liveTail = await resumedMonitor.PollAsync();
        Assert.False(liveTail.IsBootstrapRead);
        Assert.False(liveTail.HasPendingLiveTail);
        Assert.Single(liveTail.JournalEvents);
        Assert.Equal("ScanOrganic", liveTail.JournalEvents[0].EventName);

        // Multi-part session where part 1 was persisted and part 2 is brand new.
        string part2 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100000.02.log");
        await File.WriteAllTextAsync(
            part2,
            "{\"event\":\"Fileheader\",\"part\":2,\"Odyssey\":true}\n"
                + "{\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n"
        );
        File.SetLastWriteTimeUtc(brandNew, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(part2, new DateTime(2026, 11, 25, 10, 5, 0, DateTimeKind.Utc));

        long part1FullBytes = new FileInfo(brandNew).Length;
        var multiPartMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(new JournalCheckpoint(brandNew, part1FullBytes), emptyBaseline)
        );
        JournalMonitorUpdate part1Bootstrap = await multiPartMonitor.PollAsync();
        Assert.True(part1Bootstrap.IsBootstrapRead);
        Assert.True(part1Bootstrap.HasPendingLiveTail);
        Assert.Equal(4, part1Bootstrap.JournalEvents.Count);

        JournalMonitorUpdate part2Live = await multiPartMonitor.PollAsync();
        Assert.False(part2Live.IsBootstrapRead);
        Assert.False(part2Live.HasPendingLiveTail);
        Assert.Contains(part2Live.JournalEvents, entry => entry.EventName == "FSDJump");
    }

    [Fact]
    public async Task CommanderSwitchCatchUpReadsMultiplePostBaselineSessionsAndUnfinishedCheckpointSessionsInOrder()
    {
        Directory.CreateDirectory(temporaryDirectory);

        // Pre-existing historical session at baseline must not be replayed once newer sessions exist.
        string historical = Path.Combine(temporaryDirectory, "Journal.2026-11-25T090000.01.log");
        await File.WriteAllTextAsync(
            historical,
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"FSDJump\",\"StarSystem\":\"OldSystem\"}\n"
                + "{\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(historical, new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc));
        var baseline = JournalBaselineSnapshot.Capture(
            [temporaryDirectory],
            new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero)
        );

        // Commander B completes Session 1 and rolls into an independent Session 2 before handoff completes.
        string session1Prefix =
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string session1Tail =
            "{\"timestamp\":\"2026-11-25T10:00:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:04Z\",\"event\":\"Shutdown\"}\n";
        string session1 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(session1, session1Prefix + session1Tail);
        File.SetLastWriteTimeUtc(session1, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));

        string session2 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            session2,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"NewSystem\"}\n"
        );
        File.SetLastWriteTimeUtc(session2, new DateTime(2026, 11, 25, 10, 5, 0, DateTimeKind.Utc));

        // 1. Both brand-new sessions are replayed live in chronological order without re-reading the historical session.
        var catchUpMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(Baseline: baseline)
        );
        JournalMonitorUpdate firstCatchUp = await catchUpMonitor.PollAsync();
        Assert.Equal(session1, firstCatchUp.JournalPath);
        Assert.False(firstCatchUp.IsBootstrapRead);
        Assert.True(firstCatchUp.HasPendingLiveTail);
        Assert.Contains(firstCatchUp.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate secondCatchUp = await catchUpMonitor.PollAsync();
        Assert.Equal(session2, secondCatchUp.JournalPath);
        Assert.False(secondCatchUp.IsBootstrapRead);
        Assert.False(secondCatchUp.HasPendingLiveTail);
        Assert.Contains(secondCatchUp.JournalEvents, entry => entry.EventName == "FSDJump");

        // 2. When Session 1 had a persisted prefix and unread tail before Session 2 started, drain prefix -> tail -> Session 2.
        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(session1Prefix);
        var checkpointCatchUp = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(new JournalCheckpoint(session1, prefixBytes), baseline)
        );
        JournalMonitorUpdate s1Prefix = await checkpointCatchUp.PollAsync();
        Assert.Equal(session1, s1Prefix.JournalPath);
        Assert.True(s1Prefix.IsBootstrapRead);
        Assert.True(s1Prefix.HasPendingLiveTail);
        Assert.Equal(3, s1Prefix.JournalEvents.Count);

        JournalMonitorUpdate s1Tail = await checkpointCatchUp.PollAsync();
        Assert.Equal(session1, s1Tail.JournalPath);
        Assert.False(s1Tail.IsBootstrapRead);
        Assert.True(s1Tail.HasPendingLiveTail);
        Assert.Contains(s1Tail.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate s2Live = await checkpointCatchUp.PollAsync();
        Assert.Equal(session2, s2Live.JournalPath);
        Assert.False(s2Live.IsBootstrapRead);
        Assert.False(s2Live.HasPendingLiveTail);
        Assert.Contains(s2Live.JournalEvents, entry => entry.EventName == "FSDJump");
    }

    [Fact]
    public async Task UnrelatedJournalReadErrorDoesNotMarkSelectedJournalLiveTailAsBootstrap()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string targetPrefix =
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string targetTail =
            "{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n";
        string targetJournal = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(targetJournal, targetPrefix + targetTail);
        File.SetLastWriteTimeUtc(targetJournal, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));

        string lockedOther = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(lockedOther, "{\"event\":\"Fileheader\",\"part\":1}\n");
        File.SetLastWriteTimeUtc(lockedOther, new DateTime(2026, 11, 25, 10, 5, 0, DateTimeKind.Utc));

        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(targetPrefix);
        var monitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            _ => default,
            openJournalStream: path =>
                string.Equals(path, lockedOther, StringComparison.Ordinal)
                    ? throw new IOException("Unrelated journal is temporarily locked.")
                    : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            switchResume: new JournalSwitchResume(new JournalCheckpoint(targetJournal, prefixBytes))
        );

        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        Assert.True(bootstrap.IsBootstrapRead);
        Assert.True(bootstrap.HasPendingLiveTail);
        Assert.Contains(bootstrap.Errors, error => error.Contains("temporarily locked", StringComparison.Ordinal));

        JournalMonitorUpdate liveTail = await monitor.PollAsync();
        Assert.False(liveTail.IsBootstrapRead);
        Assert.False(liveTail.HasPendingLiveTail);
        Assert.Equal("ScanOrganic", Assert.Single(liveTail.JournalEvents).EventName);
    }

    [Fact]
    public async Task CommanderSwitchCatchUpRetriesOldestSessionAfterTransientEventReadFailure()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var baseline = JournalBaselineSnapshot.Capture(
            [temporaryDirectory],
            new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero)
        );

        string session1Prefix =
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string session1Tail =
            "{\"timestamp\":\"2026-11-25T10:00:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:04Z\",\"event\":\"Shutdown\"}\n";
        string session1 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(session1, session1Prefix + session1Tail);
        File.SetLastWriteTimeUtc(session1, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));

        string session2 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            session2,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"NewSystem\"}\n"
        );
        File.SetLastWriteTimeUtc(session2, new DateTime(2026, 11, 25, 10, 5, 0, DateTimeKind.Utc));

        // 1. Two brand-new sessions: fail opening session1 for event reading after identity scan succeeds.
        int session1Opens = 0;
        var liveMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            _ => default,
            openJournalStream: path =>
            {
                if (string.Equals(path, session1, StringComparison.Ordinal) && ++session1Opens == 2)
                {
                    throw new IOException("Session 1 temporarily locked.");
                }

                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            },
            switchResume: new JournalSwitchResume(Baseline: baseline)
        );

        JournalMonitorUpdate failedPoll = await liveMonitor.PollAsync();
        Assert.Contains(failedPoll.Errors, error => error.Contains("temporarily locked", StringComparison.Ordinal));
        Assert.Empty(failedPoll.JournalEvents);

        // Next poll retries session1 live (never skipping to session2), then poll 3 reads session2 live.
        JournalMonitorUpdate retriedSession1 = await liveMonitor.PollAsync();
        Assert.Equal(session1, retriedSession1.JournalPath);
        Assert.False(retriedSession1.IsBootstrapRead);
        Assert.True(retriedSession1.HasPendingLiveTail);
        Assert.Contains(retriedSession1.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate readSession2 = await liveMonitor.PollAsync();
        Assert.Equal(session2, readSession2.JournalPath);
        Assert.False(readSession2.IsBootstrapRead);
        Assert.False(readSession2.HasPendingLiveTail);
        Assert.Contains(readSession2.JournalEvents, entry => entry.EventName == "FSDJump");

        // 2. Checkpoint-split session1 also preserves its bootstrap/tail boundary across a transient open failure.
        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(session1Prefix);
        int checkpointOpens = 0;
        var checkpointMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            _ => default,
            openJournalStream: path =>
            {
                if (string.Equals(path, session1, StringComparison.Ordinal) && ++checkpointOpens == 2)
                {
                    throw new IOException("Session 1 checkpoint open locked.");
                }

                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            },
            switchResume: new JournalSwitchResume(new JournalCheckpoint(session1, prefixBytes), baseline)
        );

        JournalMonitorUpdate failedCheckpointPoll = await checkpointMonitor.PollAsync();
        Assert.Contains(
            failedCheckpointPoll.Errors,
            error => error.Contains("checkpoint open locked", StringComparison.Ordinal)
        );

        JournalMonitorUpdate s1Prefix = await checkpointMonitor.PollAsync();
        Assert.Equal(session1, s1Prefix.JournalPath);
        Assert.True(s1Prefix.IsBootstrapRead);
        Assert.True(s1Prefix.HasPendingLiveTail);
        Assert.Equal(3, s1Prefix.JournalEvents.Count);

        JournalMonitorUpdate s1Tail = await checkpointMonitor.PollAsync();
        Assert.Equal(session1, s1Tail.JournalPath);
        Assert.False(s1Tail.IsBootstrapRead);
        Assert.True(s1Tail.HasPendingLiveTail);
        Assert.Contains(s1Tail.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate s2Live = await checkpointMonitor.PollAsync();
        Assert.Equal(session2, s2Live.JournalPath);
        Assert.False(s2Live.IsBootstrapRead);
        Assert.False(s2Live.HasPendingLiveTail);
        Assert.Contains(s2Live.JournalEvents, entry => entry.EventName == "FSDJump");
    }

    [Fact]
    public async Task CommanderSwitchCatchUpReadsMoreThanSixteenPostBaselineSessionsWithoutSkippingOldest()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string historical = Path.Combine(temporaryDirectory, "Journal.2026-11-25T080000.01.log");
        await File.WriteAllTextAsync(
            historical,
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"FSDJump\",\"StarSystem\":\"HistoricalSystem\"}\n"
                + "{\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(historical, new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc));
        var baseline = JournalBaselineSnapshot.Capture(
            [temporaryDirectory],
            new DateTimeOffset(2026, 11, 25, 8, 30, 0, TimeSpan.Zero)
        );

        const int sessionCount = 18;
        DateTime start = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
        for (int index = 1; index <= sessionCount; index++)
        {
            DateTime timestamp = start.AddMinutes(index);
            string stamp = timestamp.ToString(
                "yyyy-MM-ddTHH:mm:ssZ",
                System.Globalization.CultureInfo.InvariantCulture
            );
            string path = Path.Combine(temporaryDirectory, $"Journal.{timestamp:yyyy-MM-ddTHHmmss}.01.log");
            string suffix = index < sessionCount ? "{\"event\":\"Shutdown\"}\n" : string.Empty;
            await File.WriteAllTextAsync(
                path,
                $"{{\"timestamp\":\"{stamp}\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}}\n"
                    + $"{{\"timestamp\":\"{stamp}\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}}\n"
                    + $"{{\"timestamp\":\"{stamp}\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}}\n"
                    + $"{{\"timestamp\":\"{stamp}\",\"event\":\"FSDJump\",\"StarSystem\":\"System-{index:D2}\"}}\n"
                    + suffix
            );
            File.SetLastWriteTimeUtc(path, timestamp);
        }

        var monitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(Baseline: baseline)
        );
        List<string> observedSystems = [];
        for (int poll = 0; poll < sessionCount + 2; poll++)
        {
            JournalMonitorUpdate update = await monitor.PollAsync();
            Assert.False(update.IsBootstrapRead);
            foreach (JournalEventEnvelope entry in update.JournalEvents)
            {
                if (
                    entry.EventName == "FSDJump"
                    && entry.Payload.TryGetProperty("StarSystem", out System.Text.Json.JsonElement system)
                )
                {
                    observedSystems.Add(system.GetString()!);
                }
            }

            if (!update.HasPendingLiveTail)
            {
                break;
            }
        }

        Assert.Equal(
            Enumerable.Range(1, sessionCount).Select(index => $"System-{index:D2}").ToArray(),
            observedSystems
        );
    }

    [Fact]
    public async Task ConfirmedCheckpointOverridesLargerStartupBaselineAndIdentityFailureRetriesOlderSession()
    {
        Directory.CreateDirectory(temporaryDirectory);

        // 1. Confirmed checkpoint at prefixBytes must win over a larger startup baseline captured after unpersisted events were appended.
        string session1Prefix =
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"FSDJump\",\"StarSystem\":\"PersistedSystem\"}\n";
        string session1UnpersistedTail =
            "{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
            + "{\"event\":\"Shutdown\"}\n";
        string session1 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(session1, session1Prefix + session1UnpersistedTail);
        File.SetLastWriteTimeUtc(session1, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));

        // Capture startup baseline AFTER session1 already contains the unpersisted tail.
        var startupBaseline = JournalBaselineSnapshot.Capture(
            [temporaryDirectory],
            new DateTimeOffset(2026, 11, 25, 10, 5, 0, TimeSpan.Zero)
        );
        long confirmedPrefixBytes = System.Text.Encoding.UTF8.GetByteCount(session1Prefix);

        // Newer session2 arrives after startup baseline.
        string session2 = Path.Combine(temporaryDirectory, "Journal.2026-11-25T101000.01.log");
        await File.WriteAllTextAsync(
            session2,
            "{\"timestamp\":\"2026-11-25T10:10:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:10:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:10:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:10:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"NewerSystem\"}\n"
        );
        File.SetLastWriteTimeUtc(session2, new DateTime(2026, 11, 25, 10, 10, 0, DateTimeKind.Utc));

        var confirmedOffsets = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [session1] = confirmedPrefixBytes,
        };
        var confirmedMonitor = new JournalDirectoryMonitor(
            [temporaryDirectory],
            "F14004033",
            new JournalSwitchResume(Baseline: startupBaseline, ConfirmedOffsets: confirmedOffsets)
        );

        JournalMonitorUpdate s1Bootstrap = await confirmedMonitor.PollAsync();
        Assert.Equal(session1, s1Bootstrap.JournalPath);
        Assert.True(s1Bootstrap.IsBootstrapRead);
        Assert.True(s1Bootstrap.HasPendingLiveTail);
        Assert.Equal(confirmedPrefixBytes, confirmedMonitor.CurrentCheckpoint!.ByteOffset);

        JournalMonitorUpdate s1LiveTail = await confirmedMonitor.PollAsync();
        Assert.Equal(session1, s1LiveTail.JournalPath);
        Assert.False(s1LiveTail.IsBootstrapRead);
        Assert.True(s1LiveTail.HasPendingLiveTail);
        Assert.Contains(s1LiveTail.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate s2Live = await confirmedMonitor.PollAsync();
        Assert.Equal(session2, s2Live.JournalPath);
        Assert.False(s2Live.IsBootstrapRead);
        Assert.False(s2Live.HasPendingLiveTail);
        Assert.Contains(s2Live.JournalEvents, entry => entry.EventName == "FSDJump");

        // 2. A transient identity-read error on an older post-baseline session must keep catch-up selection retryable rather than skipping to session2.
        string identityDir = Path.Combine(temporaryDirectory, "identity-retry");
        Directory.CreateDirectory(identityDir);
        var emptyBaseline = JournalBaselineSnapshot.Capture(
            [identityDir],
            new DateTimeOffset(2026, 11, 25, 10, 30, 0, TimeSpan.Zero)
        );
        string older = Path.Combine(identityDir, "Journal.2026-11-25T110000.01.log");
        await File.WriteAllTextAsync(
            older,
            "{\"timestamp\":\"2026-11-25T11:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T11:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T11:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T11:00:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
                + "{\"timestamp\":\"2026-11-25T11:00:04Z\",\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 11, 25, 11, 0, 0, DateTimeKind.Utc));

        string newer = Path.Combine(identityDir, "Journal.2026-11-25T110500.01.log");
        await File.WriteAllTextAsync(
            newer,
            "{\"timestamp\":\"2026-11-25T11:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T11:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T11:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T11:05:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"AfterIdentityRetry\"}\n"
        );
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 11, 25, 11, 5, 0, DateTimeKind.Utc));

        int olderOpens = 0;
        var identityRetryMonitor = new JournalDirectoryMonitor(
            [identityDir],
            "F14004033",
            _ => default,
            openJournalStream: path =>
            {
                if (string.Equals(path, older, StringComparison.Ordinal) && ++olderOpens == 1)
                {
                    throw new IOException("Older journal identity scan temporarily locked.");
                }

                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            },
            switchResume: new JournalSwitchResume(Baseline: emptyBaseline)
        );

        JournalMonitorUpdate deferredPoll = await identityRetryMonitor.PollAsync();
        Assert.Contains(
            deferredPoll.Errors,
            error => error.Contains("identity scan temporarily locked", StringComparison.Ordinal)
        );
        Assert.Empty(deferredPoll.JournalEvents);
        Assert.Null(deferredPoll.JournalPath);

        // Once the older session is readable, catch-up delivers older live first and newer live second.
        JournalMonitorUpdate olderLive = await identityRetryMonitor.PollAsync();
        Assert.Equal(older, olderLive.JournalPath);
        Assert.False(olderLive.IsBootstrapRead);
        Assert.True(olderLive.HasPendingLiveTail);
        Assert.Contains(olderLive.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate newerLive = await identityRetryMonitor.PollAsync();
        Assert.Equal(newer, newerLive.JournalPath);
        Assert.False(newerLive.IsBootstrapRead);
        Assert.False(newerLive.HasPendingLiveTail);
        Assert.Contains(newerLive.JournalEvents, entry => entry.EventName == "FSDJump");

        // 3. Even when an older saved checkpoint (A) IS found, an unreadable middle journal (B) between checkpoint A and newer journal C must defer catch-up until B can be identified.
        string middleDir = Path.Combine(temporaryDirectory, "middle-identity-retry");
        Directory.CreateDirectory(middleDir);
        string journalAPrefix =
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string journalATail =
            "{\"event\":\"FSDJump\",\"StarSystem\":\"JournalATail\"}\n" + "{\"event\":\"Shutdown\"}\n";
        string journalA = Path.Combine(middleDir, "Journal.2026-11-25T120000.01.log");
        await File.WriteAllTextAsync(journalA, journalAPrefix + journalATail);
        File.SetLastWriteTimeUtc(journalA, new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc));
        var middleBaseline = JournalBaselineSnapshot.Capture(
            [middleDir],
            new DateTimeOffset(2026, 11, 25, 12, 2, 0, TimeSpan.Zero)
        );
        long journalACheckpointBytes = System.Text.Encoding.UTF8.GetByteCount(journalAPrefix);

        string journalB = Path.Combine(middleDir, "Journal.2026-11-25T120500.01.log");
        await File.WriteAllTextAsync(
            journalB,
            "{\"timestamp\":\"2026-11-25T12:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T12:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T12:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T12:05:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
                + "{\"timestamp\":\"2026-11-25T12:05:04Z\",\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(journalB, new DateTime(2026, 11, 25, 12, 5, 0, DateTimeKind.Utc));

        string journalC = Path.Combine(middleDir, "Journal.2026-11-25T121000.01.log");
        await File.WriteAllTextAsync(
            journalC,
            "{\"timestamp\":\"2026-11-25T12:10:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T12:10:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T12:10:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T12:10:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"JournalCLatest\"}\n"
        );
        File.SetLastWriteTimeUtc(journalC, new DateTime(2026, 11, 25, 12, 10, 0, DateTimeKind.Utc));

        int journalBOpens = 0;
        var middleRetryMonitor = new JournalDirectoryMonitor(
            [middleDir],
            "F14004033",
            _ => default,
            openJournalStream: path =>
            {
                if (string.Equals(path, journalB, StringComparison.Ordinal) && ++journalBOpens == 1)
                {
                    throw new IOException("Middle journal temporarily locked.");
                }

                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            },
            switchResume: new JournalSwitchResume(
                new JournalCheckpoint(journalA, journalACheckpointBytes),
                middleBaseline
            )
        );

        JournalMonitorUpdate blockedMiddle = await middleRetryMonitor.PollAsync();
        Assert.Contains(
            blockedMiddle.Errors,
            error => error.Contains("Middle journal temporarily locked", StringComparison.Ordinal)
        );
        Assert.Empty(blockedMiddle.JournalEvents);
        Assert.Null(blockedMiddle.JournalPath);

        JournalMonitorUpdate aBootstrap = await middleRetryMonitor.PollAsync();
        Assert.Equal(journalA, aBootstrap.JournalPath);
        Assert.True(aBootstrap.IsBootstrapRead);
        Assert.True(aBootstrap.HasPendingLiveTail);

        JournalMonitorUpdate aTail = await middleRetryMonitor.PollAsync();
        Assert.Equal(journalA, aTail.JournalPath);
        Assert.False(aTail.IsBootstrapRead);
        Assert.True(aTail.HasPendingLiveTail);
        Assert.Contains(aTail.JournalEvents, entry => entry.EventName == "FSDJump");

        JournalMonitorUpdate bLive = await middleRetryMonitor.PollAsync();
        Assert.Equal(journalB, bLive.JournalPath);
        Assert.False(bLive.IsBootstrapRead);
        Assert.True(bLive.HasPendingLiveTail);
        Assert.Contains(bLive.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate cLive = await middleRetryMonitor.PollAsync();
        Assert.Equal(journalC, cLive.JournalPath);
        Assert.False(cLive.IsBootstrapRead);
        Assert.False(cLive.HasPendingLiveTail);
        Assert.Contains(cLive.JournalEvents, entry => entry.EventName == "FSDJump");
    }

    [Fact]
    public async Task EmptyFirstPollDuringCommanderSwitchKeepsBootstrapBoundaryUntilTargetJournalAppears()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string switchDir = Path.Combine(temporaryDirectory, "empty-first-poll");
        Directory.CreateDirectory(switchDir);

        string targetJournal = Path.Combine(switchDir, "Journal.2026-11-25T130000.01.log");
        string historicalPrefix =
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"FSDJump\",\"StarSystem\":\"AlreadyCounted\"}\n"
            + "{\"event\":\"SendText\",\"To\":\"local\",\"Message\":\".exit\"}\n";
        string liveTail = "{\"event\":\"FSDJump\",\"StarSystem\":\"NewAfterAppear\"}\n";
        long historicalBytes = System.Text.Encoding.UTF8.GetByteCount(historicalPrefix);

        // Capture baseline with targetJournal recorded at historicalBytes, then simulate targetJournal being temporarily absent during Poll 1.
        await File.WriteAllTextAsync(targetJournal, historicalPrefix);
        var baseline = JournalBaselineSnapshot.Capture([switchDir]);
        File.Delete(targetJournal);

        var monitor = new JournalDirectoryMonitor(
            [switchDir],
            "F14004033",
            new JournalSwitchResume(new JournalCheckpoint(targetJournal, historicalBytes), baseline)
        );

        // Poll 1 sees no journal and no errors; bootstrap must stay pending until targetJournal is read.
        JournalMonitorUpdate emptyPoll = await monitor.PollAsync();
        Assert.Null(emptyPoll.JournalPath);
        Assert.Empty(emptyPoll.Errors);
        Assert.Empty(emptyPoll.JournalEvents);

        // When targetJournal reappears with historicalPrefix + liveTail, Poll 2 MUST still split bootstrap prefix from live tail.
        await File.WriteAllTextAsync(targetJournal, historicalPrefix + liveTail);
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        Assert.Equal(targetJournal, bootstrap.JournalPath);
        Assert.True(bootstrap.IsBootstrapRead);
        Assert.True(bootstrap.HasPendingLiveTail);
        Assert.Equal(5, bootstrap.JournalEvents.Count);
        Assert.Equal(historicalBytes, monitor.CurrentCheckpoint!.ByteOffset);

        JournalMonitorUpdate tail = await monitor.PollAsync();
        Assert.Equal(targetJournal, tail.JournalPath);
        Assert.False(tail.IsBootstrapRead);
        Assert.False(tail.HasPendingLiveTail);
        Assert.Equal("FSDJump", Assert.Single(tail.JournalEvents).EventName);
    }

    [Fact]
    public async Task StartupUnreadableNewerJournalPreservesBootstrapBoundaryWhenUnlocked()
    {
        Directory.CreateDirectory(temporaryDirectory);

        foreach (string? targetFrontierId in new string?[] { "F14004033", null })
        {
            string dir = Path.Combine(temporaryDirectory, targetFrontierId ?? "automatic");
            Directory.CreateDirectory(dir);

            string older = Path.Combine(dir, "Journal.2026-11-25T100000.01.log");
            await File.WriteAllTextAsync(
                older,
                "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                    + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                    + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                    + "{\"event\":\"FSDJump\",\"StarSystem\":\"OlderSystem\"}\n"
            );
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));

            string newerHistorical =
                "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"SendText\",\"To\":\"local\",\"Message\":\".exit\"}\n";
            string newerLiveTail =
                "{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n";
            string newer = Path.Combine(dir, "Journal.2026-11-25T100500.01.log");
            await File.WriteAllTextAsync(newer, newerHistorical);
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 11, 25, 10, 5, 0, DateTimeKind.Utc));

            bool lockNewer = true;
            var monitor = new JournalDirectoryMonitor(
                [dir],
                targetFrontierId,
                _ => default,
                openJournalStream: path =>
                    lockNewer && string.Equals(path, newer, StringComparison.Ordinal)
                        ? throw new IOException("Newest startup journal temporarily locked.")
                        : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
            );

            // Poll 1: newer is locked, so monitor bootstraps from older and records newer's startup length.
            JournalMonitorUpdate firstPoll = await monitor.PollAsync();
            Assert.Equal(older, firstPoll.JournalPath);
            Assert.True(firstPoll.IsBootstrapRead);
            Assert.Contains(
                firstPoll.Errors,
                error => error.Contains("Newest startup journal temporarily locked", StringComparison.Ordinal)
            );

            // While locked, a live event is appended to newer; then newer unlocks.
            await File.AppendAllTextAsync(newer, newerLiveTail);
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 11, 25, 10, 6, 0, DateTimeKind.Utc));
            lockNewer = false;

            // Poll 2: pre-existing historical contents of newer MUST still be read as bootstrap (never live).
            JournalMonitorUpdate unlockedBootstrap = await monitor.PollAsync();
            Assert.Equal(newer, unlockedBootstrap.JournalPath);
            Assert.True(unlockedBootstrap.IsBootstrapRead);
            Assert.True(unlockedBootstrap.HasPendingLiveTail);
            Assert.Contains(unlockedBootstrap.JournalEvents, entry => entry.EventName == "SendText");
            Assert.DoesNotContain(unlockedBootstrap.JournalEvents, entry => entry.EventName == "ScanOrganic");

            // Poll 3: post-startup appended tail runs as live.
            JournalMonitorUpdate unlockedLiveTail = await monitor.PollAsync();
            Assert.Equal(newer, unlockedLiveTail.JournalPath);
            Assert.False(unlockedLiveTail.IsBootstrapRead);
            Assert.False(unlockedLiveTail.HasPendingLiveTail);
            Assert.Equal("ScanOrganic", Assert.Single(unlockedLiveTail.JournalEvents).EventName);
        }
    }

    [Fact]
    public async Task CommanderSwitchCatchUpRecoversLockedNewestSessionEvenAfterNewerSessionStarts()
    {
        Directory.CreateDirectory(temporaryDirectory);

        // 1. Locked newest session (session2) must not be skipped when session3 starts before session2 unlocks.
        string dir = Path.Combine(temporaryDirectory, "locked-newest-catchup");
        Directory.CreateDirectory(dir);
        string session1Prefix =
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string session1Tail =
            "{\"event\":\"FSDJump\",\"StarSystem\":\"Session1Tail\"}\n" + "{\"event\":\"Shutdown\"}\n";
        string session1 = Path.Combine(dir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(session1, session1Prefix + session1Tail);
        File.SetLastWriteTimeUtc(session1, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));
        var baseline = JournalBaselineSnapshot.Capture(
            [dir],
            new DateTimeOffset(2026, 11, 25, 10, 2, 0, TimeSpan.Zero)
        );
        long session1PrefixBytes = System.Text.Encoding.UTF8.GetByteCount(session1Prefix);

        string session2 = Path.Combine(dir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            session2,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:04Z\",\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(session2, new DateTime(2026, 11, 25, 10, 5, 0, DateTimeKind.Utc));

        bool lockSession2 = true;
        var monitor = new JournalDirectoryMonitor(
            [dir],
            "F14004033",
            _ => default,
            openJournalStream: path =>
                lockSession2 && string.Equals(path, session2, StringComparison.Ordinal)
                    ? throw new IOException("Session 2 temporarily locked.")
                    : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            switchResume: new JournalSwitchResume(new JournalCheckpoint(session1, session1PrefixBytes), baseline)
        );

        JournalMonitorUpdate s1Bootstrap = await monitor.PollAsync();
        Assert.Equal(session1, s1Bootstrap.JournalPath);
        Assert.True(s1Bootstrap.IsBootstrapRead);
        Assert.True(s1Bootstrap.HasPendingLiveTail);
        Assert.Contains(
            s1Bootstrap.Errors,
            error => error.Contains("Session 2 temporarily locked", StringComparison.Ordinal)
        );

        JournalMonitorUpdate s1Tail = await monitor.PollAsync();
        Assert.Equal(session1, s1Tail.JournalPath);
        Assert.False(s1Tail.IsBootstrapRead);
        Assert.False(s1Tail.HasPendingLiveTail);
        Assert.Contains(s1Tail.JournalEvents, entry => entry.EventName == "FSDJump");

        // Commander opens session3 before session2 unlocks.
        string session3 = Path.Combine(dir, "Journal.2026-11-25T101000.01.log");
        await File.WriteAllTextAsync(
            session3,
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"FSDJump\",\"StarSystem\":\"Session3Latest\"}\n"
        );
        File.SetLastWriteTimeUtc(session3, new DateTime(2026, 11, 25, 10, 10, 0, DateTimeKind.Utc));

        // Poll 3 while session2 is still locked must NOT jump ahead to session3.
        JournalMonitorUpdate waitingPoll = await monitor.PollAsync();
        Assert.Equal(session1, waitingPoll.JournalPath);
        Assert.Empty(waitingPoll.JournalEvents);
        Assert.Contains(
            waitingPoll.Errors,
            error => error.Contains("Session 2 temporarily locked", StringComparison.Ordinal)
        );

        // Once session2 unlocks, session2 and session3 both replay live in chronological order.
        lockSession2 = false;
        JournalMonitorUpdate s2Live = await monitor.PollAsync();
        Assert.Equal(session2, s2Live.JournalPath);
        Assert.False(s2Live.IsBootstrapRead);
        Assert.True(s2Live.HasPendingLiveTail);
        Assert.Contains(s2Live.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate s3Live = await monitor.PollAsync();
        Assert.Equal(session3, s3Live.JournalPath);
        Assert.False(s3Live.IsBootstrapRead);
        Assert.False(s3Live.HasPendingLiveTail);
        Assert.Contains(s3Live.JournalEvents, entry => entry.EventName == "FSDJump");

        // 2. When the locked newest file itself holds the saved checkpoint prefix, unlocking it still splits bootstrap prefix from live tail.
        string checkpointDir = Path.Combine(temporaryDirectory, "locked-checkpoint-file");
        Directory.CreateDirectory(checkpointDir);
        string olderHistorical = Path.Combine(checkpointDir, "Journal.2026-11-25T090000.01.log");
        await File.WriteAllTextAsync(
            olderHistorical,
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(olderHistorical, new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc));

        string checkpointPrefix =
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"event\":\"SendText\",\"To\":\"local\",\"Message\":\".exit\"}\n";
        string checkpointTail =
            "{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n";
        string checkpointJournal = Path.Combine(checkpointDir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(checkpointJournal, checkpointPrefix + checkpointTail);
        File.SetLastWriteTimeUtc(checkpointJournal, new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));
        var checkpointBaseline = JournalBaselineSnapshot.Capture([checkpointDir]);
        long checkpointBytes = System.Text.Encoding.UTF8.GetByteCount(checkpointPrefix);

        bool lockCheckpointJournal = true;
        var checkpointMonitor = new JournalDirectoryMonitor(
            [checkpointDir],
            "F14004033",
            _ => default,
            openJournalStream: path =>
                lockCheckpointJournal && string.Equals(path, checkpointJournal, StringComparison.Ordinal)
                    ? throw new IOException("Checkpoint journal temporarily locked.")
                    : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            switchResume: new JournalSwitchResume(
                new JournalCheckpoint(checkpointJournal, checkpointBytes),
                checkpointBaseline
            )
        );

        JournalMonitorUpdate fallbackOlder = await checkpointMonitor.PollAsync();
        Assert.Equal(olderHistorical, fallbackOlder.JournalPath);
        Assert.True(fallbackOlder.IsBootstrapRead);

        lockCheckpointJournal = false;
        JournalMonitorUpdate recoveredPrefix = await checkpointMonitor.PollAsync();
        Assert.Equal(checkpointJournal, recoveredPrefix.JournalPath);
        Assert.True(recoveredPrefix.IsBootstrapRead);
        Assert.True(recoveredPrefix.HasPendingLiveTail);
        Assert.Contains(recoveredPrefix.JournalEvents, entry => entry.EventName == "SendText");
        Assert.DoesNotContain(recoveredPrefix.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate recoveredTail = await checkpointMonitor.PollAsync();
        Assert.Equal(checkpointJournal, recoveredTail.JournalPath);
        Assert.False(recoveredTail.IsBootstrapRead);
        Assert.False(recoveredTail.HasPendingLiveTail);
        Assert.Equal("ScanOrganic", Assert.Single(recoveredTail.JournalEvents).EventName);
    }

    [Fact]
    public async Task RestoredHistoricalJournalAbsentFromBaselineRunsAsBootstrapAndDoesNotTriggerLiveEffects()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string dir = Path.Combine(temporaryDirectory, "restored-historical");
        Directory.CreateDirectory(dir);
        DateTimeOffset startupUtc = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        var baseline = JournalBaselineSnapshot.Capture([dir], startupUtc);

        // 1. Copy an old journal (timestamp from 10 days earlier, no Shutdown, contains .kill) into the watched directory with current mtime.
        string restoredOld = Path.Combine(dir, "Journal.2026-11-15T080000.01.log");
        await File.WriteAllTextAsync(
            restoredOld,
            "{\"timestamp\":\"2026-11-15T08:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-15T08:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-15T08:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-15T08:01:00Z\",\"event\":\"SendText\",\"To\":\"local\",\"Message\":\".kill\"}\n"
        );
        File.SetLastWriteTimeUtc(restoredOld, startupUtc.AddMinutes(5).UtcDateTime);

        var restoredMonitor = new JournalDirectoryMonitor(
            [dir],
            "F14004033",
            new JournalSwitchResume(Baseline: baseline)
        );
        JournalMonitorUpdate restoredUpdate = await restoredMonitor.PollAsync();
        Assert.Equal(restoredOld, restoredUpdate.JournalPath);
        Assert.True(restoredUpdate.IsBootstrapRead);
        Assert.False(restoredUpdate.HasPendingLiveTail);
        Assert.Contains(restoredUpdate.JournalEvents, entry => entry.EventName == "SendText");

        // 2. A journal without valid timestamps also lacks positive evidence of being new and must fail safe to bootstrap.
        string noTimestampDir = Path.Combine(temporaryDirectory, "restored-no-timestamp");
        Directory.CreateDirectory(noTimestampDir);
        var noTimestampBaseline = JournalBaselineSnapshot.Capture([noTimestampDir], startupUtc);
        string untimestamped = Path.Combine(noTimestampDir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            untimestamped,
            "{\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"event\":\"SendText\",\"To\":\"local\",\"Message\":\".kill\"}\n"
        );

        var untimestampedMonitor = new JournalDirectoryMonitor(
            [noTimestampDir],
            "F14004033",
            new JournalSwitchResume(Baseline: noTimestampBaseline)
        );
        JournalMonitorUpdate untimestampedUpdate = await untimestampedMonitor.PollAsync();
        Assert.Equal(untimestamped, untimestampedUpdate.JournalPath);
        Assert.True(untimestampedUpdate.IsBootstrapRead);
    }

    [Fact]
    public async Task PendingLiveTailDrainClearsStaleNewestIdentifiedJournalObservation()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string dir = Path.Combine(temporaryDirectory, "pending-tail-fresh-observation");
        Directory.CreateDirectory(dir);

        string targetPrefix =
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"First\",\"FID\":\"F13228986\"}\n";
        string targetTail =
            "{\"timestamp\":\"2026-11-25T10:00:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n";
        string targetJournal = Path.Combine(dir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(targetJournal, targetPrefix + targetTail);
        File.SetLastWriteTimeUtc(targetJournal, new DateTime(2026, 11, 25, 10, 0, 3, DateTimeKind.Utc));

        string otherCommander = Path.Combine(dir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            otherCommander,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
        );
        File.SetLastWriteTimeUtc(otherCommander, new DateTime(2026, 11, 25, 10, 5, 2, DateTimeKind.Utc));

        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(targetPrefix);
        var monitor = new JournalDirectoryMonitor(
            [dir],
            "F13228986",
            new JournalSwitchResume(new JournalCheckpoint(targetJournal, prefixBytes))
        );

        // Poll 1 discovers otherCommander as newest while splitting targetJournal's bootstrap prefix.
        JournalMonitorUpdate poll1 = await monitor.PollAsync();
        Assert.True(poll1.HasPendingLiveTail);
        Assert.Equal("F14004033", poll1.NewestIdentifiedJournal?.FrontierId);

        // Before Poll 2 drains targetJournal's live tail, Elite restarts into a newer menu-only journal.
        string menuOnly = Path.Combine(dir, "Journal.2026-11-25T100600.01.log");
        await File.WriteAllTextAsync(
            menuOnly,
            "{\"timestamp\":\"2026-11-25T10:06:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
        );
        File.SetLastWriteTimeUtc(menuOnly, new DateTime(2026, 11, 25, 10, 6, 0, DateTimeKind.Utc));

        // Poll 2 drains the pending live tail without running FindLatestJournalAsync; it MUST NOT leak stale F14004033.
        JournalMonitorUpdate poll2 = await monitor.PollAsync();
        Assert.False(poll2.HasPendingLiveTail);
        Assert.Null(poll2.NewestIdentifiedJournal);
        Assert.Equal("ScanOrganic", Assert.Single(poll2.JournalEvents).EventName);

        // Poll 3 scans disk freshly and sees menu-only -> NewestIdentifiedJournal stays null.
        JournalMonitorUpdate poll3 = await monitor.PollAsync();
        Assert.Null(poll3.NewestIdentifiedJournal);
    }

    [Fact]
    public async Task CommanderSwitchCatchUpDefersCompanionSnapshotsUntilLiveTailDrains()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string dir = Path.Combine(temporaryDirectory, "catchup-cargo-defer");
        Directory.CreateDirectory(dir);

        string prefix =
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string tail =
            "{\"timestamp\":\"2026-11-25T10:00:04Z\",\"event\":\"MarketBuy\",\"Type\":\"steel\",\"Type_Localised\":\"Steel\",\"Count\":10}\n";
        string journal = Path.Combine(dir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(journal, prefix + tail);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "Cargo.json"),
            """{"timestamp":"2026-11-25T10:00:05Z","event":"Cargo","Vessel":"Ship","Count":10,"Inventory":[{"Name":"steel","Name_Localised":"Steel","Count":10,"Stolen":0}]}"""
        );

        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(prefix);
        var monitor = new JournalDirectoryMonitor(
            [dir],
            "F14004033",
            new JournalSwitchResume(new JournalCheckpoint(journal, prefixBytes))
        );

        // Poll 1 (bootstrap split with unread tail remaining) must defer Cargo.json until catch-up finishes.
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        Assert.True(bootstrap.IsBootstrapRead);
        Assert.True(bootstrap.HasPendingLiveTail);
        Assert.Null(bootstrap.Cargo);

        // Poll 2 (final live tail containing MarketBuy) delivers Cargo.json so inventory reconciles to 10, not 20.
        JournalMonitorUpdate liveTail = await monitor.PollAsync();
        Assert.False(liveTail.IsBootstrapRead);
        Assert.False(liveTail.HasPendingLiveTail);
        Assert.NotNull(liveTail.Cargo);

        var cargoState = new CargoInventoryState();
        foreach (JournalEventEnvelope entry in bootstrap.JournalEvents)
        {
            cargoState.Apply(entry);
        }

        if (bootstrap.Cargo is not null)
        {
            cargoState.Reset(bootstrap.Cargo);
        }

        foreach (JournalEventEnvelope entry in liveTail.JournalEvents)
        {
            cargoState.Apply(entry);
        }

        if (liveTail.Cargo is not null)
        {
            cargoState.Reset(liveTail.Cargo);
        }

        CargoSnapshot? finalCargo = cargoState.CreateSnapshot();
        Assert.NotNull(finalCargo);
        Assert.Equal(10, finalCargo.Count);
        Assert.Equal(10, Assert.Single(finalCargo.Inventory).Count);
    }

    [Fact]
    public async Task UnreadableUnchangedHistoricalJournalDoesNotBlockNewCommanderTrackingOnSwitch()
    {
        Directory.CreateDirectory(temporaryDirectory);

        // 1. Permanently unreadable historical file unchanged since startup baseline must NOT block a brand-new commander journal.
        string unchangedDir = Path.Combine(temporaryDirectory, "unreadable-historical");
        Directory.CreateDirectory(unchangedDir);
        string oldLocked = Path.Combine(unchangedDir, "Journal.2026-11-25T090000.01.log");
        await File.WriteAllTextAsync(
            oldLocked,
            "{\"timestamp\":\"2026-11-25T09:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T09:00:01Z\",\"event\":\"Commander\",\"Name\":\"Other\",\"FID\":\"F99999999\"}\n"
        );
        File.SetLastWriteTimeUtc(oldLocked, new DateTime(2026, 11, 25, 9, 0, 1, DateTimeKind.Utc));
        var baseline = JournalBaselineSnapshot.Capture(
            [unchangedDir],
            new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero)
        );

        string brandNew = Path.Combine(unchangedDir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            brandNew,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n"
        );
        File.SetLastWriteTimeUtc(brandNew, new DateTime(2026, 11, 25, 10, 5, 3, DateTimeKind.Utc));

        var unblockedMonitor = new JournalDirectoryMonitor(
            [unchangedDir],
            "F14004033",
            _ => default,
            openJournalStream: path =>
                string.Equals(path, oldLocked, StringComparison.Ordinal)
                    ? throw new IOException("Unrelated historical journal permanently locked.")
                    : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            switchResume: new JournalSwitchResume(Baseline: baseline)
        );

        JournalMonitorUpdate liveUpdate = await unblockedMonitor.PollAsync();
        Assert.Equal(brandNew, liveUpdate.JournalPath);
        Assert.False(liveUpdate.IsBootstrapRead);
        Assert.False(liveUpdate.HasPendingLiveTail);
        Assert.Contains(liveUpdate.JournalEvents, entry => entry.EventName == "FSDJump");

        // 2. If a pre-baseline file HAS grown since baseline (unread post-baseline bytes), it is inside the catch-up window and still defers until readable.
        string grownDir = Path.Combine(temporaryDirectory, "unreadable-grown");
        Directory.CreateDirectory(grownDir);
        string grownPrefix =
            "{\"timestamp\":\"2026-11-25T09:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T09:00:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
            + "{\"timestamp\":\"2026-11-25T09:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n";
        string grownFile = Path.Combine(grownDir, "Journal.2026-11-25T090000.01.log");
        await File.WriteAllTextAsync(grownFile, grownPrefix);
        File.SetLastWriteTimeUtc(grownFile, new DateTime(2026, 11, 25, 9, 0, 2, DateTimeKind.Utc));
        var grownBaseline = JournalBaselineSnapshot.Capture(
            [grownDir],
            new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero)
        );

        await File.AppendAllTextAsync(
            grownFile,
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(grownFile, new DateTime(2026, 11, 25, 10, 0, 1, DateTimeKind.Utc));

        string newerSession = Path.Combine(grownDir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            newerSession,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Achenar\"}\n"
        );
        File.SetLastWriteTimeUtc(newerSession, new DateTime(2026, 11, 25, 10, 5, 3, DateTimeKind.Utc));

        bool lockGrown = true;
        var grownMonitor = new JournalDirectoryMonitor(
            [grownDir],
            "F14004033",
            _ => default,
            openJournalStream: path =>
                lockGrown && string.Equals(path, grownFile, StringComparison.Ordinal)
                    ? throw new IOException("Grown baseline journal temporarily locked.")
                    : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            switchResume: new JournalSwitchResume(Baseline: grownBaseline)
        );

        JournalMonitorUpdate deferred = await grownMonitor.PollAsync();
        Assert.Null(deferred.JournalPath);
        Assert.Contains(deferred.Errors, error => error.Contains("Grown baseline journal", StringComparison.Ordinal));

        lockGrown = false;
        JournalMonitorUpdate grownBootstrap = await grownMonitor.PollAsync();
        Assert.Equal(grownFile, grownBootstrap.JournalPath);
        Assert.True(grownBootstrap.IsBootstrapRead);
        Assert.True(grownBootstrap.HasPendingLiveTail);

        JournalMonitorUpdate grownTail = await grownMonitor.PollAsync();
        Assert.Equal(grownFile, grownTail.JournalPath);
        Assert.False(grownTail.IsBootstrapRead);
        Assert.True(grownTail.HasPendingLiveTail);
        Assert.Contains(grownTail.JournalEvents, entry => entry.EventName == "ScanOrganic");

        JournalMonitorUpdate newerLive = await grownMonitor.PollAsync();
        Assert.Equal(newerSession, newerLive.JournalPath);
        Assert.False(newerLive.IsBootstrapRead);
        Assert.False(newerLive.HasPendingLiveTail);
        Assert.Contains(newerLive.JournalEvents, entry => entry.EventName == "FSDJump");
    }

    [Fact]
    public async Task NewerOtherCommanderJournalInSameDirectorySuppressesCompanionSnapshotsWhileOtherDirectoryStaysActive()
    {
        Directory.CreateDirectory(temporaryDirectory);

        // 1. Same directory: when Commander A's journal has no Shutdown and Commander B starts in the same folder,
        // Commander A's monitor must NOT read Commander B's Cargo.json or Status.json.
        string sharedDir = Path.Combine(temporaryDirectory, "shared");
        Directory.CreateDirectory(sharedDir);
        string journalA = Path.Combine(sharedDir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(
            journalA,
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"First\",\"FID\":\"F13228986\"}\n"
        );
        File.SetLastWriteTimeUtc(journalA, new DateTime(2026, 11, 25, 10, 0, 2, DateTimeKind.Utc));
        await File.WriteAllTextAsync(
            Path.Combine(sharedDir, "Cargo.json"),
            """{"timestamp":"2026-11-25T10:00:03Z","event":"Cargo","Vessel":"Ship","Count":100,"Inventory":[{"Name":"gold","Name_Localised":"Gold","Count":100,"Stolen":0}]}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(sharedDir, "Status.json"),
            """{"timestamp":"2026-11-25T10:00:03Z","event":"Status","Flags":1,"Flags2":0}"""
        );

        var monitorA = new JournalDirectoryMonitor([sharedDir], "F13228986");
        JournalMonitorUpdate initial = await monitorA.PollAsync();
        Assert.False(initial.IsCompanionDirectorySuperseded);
        Assert.NotNull(initial.Cargo);
        Assert.Equal(100, initial.Cargo.Count);
        Assert.NotNull(initial.Status);

        // Commander B starts in the same folder and overwrites Cargo.json and Status.json.
        string journalB = Path.Combine(sharedDir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            journalB,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Second\",\"FID\":\"F14004033\"}\n"
        );
        File.SetLastWriteTimeUtc(journalB, new DateTime(2026, 11, 25, 10, 5, 2, DateTimeKind.Utc));
        await File.WriteAllTextAsync(
            Path.Combine(sharedDir, "Cargo.json"),
            """{"timestamp":"2026-11-25T10:05:03Z","event":"Cargo","Vessel":"Ship","Count":50,"Inventory":[{"Name":"steel","Name_Localised":"Steel","Count":50,"Stolen":0}]}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(sharedDir, "Status.json"),
            """{"timestamp":"2026-11-25T10:05:03Z","event":"Status","Flags":5,"Flags2":0}"""
        );

        JournalMonitorUpdate superseded = await monitorA.PollAsync();
        Assert.Equal(journalA, superseded.JournalPath);
        Assert.True(superseded.IsCompanionDirectorySuperseded);
        Assert.True(superseded.SessionContextChanged);
        Assert.Null(superseded.Cargo);
        Assert.Null(superseded.Status);
        Assert.Equal("F14004033", superseded.NewestIdentifiedJournal?.FrontierId);

        // 2. Separate directories: Commander B in epic/ must NOT supersede Commander A's own steam/ companions.
        string steamDir = Path.Combine(temporaryDirectory, "steam");
        string epicDir = Path.Combine(temporaryDirectory, "epic");
        Directory.CreateDirectory(steamDir);
        Directory.CreateDirectory(epicDir);
        string steamA = Path.Combine(steamDir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(
            steamA,
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
        );
        File.SetLastWriteTimeUtc(steamA, new DateTime(2026, 11, 25, 10, 0, 1, DateTimeKind.Utc));
        await File.WriteAllTextAsync(
            Path.Combine(steamDir, "Cargo.json"),
            """{"timestamp":"2026-11-25T10:00:03Z","event":"Cargo","Vessel":"Ship","Count":100,"Inventory":[{"Name":"gold","Name_Localised":"Gold","Count":100,"Stolen":0}]}"""
        );
        string epicB = Path.Combine(epicDir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            epicB,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
        );
        File.SetLastWriteTimeUtc(epicB, new DateTime(2026, 11, 25, 10, 5, 1, DateTimeKind.Utc));

        var multiDirMonitorA = new JournalDirectoryMonitor([steamDir, epicDir], "F13228986");
        JournalMonitorUpdate isolated = await multiDirMonitorA.PollAsync();
        Assert.Equal(steamA, isolated.JournalPath);
        Assert.False(isolated.IsCompanionDirectorySuperseded);
        Assert.NotNull(isolated.Cargo);
        Assert.Equal(100, isolated.Cargo.Count);
    }

    [Fact]
    public async Task FinalCatchUpPollRevalidatesDirectoryOwnershipAndRecoversFromDeletedCatchUpFile()
    {
        Directory.CreateDirectory(temporaryDirectory);

        // 1. If another commander opens a newer journal in the same folder between Poll 1 (bootstrap split)
        // and Poll 2 (final live tail drain), Poll 2 MUST revalidate directory ownership and skip companion reads.
        string dir = Path.Combine(temporaryDirectory, "catchup-recheck-owner");
        Directory.CreateDirectory(dir);
        string prefix =
            "{\"timestamp\":\"2026-11-25T10:00:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
            + "{\"timestamp\":\"2026-11-25T10:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"First\",\"FID\":\"F13228986\"}\n";
        string tail =
            "{\"timestamp\":\"2026-11-25T10:00:03Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Species\":\"$Codex_Ent_Aleoids_01_Name;\"}\n";
        string journalA = Path.Combine(dir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(journalA, prefix + tail);
        File.SetLastWriteTimeUtc(journalA, new DateTime(2026, 11, 25, 10, 0, 3, DateTimeKind.Utc));
        long prefixBytes = System.Text.Encoding.UTF8.GetByteCount(prefix);

        var monitor = new JournalDirectoryMonitor(
            [dir],
            "F13228986",
            new JournalSwitchResume(new JournalCheckpoint(journalA, prefixBytes))
        );
        JournalMonitorUpdate poll1 = await monitor.PollAsync();
        Assert.True(poll1.HasPendingLiveTail);
        Assert.False(poll1.IsCompanionDirectorySuperseded);
        Assert.Null(poll1.Cargo);

        // Commander B starts in the same folder and writes Cargo.json + Status.json before Poll 2 drains A's tail.
        string journalB = Path.Combine(dir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            journalB,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"Second\",\"FID\":\"F14004033\"}\n"
        );
        File.SetLastWriteTimeUtc(journalB, new DateTime(2026, 11, 25, 10, 5, 1, DateTimeKind.Utc));
        await File.WriteAllTextAsync(
            Path.Combine(dir, "Cargo.json"),
            """{"timestamp":"2026-11-25T10:05:02Z","event":"Cargo","Vessel":"Ship","Count":50,"Inventory":[{"Name":"steel","Name_Localised":"Steel","Count":50,"Stolen":0}]}"""
        );
        await File.WriteAllTextAsync(
            Path.Combine(dir, "Status.json"),
            """{"timestamp":"2026-11-25T10:05:02Z","event":"Status","Flags":1,"Flags2":0}"""
        );

        JournalMonitorUpdate poll2 = await monitor.PollAsync();
        Assert.False(poll2.HasPendingLiveTail);
        Assert.True(poll2.IsCompanionDirectorySuperseded);
        Assert.Null(poll2.Cargo);
        Assert.Null(poll2.Status);
        Assert.Equal("ScanOrganic", Assert.Single(poll2.JournalEvents).EventName);

        // 2. If a pending catch-up tail file OR queued catch-up session is deleted after selection,
        // monitoring must recover and advance to newer journals instead of wedging forever.
        string missingDir = Path.Combine(temporaryDirectory, "deleted-catchup-recovery");
        Directory.CreateDirectory(missingDir);
        var baseline = JournalBaselineSnapshot.Capture(
            [missingDir],
            new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero)
        );
        string session1 = Path.Combine(missingDir, "Journal.2026-11-25T100000.01.log");
        await File.WriteAllTextAsync(session1, prefix + tail);
        File.SetLastWriteTimeUtc(session1, new DateTime(2026, 11, 25, 10, 0, 3, DateTimeKind.Utc));

        string session2 = Path.Combine(missingDir, "Journal.2026-11-25T100500.01.log");
        await File.WriteAllTextAsync(
            session2,
            "{\"timestamp\":\"2026-11-25T10:05:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:01Z\",\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:02Z\",\"event\":\"LoadGame\",\"Commander\":\"First\",\"FID\":\"F13228986\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:05:03Z\",\"event\":\"Shutdown\"}\n"
        );
        File.SetLastWriteTimeUtc(session2, new DateTime(2026, 11, 25, 10, 5, 3, DateTimeKind.Utc));

        string session3 = Path.Combine(missingDir, "Journal.2026-11-25T101000.01.log");
        await File.WriteAllTextAsync(
            session3,
            "{\"timestamp\":\"2026-11-25T10:10:00Z\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}\n"
                + "{\"timestamp\":\"2026-11-25T10:10:01Z\",\"event\":\"Commander\",\"Name\":\"First\",\"FID\":\"F13228986\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:10:02Z\",\"event\":\"LoadGame\",\"Commander\":\"First\",\"FID\":\"F13228986\"}\n"
                + "{\"timestamp\":\"2026-11-25T10:10:03Z\",\"event\":\"FSDJump\",\"StarSystem\":\"RecoveredSystem\"}\n"
        );
        File.SetLastWriteTimeUtc(session3, new DateTime(2026, 11, 25, 10, 10, 3, DateTimeKind.Utc));

        var recoveryMonitor = new JournalDirectoryMonitor(
            [missingDir],
            "F13228986",
            new JournalSwitchResume(new JournalCheckpoint(session1, prefixBytes), baseline)
        );
        JournalMonitorUpdate s1Prefix = await recoveryMonitor.PollAsync();
        Assert.Equal(session1, s1Prefix.JournalPath);
        Assert.True(s1Prefix.HasPendingLiveTail);

        // Delete BOTH session1 (pending tail target) and session2 (queued catch-up session) before Poll 2!
        File.Delete(session1);
        File.Delete(session2);

        JournalMonitorUpdate recovered = await recoveryMonitor.PollAsync();
        Assert.Equal(session3, recovered.JournalPath);
        Assert.False(recovered.IsBootstrapRead);
        Assert.False(recovered.HasPendingLiveTail);
        Assert.Contains(recovered.JournalEvents, entry => entry.EventName == "FSDJump");
        Assert.NotEmpty(recovered.Errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
