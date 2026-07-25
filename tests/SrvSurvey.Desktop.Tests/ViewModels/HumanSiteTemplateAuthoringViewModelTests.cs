using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class HumanSiteTemplateAuthoringViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-author-view-{Guid.NewGuid():N}");

    [Fact]
    public void LivePointsAndShieldTogglesBuildPreviewWithoutMutatingCatalog()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Templates[0];
        var previewChanges = 0;
        var viewModel = new HumanSiteTemplateAuthoringViewModel(
            catalog,
            () => previewChanges++);
        viewModel.UpdateContext(
            Site(template),
            new HumanSiteMapPoint(0, 0),
            currentRelativeHeading: 10,
            currentShieldsUp: false);
        viewModel.StartCommand.Execute(null);
        viewModel.BeginPolygonCommand.Execute(null);

        viewModel.UpdateContext(
            Site(template),
            new HumanSiteMapPoint(10, 0),
            currentRelativeHeading: 20,
            currentShieldsUp: true);
        viewModel.UpdateContext(
            Site(template),
            new HumanSiteMapPoint(10, 10),
            currentRelativeHeading: 30,
            currentShieldsUp: false);
        viewModel.EndPolygonCommand.Execute(null);
        viewModel.BuildingName = "QA Building";
        viewModel.CommitBuildingCommand.Execute(null);

        Assert.True(viewModel.IsAuthoring);
        Assert.Equal(template.Buildings.Count + 1, viewModel.BuildingCount);
        Assert.Equal(template.Buildings.Count, catalog.Templates[0].Buildings.Count);
        Assert.True(previewChanges >= 4);
    }

    [Fact]
    public void AddsPoiMetadataAtCurrentLiveOffset()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Templates[0];
        var viewModel = new HumanSiteTemplateAuthoringViewModel(
            catalog,
            () => { });
        viewModel.UpdateContext(
            Site(template),
            new HumanSiteMapPoint(1.5, -2.5),
            currentRelativeHeading: 270,
            currentShieldsUp: false);
        viewModel.StartCommand.Execute(null);
        viewModel.SecurityLevel = 3;
        viewModel.Floor = 2;
        viewModel.NamedPointName = "Battery";

        viewModel.AddNamedPointCommand.Execute(null);
        viewModel.AddDataTerminalCommand.Execute(null);
        viewModel.AddSecureDoorCommand.Execute(null);

        Assert.Equal(template.NamedPoints.Count + 1, viewModel.NamedPointCount);
        Assert.Equal(template.DataTerminals.Count + 1,
            viewModel.DataTerminalCount);
        var door = viewModel.PreviewTemplate!.SecureDoors[^1];
        Assert.Equal(new HumanSiteMapPoint(1.5, -2.5), door.Offset);
        Assert.Equal(270, door.Rotation);
        Assert.Equal(3, door.SecurityLevel);
        Assert.Equal(2, door.Floor);
    }

    [Fact]
    public async Task ExplicitExportWritesVerifiedDraftCatalog()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Templates[0];
        var viewModel = new HumanSiteTemplateAuthoringViewModel(
            catalog,
            () => { });
        viewModel.UpdateContext(
            Site(template),
            new HumanSiteMapPoint(1, 2),
            currentRelativeHeading: 0,
            currentShieldsUp: false);
        viewModel.StartCommand.Execute(null);
        viewModel.NamedPointName = "Exported Point";
        viewModel.AddNamedPointCommand.Execute(null);
        var path = Path.Combine(directory, "humanSiteTemplates.json");

        await viewModel.ExportAsync(path);

        await using var stream = File.OpenRead(path);
        var reloaded = HumanSiteTemplateCatalog.Load(stream);
        Assert.Equal("Exported Point", reloaded.Find(
            template.Economy,
            template.SubType)!.NamedPoints[^1].Name);
        Assert.Equal(path, viewModel.LastExportPath);
        Assert.Contains("verified", viewModel.StatusMessage);
    }

    [Fact]
    public void ActiveSiteChangeDiscardsOnlyInMemoryDraft()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var viewModel = new HumanSiteTemplateAuthoringViewModel(
            catalog,
            () => { });
        viewModel.UpdateContext(
            Site(catalog.Templates[0]),
            new HumanSiteMapPoint(0, 0),
            currentRelativeHeading: 0,
            currentShieldsUp: false);
        viewModel.StartCommand.Execute(null);

        viewModel.UpdateContext(
            Site(catalog.Templates[1]) with { MarketId = 2 },
            new HumanSiteMapPoint(0, 0),
            currentRelativeHeading: 0,
            currentShieldsUp: false);

        Assert.False(viewModel.IsAuthoring);
        Assert.Contains("discarded", viewModel.StatusMessage);
    }

    private static HumanSiteLiveSnapshot Site(HumanSiteTemplate template)
    {
        return new HumanSiteLiveSnapshot(
            Name: "Test Site",
            LocalizedName: "Test Site",
            MarketId: 1,
            SystemAddress: 42,
            BodyId: 7,
            BodyName: "Test Body",
            Location: new HumanSiteSurfaceLocation(0, 0),
            Economy: HumanSiteEconomy.Agriculture,
            EconomyToken: "$economy_Agri;",
            EconomyLocalized: "Agriculture",
            FactionName: string.Empty,
            FactionState: null,
            Government: string.Empty,
            GovernmentLocalized: string.Empty,
            Services: [],
            StationType: "OnFootSettlement",
            AvailablePads: HumanSiteLandingPads.From(template),
            SubType: template.SubType,
            Template: template,
            Heading: 0,
            Docking: HumanSiteDockingStatus.None,
            GrantedPad: 0,
            DockingDeniedReason: null,
            HasLanded: false,
            FirstApproached: DateTimeOffset.UtcNow,
            LastUpdated: DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
