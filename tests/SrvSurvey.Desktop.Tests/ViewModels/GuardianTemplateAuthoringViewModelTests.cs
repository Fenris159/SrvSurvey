using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianTemplateAuthoringViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-template-vm-{Guid.NewGuid():N}");

    [Fact]
    public async Task AuthorsPreviewsAndExportsSelectedTemplate()
    {
        var template = CreateTemplate("Test");
        var catalog = new GuardianSiteTemplateCatalog([template]);
        var catalogChanges = new List<bool>();
        var viewModel = new GuardianTemplateAuthoringViewModel(
            catalog,
            catalogChanges.Add);
        viewModel.UpdateContext(
            template,
            new GuardianSurveyMeasurement(25, 45, 90));

        viewModel.StartCommand.Execute(null);
        viewModel.TemplateName = "Edited";
        viewModel.BackgroundImage = "edited.png";
        viewModel.ImageOffsetX = 12;
        viewModel.ImageOffsetY = 34;
        viewModel.ScaleFactor = 1.5m;
        viewModel.ApplyMetadataCommand.Execute(null);
        viewModel.NewPointName = "qa1";
        viewModel.NewPointType = GuardianPoiType.Orb;
        viewModel.NewPointName = "qa1";
        viewModel.AddMeasuredPointCommand.Execute(null);
        viewModel.GroupName = "B";
        viewModel.GroupAngle = 180;
        viewModel.GroupDistance = 50;
        viewModel.SetGroupCommand.Execute(null);

        Assert.True(viewModel.IsAuthoring);
        Assert.Equal("Edited", viewModel.PreviewTemplate?.Name);
        Assert.Contains(viewModel.Points, point => point.Name == "qa1");
        Assert.Contains(viewModel.Groups, group => group.Name == "B");
        Assert.Contains(false, catalogChanges);

        var path = Path.Combine(directory, "guardianSiteTemplates.json");
        await viewModel.ExportAsync(path);

        Assert.Equal(path, viewModel.LastExportPath);
        Assert.True(catalogChanges[^1]);
        await using var stream = File.OpenRead(path);
        var exported = GuardianSiteTemplateCatalog.Load(stream).Find("Test")!;
        Assert.Equal("Edited", exported.Name);
        Assert.Equal("edited.png", exported.BackgroundImage);
        Assert.Equal(new GuardianMapPoint(12, 34), exported.ImageOffset);
        Assert.Equal(1.5, exported.ScaleFactor);
        var point = exported.PointsOfInterest.Single(item => item.Name == "qa1");
        Assert.Equal(25, point.Distance);
        Assert.Equal(45, point.Angle);
        Assert.Equal(90, point.Rotation);
        Assert.Equal(new GuardianMapPoint(180, 50), exported.ObeliskGroupNameLocations["B"]);
    }

    [Fact]
    public void ChangingSiteTypeDiscardsUnexportedDraft()
    {
        var first = CreateTemplate("First");
        var second = CreateTemplate("Second");
        var viewModel = new GuardianTemplateAuthoringViewModel(
            new GuardianSiteTemplateCatalog([first, second]),
            _ => { });
        viewModel.UpdateContext(first, measurement: null);
        viewModel.StartCommand.Execute(null);
        viewModel.TemplateName = "Unexported";
        viewModel.ApplyMetadataCommand.Execute(null);

        viewModel.UpdateContext(second, measurement: null);

        Assert.False(viewModel.IsAuthoring);
        Assert.Equal("Second · Original", viewModel.TemplateTitle);
        Assert.Contains("discarded", viewModel.StatusMessage);
    }

    [Fact]
    public void MapSelectionCarriesIntoCoordinateDraftAndUpdatesPreview()
    {
        var template = CreateTemplate("Test");
        var viewModel = new GuardianTemplateAuthoringViewModel(
            new GuardianSiteTemplateCatalog([template]),
            _ => { });
        viewModel.UpdateContext(template, measurement: null);

        Assert.False(viewModel.HasSelectedPoint);
        viewModel.SelectPoint("p2");
        viewModel.StartCommand.Execute(null);

        Assert.True(viewModel.HasSelectedPoint);
        Assert.Equal("p2", viewModel.SelectedPoint?.Name);
        viewModel.PointName = "p2-edited";
        viewModel.PointAngle += 0.1m;
        viewModel.PointDistance += 0.1m;
        viewModel.PointRotation += 0.1m;

        var livePreview = viewModel.PreviewTemplate!.PointsOfInterest.Single(
            point => point.Name == "p2-edited");
        Assert.Equal(90.1, livePreview.Angle);
        Assert.Equal(20.1, livePreview.Distance);
        Assert.Equal(45.1, livePreview.Rotation);

        viewModel.SelectPoint("p1");
        Assert.Contains(
            viewModel.PreviewTemplate!.PointsOfInterest,
            point => point.Name == "p2");
        Assert.DoesNotContain(
            viewModel.PreviewTemplate.PointsOfInterest,
            point => point.Name == "p2-edited");

        viewModel.SelectPoint("p2");
        viewModel.PointName = "p2-edited";
        viewModel.PointAngle += 0.1m;
        viewModel.PointDistance += 0.1m;
        viewModel.PointRotation += 0.1m;
        viewModel.ApplySelectedPointCommand.Execute(null);

        var updated = viewModel.PreviewTemplate!.PointsOfInterest.Single(point =>
            point.Name == "p2-edited");
        Assert.Equal("p2-edited", viewModel.SelectedPoint?.Name);
        Assert.Equal(90.1, updated.Angle);
        Assert.Equal(20.1, updated.Distance);
        Assert.Equal(45.1, updated.Rotation);

        viewModel.SelectPoint("not-a-template-point");
        Assert.False(viewModel.HasSelectedPoint);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GuardianSiteTemplate CreateTemplate(string siteType)
    {
        return new GuardianSiteTemplate(
            siteType,
            "Original",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "p1",
                    GuardianPoiType.Orb,
                    0,
                    10,
                    0),
                new GuardianPointOfInterest(
                    "p2",
                    GuardianPoiType.BrokenObelisk,
                    90,
                    20,
                    45),
            ],
            [],
            new Dictionary<string, GuardianMapPoint>
            {
                ["A"] = new GuardianMapPoint(0, 20),
            });
    }
}
