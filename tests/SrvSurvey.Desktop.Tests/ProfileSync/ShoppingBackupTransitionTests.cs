using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ShoppingBackupTransitionTests
{
    [Theory]
    [InlineData("cross-platform-ui.json.shopping.json")]
    [InlineData("commanders/F123/cross-platform-ui.json.shopping.json")]
    public void ReopenedSearchSurvivesSuccessiveBackupsAfterClearingProjectTags(string file)
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        source.Write(
            "config",
            file,
            """{"Version":1,"PILOT":{"ProjectIds":["first"],"Search":{"SelectedCarrierMarketIds":[42],"WishList":[{"Commodity":"steel","Quantity":12}]}}}"""
        );
        ProfileSnapshot initial = source.Store.Capture();
        source.Store.SaveBaseline(initial);
        target.Store.Apply(initial);

        source.Write("config", file, """{"Version":1,"PILOT":{"ProjectIds":[],"Search":null}}""");
        ProfileSnapshot cleared = source.Store.Capture();
        source.Store.SaveBaseline(cleared);
        target.Store.Apply(cleared);
        Assert.Null(JsonNode.Parse(File.ReadAllText(target.Path("config", file)))!["PILOT"]!["Search"]);

        source.Write(
            "config",
            file,
            """{"Version":1,"PILOT":{"ProjectIds":["latest"],"Search":{"SelectedCarrierMarketIds":[43],"WishList":[{"Commodity":"titanium","Quantity":9}]}}}"""
        );
        ProfileSnapshot reopened = source.Store.Capture();
        source.Store.SaveBaseline(reopened);
        target.Store.Apply(reopened);
        JsonNode restored = JsonNode.Parse(File.ReadAllText(target.Path("config", file)))!;
        Assert.Equal("latest", restored["PILOT"]!["ProjectIds"]![0]!.GetValue<string>());
        Assert.NotNull(restored["PILOT"]!["Search"]);
        Assert.Equal(43, restored["PILOT"]!["Search"]!["SelectedCarrierMarketIds"]![0]!.GetValue<int>());
        Assert.Equal("titanium", restored["PILOT"]!["Search"]!["WishList"]![0]!["Commodity"]!.GetValue<string>());
        Assert.Equal(9, restored["PILOT"]!["Search"]!["WishList"]![0]!["Quantity"]!.GetValue<int>());
    }
}
