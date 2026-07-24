using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianViewModelTests
{
    [Fact]
    public void FiltersAllLegacyGuardianReferenceFields()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);

            Assert.Equal(759, viewModel.Rows.Count);
            Assert.Contains("759 of 759", viewModel.Summary);

            viewModel.SelectedKindFilter = "Ruins";
            viewModel.SelectedSiteTypeFilter = "Beta";
            viewModel.FilterText = "Synuefe";

            Assert.NotEmpty(viewModel.Rows);
            Assert.All(
                viewModel.Rows,
                row =>
                {
                    Assert.Equal(GuardianSiteKind.Ruins, row.Reference.Kind);
                    Assert.Equal("Beta", row.Reference.SiteType);
                    Assert.Contains(
                        "Synuefe",
                        row.Reference.SystemName,
                        StringComparison.OrdinalIgnoreCase);
                });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CurrentPositionReordersRowsByGalacticDistance()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var viewModel = new GuardianViewModel(root);
            var target = viewModel.Rows.Single(
                row => row.Reference.Kind == GuardianSiteKind.Ruins
                    && row.Reference.SiteId == 1);

            viewModel.UpdateCurrentSystem("GR 1 system", target.Reference.Position);

            Assert.Equal(target.Reference, viewModel.Rows[0].Reference);
            Assert.Equal(0, viewModel.Rows[0].Distance);
            Assert.Contains("GR 1 system", viewModel.OriginStatus);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadsCommanderVisitsAndCopiesSelectedSiteFields()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var folder = Path.Combine(root, "guardian", "F123");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "site-ruins-1.json"),
                """
                {
                  "firstVisited":"2026-07-01T10:00:00Z",
                  "lastVisited":"2026-07-02T10:00:00Z",
                  "type":"Beta","index":1,
                  "systemAddress":3515254557027,
                  "systemName":"Synuefe XR-H d11-102",
                  "bodyId":13,
                  "bodyName":"Synuefe XR-H d11-102 1 b",
                  "siteHeading":332,"relicTowerHeading":93,
                  "location":{"lat":-46.576923,"long":133.985107},
                  "notes":"commander note"
                }
                """);
            var viewModel = new GuardianViewModel(root);

            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            viewModel.SelectedVisitFilter = "Visited";

            var row = Assert.Single(viewModel.Rows);
            Assert.True(row.Visit.IsVisited);
            Assert.Equal("commander note", row.Notes);
            Assert.Contains("1 site survey file", viewModel.StatusMessage);

            string? copied = null;
            viewModel.SetClipboardWriter(text =>
            {
                copied = text;
                return Task.CompletedTask;
            });
            await viewModel.CopySystemAddressAsync();

            Assert.Equal("3515254557027", copied);
            Assert.Contains("Copied system address", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SurfaceCopyReportsUnavailableBeaconCoordinates()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var catalog = new GuardianSiteCatalog(
            [
                new GuardianSiteReference(
                    0,
                    GuardianSiteKind.Beacon,
                    "Test",
                    1,
                    "A 1",
                    2,
                    "Beacon",
                    0,
                    100,
                    new GalacticCoordinate(0, 0, 0),
                    null,
                    null,
                    -1,
                    -1,
                    0,
                    null,
                    null,
                    null),
            ]);
            var viewModel = new GuardianViewModel(
                root,
                catalog,
                new GuardianPublishedSiteCatalog([]),
                new GuardianSiteTemplateCatalog([]));

            viewModel.SetClipboardWriter(_ => Task.CompletedTask);
            await viewModel.CopySurfaceLocationAsync();

            Assert.Contains("not available", viewModel.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-vm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
