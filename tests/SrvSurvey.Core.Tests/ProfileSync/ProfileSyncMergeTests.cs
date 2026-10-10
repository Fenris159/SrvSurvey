using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;
using Xunit;

namespace SrvSurvey.Core.Tests.ProfileSync;

public sealed class ProfileSyncMergeTests
{
    [Fact]
    public void CaptureTracksIndependentChangesAndDeletesWithoutChangingUneditedCounters()
    {
        Dictionary<string, SyncValue> baseline = ProfileSyncMerge.Capture(
            "A",
            new Dictionary<string, JsonNode?> { ["theme"] = "blue", ["bookmark"] = 1 },
            new Dictionary<string, SyncValue>()
        );
        Dictionary<string, SyncValue> current = ProfileSyncMerge.Capture(
            "A",
            new Dictionary<string, JsonNode?> { ["theme"] = "blue", ["search"] = "Sol" },
            baseline
        );
        Assert.Equal(1, current["theme"].Clock["A"]);
        Assert.True(current["bookmark"].Deleted);
        Assert.Equal(2, current["bookmark"].Clock["A"]);
        Assert.Equal("Sol", current["search"].Value!.GetValue<string>());
        Dictionary<string, SyncValue> again = ProfileSyncMerge.Capture(
            "A",
            new Dictionary<string, JsonNode?> { ["theme"] = "blue", ["search"] = "Sol" },
            current
        );
        Assert.Equal(2, again["bookmark"].Clock["A"]);
    }

    [Fact]
    public void SequentialMachineChangesSupersedeEarlierValuesIncludingDeletion()
    {
        Dictionary<string, SyncValue> a = ProfileSyncMerge.Capture(
            "A",
            new Dictionary<string, JsonNode?> { ["theme"] = "blue" },
            new Dictionary<string, SyncValue>()
        );
        Dictionary<string, SyncValue> b = ProfileSyncMerge.Capture(
            "B",
            new Dictionary<string, JsonNode?> { ["theme"] = "gold" },
            a
        );
        SyncMergeResult result = ProfileSyncMerge.Merge(a, b);
        Assert.Empty(result.Conflicts);
        Assert.Equal("gold", result.Values["theme"].Value!.GetValue<string>());
        Assert.Empty(ProfileSyncMerge.Merge(b, a).Conflicts);
        Dictionary<string, SyncValue> deleted = ProfileSyncMerge.Capture("A", new Dictionary<string, JsonNode?>(), b);
        Assert.True(ProfileSyncMerge.Merge(b, deleted).Values["theme"].Deleted);
    }

    [Fact]
    public void IndependentSettingsMergeAndConcurrentEqualValuesCombineTheirCounters()
    {
        var a = new Dictionary<string, SyncValue> { ["theme"] = Value("blue", "A"), ["route"] = Value("Sol", "A") };
        var b = new Dictionary<string, SyncValue>
        {
            ["theme"] = Value("blue", "B"),
            ["search"] = Value("Achenar", "B"),
        };
        SyncMergeResult merged = ProfileSyncMerge.Merge(a, b);
        Assert.Empty(merged.Conflicts);
        Assert.Equal(3, merged.Values.Count);
        Assert.Equal(2, merged.Values["theme"].Clock.Count);
        Assert.Single(a["theme"].Clock);
    }

    [Theory]
    [InlineData(false, "blue")]
    [InlineData(true, "gold")]
    public void ConflictingValuesRequireAChoiceThatSupersedesBothVersions(bool remote, string expected)
    {
        var a = new Dictionary<string, SyncValue> { ["theme"] = Value("blue", "A") };
        var b = new Dictionary<string, SyncValue> { ["theme"] = Value("gold", "B") };
        SyncMergeResult merge = ProfileSyncMerge.Merge(a, b);
        SyncConflict conflict = Assert.Single(merge.Conflicts);
        Assert.Equal("blue", merge.Values["theme"].Value!.GetValue<string>());
        SyncValue chosen = ProfileSyncMerge.Resolve("A", conflict, remote);
        Assert.Equal(expected, chosen.Value!.GetValue<string>());
        Assert.Equal(2, chosen.Clock["A"]);
        Assert.True(chosen.Includes(conflict.Local));
        Assert.True(chosen.Includes(conflict.Remote));
    }

