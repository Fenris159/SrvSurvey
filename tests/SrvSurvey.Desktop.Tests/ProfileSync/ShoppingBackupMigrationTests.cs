using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.ProfileSync;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ShoppingBackupMigrationTests
{
    private const string FileName = "cross-platform-ui.json.shopping.json";
    private const string SearchKey = "config/cross-platform-ui.json.shopping.json#/PILOT/Search";
    private const string OtherMachine = "other-machine";
    private const string CurrentDocument =
        """{"PILOT":{"Search":{"SelectedCarrierMarketIds":[43],"WishList":[{"Commodity":"titanium","Quantity":9}]}}}""";

    [Theory]
    [InlineData("populated", 3L)]
    [InlineData("tombstone", 3L)]
    [InlineData("cleared", 4L)]
    [InlineData("atomic", 4L)]
    public void LegacyBaselineCanBeCapturedWithoutLosingSearchOrObservedChanges(string shape, long expectedCounter)
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        ProfileSnapshot legacy = LegacySnapshot(source, shape, parentFirst: true);
        byte[] original = legacy.Serialize();
        source.Store.SaveBaseline(legacy);
        source.Write("config", FileName, CurrentDocument);

        ProfileSnapshot captured = source.Store.Capture();
        target.Store.Apply(captured);

        AssertSearch(target);
        Assert.Equal(expectedCounter, captured.Portable[SearchKey].Clock[source.Store.Device.Id]);
        Assert.Equal(2, captured.Portable[SearchKey].Clock[OtherMachine]);
        Assert.Equal(original, legacy.Serialize());
    }

    [Fact]
    public void ExplicitRestoreNormalizesLegacySearchHistoryBeforeStagingAndRecapture()
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        ProfileSnapshot legacy = LegacySnapshot(source, "tombstone", parentFirst: false);
        byte[] original = legacy.Serialize();

        target.Store.StageExplicitRestore(legacy, restoreMachine: false);
        Assert.True(target.Store.ApplyPending());
        AssertSearch(target);
        ProfileSnapshot captured = target.Store.Capture();
        Assert.Equal(3, captured.Portable[SearchKey].Clock[source.Store.Device.Id]);
        Assert.Equal(2, captured.Portable[SearchKey].Clock[OtherMachine]);
        Assert.Equal(original, legacy.Serialize());
    }

    [Fact]
    public void LegacyWishlistEditRetainsItsCausalOrderWhenCarrierHasLargerCounter()
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        ProfileSnapshot earlier = LegacySnapshot(source, "tombstone", parentFirst: true);
        earlier.Portable[SearchKey + "/SelectedCarrierMarketIds"].Clock[source.Store.Device.Id] = 10;
        earlier.Portable[SearchKey + "/WishList"].Clock[source.Store.Device.Id] = 1;
        var later = ProfileSnapshot.Parse(earlier.Serialize());
        later.Portable[SearchKey + "/WishList"] = new(
            JsonNode.Parse("""[{"Commodity":"titanium","Quantity":12}]"""),
            false,
            new() { [source.Store.Device.Id] = 2 }
        );

        ProfileSnapshot local = ProfileSyncStore.ValidateScope(earlier);
        ProfileSnapshot remote = ProfileSyncStore.ValidateScope(later);
        SyncMergeResult merged = ProfileSyncMerge.Merge(local.Portable, remote.Portable);
        Assert.Empty(merged.Conflicts);
        target.Store.Apply(remote with { Portable = merged.Values });

        AssertSearch(target, quantity: 12);
    }

    [Fact]
    public void ConflictingWholeAndLiveLeafSearchValuesLeaveLocalSearchIntact()
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        target.Write("config", FileName, CurrentDocument);
        ProfileSnapshot mixed = LegacySnapshot(source, "atomic", parentFirst: true);
        mixed.Portable[SearchKey + "/WishList"] = new(
            JsonNode.Parse("""[{"Commodity":"titanium","Quantity":12}]"""),
            false,
            new() { [source.Store.Device.Id] = 5 }
        );
        byte[] original = mixed.Serialize();

        Assert.Throws<InvalidDataException>(() => target.Store.Apply(mixed));

        AssertSearch(target);
        Assert.Equal(original, mixed.Serialize());
    }

    private static ProfileSnapshot LegacySnapshot(ProfileSyncTestRoot source, string shape, bool parentFirst)
    {
        string device = source.Store.Device.Id;
        bool cleared = shape == "cleared";
        bool atomic = shape == "atomic";
        bool descendantsDeleted = cleared || atomic;
        int wishlistCounter = shape switch
        {
            "atomic" => 4,
            "cleared" => 2,
            _ => 3,
        };
        var values = new Dictionary<string, SyncValue>(StringComparer.Ordinal);
        var parent = new SyncValue(
            atomic ? JsonNode.Parse(CurrentDocument)!["PILOT"]!["Search"]!.DeepClone() : null,
            shape == "tombstone",
            new() { [device] = descendantsDeleted ? 3 : 2 }
        );
        if (parentFirst && shape != "populated")
        {
            values.Add(SearchKey, parent);
        }
        values.Add(
            SearchKey + "/SelectedCarrierMarketIds",
            new(
                descendantsDeleted ? null : JsonNode.Parse("[43]"),
                descendantsDeleted,
                new() { [device] = descendantsDeleted ? 2 : 1, [OtherMachine] = 2 }
            )
        );
        values.Add(
            SearchKey + "/WishList",
            new(
                descendantsDeleted ? null : JsonNode.Parse("""[{"Commodity":"titanium","Quantity":9}]"""),
                descendantsDeleted,
                new() { [device] = wishlistCounter }
            )
        );
        if (!parentFirst && shape != "populated")
        {
            values.Add(SearchKey, parent);
        }
        return source.Store.Capture() with { SchemaVersion = 1, Portable = values };
    }

    private static void AssertSearch(ProfileSyncTestRoot target, int quantity = 9)
    {
        string file = target.Path("config", FileName);
        Assert.True(File.Exists(file), "The restored shopping search must remain on disk.");
        JsonNode saved = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(43, saved["PILOT"]!["Search"]!["SelectedCarrierMarketIds"]![0]!.GetValue<int>());
        Assert.Equal("titanium", saved["PILOT"]!["Search"]!["WishList"]![0]!["Commodity"]!.GetValue<string>());
        Assert.Equal(quantity, saved["PILOT"]!["Search"]!["WishList"]![0]!["Quantity"]!.GetValue<int>());
    }
}
