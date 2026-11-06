using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationProjectPreviewViewModelTests
{
    /// <summary>Raw Raven preparation metadata becomes counted, canonical layout names in both preview locations.</summary>
    [Theory]
    [InlineData("{\"alastor?\":1}", "1x Alastor-layout Military Installation")]
    [InlineData(
        "{\"apollo\":2,\" alastor? \":1,\"ALASTOR\":2}",
        "3x Alastor-layout Military Installation, 2x Apollo-layout Orbis Starport"
    )]
    [InlineData("{\"future_layout?\":2,\"vacuna\":0,\"alastor\":-1,\"\":1}", "2x future_layout")]
    public async Task LoadingPreviewShowsCountedBuildingsAndOmitsSiteMetadata(string preparation, string expected)
    {
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader((_, _) => Task.FromResult<ColonizationProjectPreviewData?>(LoadingPreviewData(preparation))),
            "loading"
        );
        await model.RefreshAsync();
        Assert.Equal(expected, model.Subtitle);
        Assert.Equal(
            ["Build ID", "Project name", "Build type", "Buildings:", "Architect", "Notes"],
            model.Details.Select(row => row.Label)
        );
        Assert.Equal(expected, Assert.Single(model.Details, row => row.Label == "Buildings:").Value);
        Assert.Equal("Fleet Carrier loading", Assert.Single(model.Details, row => row.Label == "Build type").Value);
        Assert.Equal("Example Cmdr", Assert.Single(model.Details, row => row.Label == "Architect").Value);
        Assert.Equal("Stage cargo before departure", Assert.Single(model.Details, row => row.Label == "Notes").Value);
    }

    /// <summary>Older loading projects retain a readable preview when their optional building selection is absent or empty.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"alastor\":0,\"apollo\":-1,\"?\":1}")]
    public async Task LoadingPreviewHandlesMissingBuildingSelection(string preparation)
    {
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader((_, _) => Task.FromResult<ColonizationProjectPreviewData?>(LoadingPreviewData(preparation))),
            "loading"
        );
        await model.RefreshAsync();
        Assert.Equal("Fleet Carrier loading", model.Subtitle);
        Assert.Equal(
            "Building list unavailable.",
            Assert.Single(model.Details, row => row.Label == "Buildings:").Value
        );
    }

    /// <summary>The loading-specific details do not remove site fields or change the subtitle of a normal build.</summary>
    [Fact]
    public async Task LoadingPreviewLeavesNormalBuildMetadataUnchanged()
    {
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader((_, _) => Task.FromResult<ColonizationProjectPreviewData?>(Data())),
            "build"
        );
        await model.RefreshAsync();
        Assert.Equal("Example system · Military Hub (ares)", model.Subtitle);
        Assert.Equal(
            [
                "Build ID",
                "Project name",
                "Build type",
                "System",
                "System address",
                "Body",
                "Architect",
                "Faction",
                "Notes",
            ],
            model.Details.Select(row => row.Label)
        );
    }

    /// <summary>Exercises the wire format rather than constructing already-decoded preparation fields.</summary>
    internal static ColonizationProjectPreviewData LoadingPreviewData(string preparation)
    {
        ColonizationProject project = JsonSerializer.Deserialize<ColonizationProject>(
            $$"""
            {
              "buildId":"loading", "buildName":"test loading", "buildType":"fc_loading",
              "maxNeed":10080, "sumNeed":10080, "commodities":{"steel":10080},
              "prepBuilds":{{preparation}}, "systemAddress":1,
              "architectName":"Example Cmdr", "notes":"Stage cargo before departure"
            }
            """
        )!;
        return new(project, [], new());
    }
}
