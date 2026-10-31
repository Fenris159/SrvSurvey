using System.Diagnostics;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed partial class JourneyUpgradeRecoveryTests
{
    [Fact]
    public async Task ReviewRecoveryCompareAndReplaceIsExclusiveAcrossProcesses()
    {
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "original");
        await using var lease = new FileStream(
            Profiles.GetProfilePath("F123", true) + ".lease",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using Process first = StartProfileSelectionWorker("first");
        using Process second = StartProfileSelectionWorker("second");
        Task<string>[] errors = [first.StandardError.ReadToEndAsync(), second.StandardError.ReadToEndAsync()];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            while (
                !File.Exists(Path.Combine(DataDirectory, "first.ready"))
                || !File.Exists(Path.Combine(DataDirectory, "second.ready"))
            )
            {
                Assert.False(first.HasExited || second.HasExited);
                await Task.Delay(20, deadline.Token);
            }
            await File.WriteAllTextAsync(Path.Combine(DataDirectory, "go"), string.Empty, deadline.Token);
            await Task.Delay(100, deadline.Token);
            Assert.False(first.HasExited || second.HasExited);
            await lease.DisposeAsync();
            await Task.WhenAll(first.WaitForExitAsync(deadline.Token), second.WaitForExitAsync(deadline.Token));
            Assert.True(first.ExitCode is 0 or 10);
            Assert.True(second.ExitCode is 0 or 10);
            Assert.NotEqual(first.ExitCode, second.ExitCode);
            Assert.Equal(
                first.ExitCode == 0 ? "first" : "second",
                (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName
            );
        }
        finally
        {
            foreach (Process worker in new[] { first, second })
            {
                if (!worker.HasExited)
                {
                    worker.Kill(entireProcessTree: true);
                }
                await worker.WaitForExitAsync();
            }
        }
        Assert.All(await Task.WhenAll(errors), Assert.Empty);
    }

    private Process StartProfileSelectionWorker(string name)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardError = true };
        info.ArgumentList.Add(typeof(JourneyUpgradeRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("--journey-profile-cas-worker");
        info.ArgumentList.Add(DataDirectory);
        info.ArgumentList.Add(name);
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the profile writer.");
    }

    internal static async Task<int> RunProfileSelectionWorkerAsync(string folder, string replacement)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await File.WriteAllTextAsync(Path.Combine(folder, replacement + ".ready"), string.Empty, deadline.Token);
        while (!File.Exists(Path.Combine(folder, "go")))
        {
            await Task.Delay(10, deadline.Token);
        }
        bool replaced = await new CommanderProfileStore(folder).TryReplaceActiveJourneyAsync(
            "F123",
            true,
            "original",
            replacement,
            deadline.Token
        );
        return replaced ? 0 : 10;
    }

    [Fact]
    public async Task ReviewRecoveryAWaitingWriterCanBeCancelledWithoutChangingTheProfile()
    {
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "original");
        await using var lease = new FileStream(
            Profiles.GetProfilePath("F123", true) + ".lease",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CommanderProfileStore(DataDirectory).SaveActiveJourneyAsync(
                "F123",
                "Commander",
                true,
                "replacement",
                deadline.Token
            )
        );
        Assert.Equal("original", (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
    }

    [Fact]
    public async Task ReviewRecoveryUnavailableCoordinationKeepsReadableHistoryAndOriginalBytes()
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        string original = await File.ReadAllTextAsync(path);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, ".journey.lease"));

        JourneyCatalogResult catalog = await CreateService().LoadAllAsync("F123");

        Assert.Equal("Original expedition", Assert.Single(catalog.Journeys).Name);
        Assert.Contains(
            catalog.Errors,
            error => error.Contains("format upgrade could not be saved", StringComparison.Ordinal)
        );
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".pre-upgrade.bak"));
    }

    [Fact]
    public async Task ReviewRecoveryCoordinationErrorsAreNotRetriedAsLockContention()
    {
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "original");
        string path = Profiles.GetProfilePath("F123", true);
        string original = await File.ReadAllTextAsync(path);
        File.Delete(path + ".lease");
        Directory.CreateDirectory(path + ".lease");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "replacement")
        );

        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ReviewRecoveryBeginDoesNotReplaceAConcurrentSelection()
    {
        await WriteJournalAsync("F123");
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, null);
        JourneyService service = CreateService(message =>
        {
            if (message.StartsWith("Journeys: resolve;", StringComparison.Ordinal))
            {
                JsonObject profile = JsonNode
                    .Parse(File.ReadAllText(Profiles.GetProfilePath("F123", true)))!
                    .AsObject();
                profile["activeJourney"] = "concurrent-selection";
                File.WriteAllText(Profiles.GetProfilePath("F123", true), profile.ToJsonString());
            }
        });
        JourneyJournalSystemSearchResult start = await service.FindCurrentStartAsync("F123", true, 42);

        JourneyServiceResult result = await service.BeginAsync(new("F123", "Commander", true, "New", "", start.Entry!));

        Assert.Equal("concurrent-selection", (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.Null(service.ActiveJourney);
        Assert.Contains(result.Errors, error => error.Contains("selection changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewRecoveryRechecksARestoredFileBeforeDetachingItsSelection()
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        string original = await File.ReadAllTextAsync(path);
        File.Delete(path);
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        JourneyService service = CreateService(message =>
        {
            if (message.StartsWith("Journeys: recheck-missing;", StringComparison.Ordinal))
            {
                File.WriteAllText(path, original);
            }
        });

        JourneyServiceResult result = await service.InitializeActiveAsync("F123", true);

        Assert.Equal(LegacyName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.Equal(LegacyName, result.Journey!.FileName);
        Assert.Equal(3, Assert.Single(result.Journey.VisitedSystems).Counts.Screenshots);
    }

    [Fact]
    public async Task ReviewRecoveryClearingAnotherFileKeepsTheCurrentProcessorRecording()
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        await File.WriteAllTextAsync(path, "{broken");
        await WriteJournalAsync("F123");
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        JourneyService service = CreateService();
        JourneyRecoveryReference unavailable = (await service.InitializeActiveAsync("F123", true)).Recovery!;
        Assert.True(await service.ClearUnavailableJourneyAsync(unavailable));
        JourneyJournalSystemSearchResult start = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyDocument current = (
            await service.BeginAsync(new("F123", "Commander", true, "Current", "", start.Entry!))
        ).Journey!;
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);

        Assert.True(await service.ClearUnavailableJourneyAsync(unavailable));
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}""",
                out JournalEventEnvelope? parsed,
                out string? error
            ),
            error
        );
        JourneyServiceResult applied = await service.ApplyLiveAsync([Assert.IsType<JournalEventEnvelope>(parsed)]);

        Assert.Equal(current.FileName, service.ActiveJourney!.FileName);
        Assert.Equal(1, applied.ProcessedEventCount);
        Assert.Equal(1, service.ActiveJourney.CurrentSystem!.Counts.Screenshots);
    }

    [Fact]
    public async Task ReviewRecoveryConcurrentCreatorsKeepEveryJourney()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-01T00:05:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Task<JourneyDocument>[] creations = Enumerable
            .Range(0, 24)
            .Select(index =>
                new JourneyStore(DataDirectory).CreateAsync(
                    new("F123", "Commander", "Journey " + index, "", StartingJournal, timestamp)
                )
            )
            .ToArray();

        JourneyDocument[] journeys = await Task.WhenAll(creations);

        Assert.Equal(
            journeys.Length,
            journeys.Select(journey => journey.FileName).Distinct(StringComparer.Ordinal).Count()
        );
        JourneyCatalogResult catalog = await new JourneyStore(DataDirectory).LoadAllAsync("F123");
        Assert.Equal(journeys.Length, catalog.Journeys.Count);
        Assert.Equal(
            journeys.Select(journey => journey.Name).Order(StringComparer.Ordinal),
            catalog.Journeys.Select(journey => journey.Name).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task ReviewRecoveryOnlyOneStoreCanReplaceTheSameSelection()
    {
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "original");
        Task<bool>[] replacements = Enumerable
            .Range(0, 24)
            .Select(index =>
                new CommanderProfileStore(DataDirectory).TryReplaceActiveJourneyAsync(
                    "F123",
                    true,
                    "original",
                    "selection-" + index,
                    CancellationToken.None
                )
            )
            .ToArray();

        bool[] results = await Task.WhenAll(replacements);

        Assert.Single(results, succeeded => succeeded);
    }

    [Fact]
    public async Task ReviewRecoveryUpgradeWaitsForAWriterAndPreservesItsLatestCounts()
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        string leasePath = Path.Combine(Path.GetDirectoryName(path)!, ".journey.lease");
        await using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Task<JourneyCatalogResult> upgrade = CreateService().LoadAllAsync("F123");
        await Task.Delay(75);
        Assert.False(upgrade.IsCompleted);
        JsonObject latest = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        latest["visitedSystems"]![0]!["count"]!["screenshots"] = 99;
        string updatePath = path + ".writer.tmp";
        await File.WriteAllTextAsync(updatePath, latest.ToJsonString());
        File.Move(updatePath, path, overwrite: true);
        await lease.DisposeAsync();

        JourneyCatalogResult upgraded = await upgrade.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(99, Assert.Single(Assert.Single(upgraded.Journeys).VisitedSystems).Counts.Screenshots);
        JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(99, saved["visitedSystems"]![0]!["count"]!["screenshots"]!.GetValue<int>());
        Assert.Equal(1, saved["schemaVersion"]!.GetValue<int>());
    }
}