    [Fact]
    public void ConcurrentDeletionDoesNotSilentlyDiscardAnEdit()
    {
        var baseValues = new Dictionary<string, SyncValue> { ["bookmark"] = Value("Sol", "A") };
        Dictionary<string, SyncValue> removed = ProfileSyncMerge.Capture(
            "A",
            new Dictionary<string, JsonNode?>(),
            baseValues
        );
        Dictionary<string, SyncValue> edited = ProfileSyncMerge.Capture(
            "B",
            new Dictionary<string, JsonNode?> { ["bookmark"] = "Achenar" },
            baseValues
        );
        Assert.Single(ProfileSyncMerge.Merge(removed, edited).Conflicts);
    }

    [Fact]
    public void ReusedCountersForDifferentValuesAreRejected()
    {
        var a = new Dictionary<string, SyncValue> { ["theme"] = Value("blue", "A") };
        var b = new Dictionary<string, SyncValue> { ["theme"] = Value("gold", "A") };
        Assert.Throws<InvalidDataException>(() => ProfileSyncMerge.Merge(a, b));
    }

    [Fact]
    public void SnapshotRoundTripsAndMalformedPayloadsAreRejected()
    {
        var snapshot = new ProfileSnapshot(
            1,
            "A",
            "Desktop",
            "Linux",
            DateTimeOffset.UtcNow,
            new() { ["theme"] = Value("gold", "A") },
            new() { ["monitor"] = "DP-1" },
            new string('a', 64)
        );
        var parsed = ProfileSnapshot.Parse(snapshot.Serialize());
        Assert.Equal("gold", parsed.Portable["theme"].Value!.GetValue<string>());
        Assert.Equal("DP-1", parsed.Machine["monitor"]!.GetValue<string>());
        Assert.Equal(snapshot.MachineFingerprint, parsed.MachineFingerprint);
        Assert.Throws<InvalidDataException>(() => ProfileSnapshot.Parse(Encoding.UTF8.GetBytes("null")));
        Assert.Throws<InvalidDataException>(() => ProfileSnapshot.Parse(Encoding.UTF8.GetBytes("bad json")));
        Assert.Throws<InvalidDataException>(() => ProfileSnapshot.Parse(new byte[ProfileSnapshot.MaximumBytes + 1]));
        Assert.Throws<ArgumentNullException>(() => ProfileSnapshot.Parse(null!));
    }

    [Theory]
    [InlineData("SchemaVersion", "2")]
    [InlineData("DeviceId", "\"\"")]
    [InlineData("DeviceName", "null")]
    [InlineData("Platform", "\"invalid\"")]
    [InlineData("MachineFingerprint", "null")]
    [InlineData("MachineFingerprint", "\"raw-machine-id\"")]
    [InlineData("MachineFingerprint", "\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"")]
    [InlineData("Portable", "null")]
    [InlineData("Machine", "null")]
    [InlineData("Portable", "{\"x\":null}")]
    [InlineData("Portable", "{\"x\":{\"Clock\":null}}")]
    [InlineData("Portable", "{\"x\":{\"Clock\":{}}}")]
    [InlineData("Portable", "{\"x\":{\"Clock\":{\"A\":0}}}")]
    [InlineData("Portable", "{\"x\":{\"Clock\":{\"\":1}}}")]
    public void SnapshotRejectsInvalidSchemaAndCounters(string property, string value)
    {
        JsonObject root = JsonNode
            .Parse(
                "{\"SchemaVersion\":1,\"DeviceId\":\"A\",\"DeviceName\":\"Desktop\",\"Platform\":\"Linux\",\"Portable\":{},\"Machine\":{}}"
            )!
            .AsObject();
        root[property] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => ProfileSnapshot.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    private static SyncValue Value(string value, string device) =>
        new(JsonValue.Create(value), false, new() { [device] = 1 });
}
