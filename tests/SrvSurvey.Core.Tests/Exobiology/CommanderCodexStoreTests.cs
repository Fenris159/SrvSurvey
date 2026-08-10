using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CommanderCodexStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-CommanderCodex-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingLedgerReturnsCompatibleEmptyState()
    {
        var store = new CommanderCodexStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", "Cmdr Test");

        Assert.True(result.IsSuccess);
        Assert.False(result.Exists);
        Assert.Equal("F123", result.Data!.FrontierId);
        Assert.Equal("Cmdr Test", result.Data.CommanderName);
        Assert.Empty(result.Data.Firsts);
        Assert.EndsWith("F123-codex.json", result.Path);
    }

    [Fact]
    public async Task DiscoversOnlyGlobalCommanderLedgers()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F2-codex.json"),
            """{"fid":"F2","commander":"Zulu","codexFirsts":{}}""");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F1-codex.json"),
            """{"fid":"F1","commander":"Alpha","codexFirsts":{}}""");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F1-codex-18.json"),
            """{"fid":"F1","commander":"Alpha","codexFirsts":{}}""");
        var store = new CommanderCodexStore(temporaryDirectory);

        var result = await store.DiscoverCommandersAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["F1", "F2"],
            result.Commanders.Select(commander => commander.FrontierId));
        Assert.Equal("Alpha", result.Commanders[0].CommanderName);
    }

    [Fact]
    public async Task LoadsLegacyStringsAndIsolatesMalformedEntries()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-codex.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid":"F123",
              "commander":"Cmdr Legacy",
              "future":{"keep":true},
              "codexFirsts":{
                "2310101":"2022-08-31T05:38:57_669611992529_11",
                "bad":"not-a-first"
              }
            }
            """);
        var store = new CommanderCodexStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", "Cmdr Current");

        Assert.True(result.IsSuccess);
        Assert.True(result.Exists);
        Assert.Equal("Cmdr Legacy", result.Data!.CommanderName);
        var first = Assert.Single(result.Data.Firsts).Value;
        Assert.Equal(669611992529, first.SystemAddress);
        Assert.Equal(11, first.BodyId);
        Assert.Single(result.Warnings);
        Assert.True(result.Data.IsDiscovered(2310101));
        Assert.True(result.Data.IsPersonalFirst(2310101, 669611992529, 11));
        Assert.False(result.Data.IsPersonalFirst(2310101, 1, 2));
        Assert.True(result.Data.IsPersonalFirst(999, 1, 2));
    }

    [Fact]
    public async Task TrackingIsAtomicLosslessAndKeepsEarliestValidFirst()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-codex.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "future":{"keep":true},
              "codexFirsts":{
                "2310101":"2024-01-02T00:00:00_-1_-1"
              }
            }
            """);
        var store = new CommanderCodexStore(temporaryDirectory);
        var firstTime = new DateTimeOffset(
            2025,
            1,
            2,
            3,
            4,
            5,
            TimeSpan.Zero);

        var repaired = await store.TrackAsync(new CommanderCodexTrackRequest
        {
            FrontierId = "F123",
            CommanderName = "Cmdr Test",
            EntryId = 2310101,
            Timestamp = firstTime,
            SystemAddress = 42,
            BodyId = 7
        });
        var later = await store.TrackAsync(new CommanderCodexTrackRequest
        {
            FrontierId = "F123",
            CommanderName = "Cmdr Test",
            EntryId = 2310101,
            Timestamp = firstTime.AddDays(1),
            SystemAddress = 99,
            BodyId = 8
        });
        var earlier = await store.TrackAsync(new CommanderCodexTrackRequest
        {
            FrontierId = "F123",
            CommanderName = "Cmdr Test",
            EntryId = 2310101,
            Timestamp = firstTime.AddDays(-1),
            SystemAddress = 24,
            BodyId = 3
        });

        Assert.True(repaired.IsSuccess);
        Assert.True(repaired.Changed);
        Assert.True(later.IsSuccess);
        Assert.False(later.Changed);
        Assert.True(earlier.Changed);
        var loaded = await store.LoadAsync("F123", null);
        var first = Assert.Single(loaded.Data!.Firsts).Value;
        Assert.Equal(24, first.SystemAddress);
        Assert.Equal(3, first.BodyId);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["future"]!["keep"]!.GetValue<bool>());
        Assert.Equal("Cmdr Test", root["commander"]!.GetValue<string>());
        Assert.Empty(Directory.GetFiles(temporaryDirectory, "*.tmp-*"));
    }

    [Fact]
    public async Task RegionalLedgersUseLegacyFileNamesAndMetadata()
    {
        var store = new CommanderCodexStore(temporaryDirectory);

        var tracked = await store.TrackAsync(new CommanderCodexTrackRequest
        {
            FrontierId = "F123",
            CommanderName = "Cmdr Test",
            EntryId = 2310101,
            Timestamp = DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            SystemAddress = 42,
            BodyId = 1,
            RegionId = 18,
            RegionName = "Inner Orion Spur"
        });
        var loaded = await store.LoadAsync(
            "F123",
            "Cmdr Test",
            regionId: 18);

        Assert.True(tracked.IsSuccess);
        Assert.EndsWith("F123-codex-18.json", tracked.Path);
        Assert.Equal(18, loaded.Data!.RegionId);
        Assert.Equal("Inner Orion Spur", loaded.Data.RegionName);
        Assert.True(loaded.Data.IsDiscovered(2310101));
    }

    [Fact]
    public async Task ManualOverridesAreLosslessAndCannotRemoveJournalFirsts()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-codex.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "future":{"keep":true},
              "codexFirsts":{
                "2310101":"2024-01-02T00:00:00_42_7"
              }
            }
            """);
        var store = new CommanderCodexStore(temporaryDirectory);
        var timestamp = DateTimeOffset.Parse("2026-07-24T12:00:00Z");

        var added = await store.SetManualDiscoveryAsync(
            "F123",
            "Cmdr Test",
            2310206,
            true,
            timestamp);
        var protectedFirst = await store.SetManualDiscoveryAsync(
            "F123",
            "Cmdr Test",
            2310101,
            false);
        var removed = await store.SetManualDiscoveryAsync(
            "F123",
            "Cmdr Test",
            2310206,
            false);

        Assert.True(added.IsSuccess);
        Assert.True(added.Changed);
        Assert.True(protectedFirst.IsSuccess);
        Assert.False(protectedFirst.Changed);
        Assert.True(protectedFirst.IsDiscovered);
        Assert.True(removed.Changed);
        Assert.False(removed.IsDiscovered);
        var loaded = await store.LoadAsync("F123", null);
        var first = Assert.Single(loaded.Data!.Firsts).Value;
        Assert.Equal(42, first.SystemAddress);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["future"]!["keep"]!.GetValue<bool>());
        Assert.Equal("Cmdr Test", root["commander"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("folder/name")]
    [InlineData("")]
    public void RejectsUnsafeFrontierIds(string frontierId)
    {
        var store = new CommanderCodexStore(temporaryDirectory);

        Assert.Throws<ArgumentException>(() => store.ResolvePath(frontierId));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
