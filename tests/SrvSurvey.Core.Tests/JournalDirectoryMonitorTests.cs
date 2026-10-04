using System.Text;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests;

public sealed class JournalDirectoryMonitorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journal-monitor-tests-{Guid.NewGuid():N}"
    );

    /// <summary>Checks separate commander monitors stay on their Steam or Epic source and never replay alias history.</summary>
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

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
