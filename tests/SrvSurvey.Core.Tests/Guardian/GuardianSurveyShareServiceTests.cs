using System.IO.Compression;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianSurveyShareServiceTests : IDisposable
{
    private const string FrontierId = "F123";
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-share-{Guid.NewGuid():N}");

    [Fact]
    public void DiscoveryReasonsMatchLegacyComparisonRules()
    {
        var published = Published(
            siteHeading: -1,
            poiStatuses: new Dictionary<string, GuardianPoiStatus>
            {
                ["P1"] = GuardianPoiStatus.Present,
            });
        var service = new GuardianSurveyShareService(
            temporaryDirectory,
            new GuardianPublishedSiteCatalog([published]));
        var survey = Survey(
            Path.Combine(temporaryDirectory, "unused.json"),
            new GuardianSurveyData
            {
                SiteType = "Alpha",
                SiteHeading = 123,
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["P1"] = GuardianPoiStatus.Absent,
                },
                RawPointsOfInterest =
                [
                    new GuardianPointOfInterest(
                        "New",
                        GuardianPoiType.Pylon,
                        1,
                        2,
                        3),
                ],
            });

        var reasons = service.GetDiscoveryReasons(survey);

        Assert.Contains("Raw points of interest", reasons);
        Assert.Contains("Site heading", reasons);
        Assert.Contains("Point-of-interest status", reasons);
    }

    [Fact]
    public async Task PrepareCreatesArchiveWithOnlyNewSurveyData()
    {
        var published = Published(
            siteHeading: 90,
            poiStatuses: new Dictionary<string, GuardianPoiStatus>
            {
                ["P1"] = GuardianPoiStatus.Present,
            });
        var service = new GuardianSurveyShareService(
            temporaryDirectory,
            new GuardianPublishedSiteCatalog([published]));
        var changedPath = SurveyPath("Body A-ruins-1.json");
        var unchangedPath = SurveyPath("Body B-ruins-1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(changedPath)!);
        await File.WriteAllTextAsync(changedPath, "{\"changed\":true}");
        await File.WriteAllTextAsync(unchangedPath, "{\"changed\":false}");
        var changed = Survey(
            changedPath,
            new GuardianSurveyData
            {
                SiteType = "Alpha",
                SiteHeading = 90,
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["P1"] = GuardianPoiStatus.Absent,
                },
            });
        var unchanged = Survey(
            unchangedPath,
            new GuardianSurveyData
            {
                SiteType = "Alpha",
                SiteHeading = 90,
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["P1"] = GuardianPoiStatus.Present,
                },
            }) with
        {
            BodyName = "Body B",
            LocalizedName = "Guardian Ruins B",
        };
        var catalog = new GuardianPublishedSiteCatalog(
            [published, published with { FullBodyName = "Body B" }]);
        service = new GuardianSurveyShareService(temporaryDirectory, catalog);

        var result = await service.PrepareAsync(
            FrontierId,
            true,
            new GuardianCommanderDataReadResult(
                [changed, unchanged],
                [],
                []));

        var site = Assert.Single(result.Sites);
        Assert.Equal("Guardian Ruins A", site.DisplayName);
        Assert.Contains("Point-of-interest status", site.Reasons);
        Assert.True(File.Exists(result.ArchivePath));
        Assert.StartsWith(
            $"surveys-{FrontierId}-",
            Path.GetFileName(result.ArchivePath));
        using var archive = ZipFile.OpenRead(result.ArchivePath);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal(Path.GetFileName(changedPath), entry.FullName);
    }

    [Fact]
    public async Task ContentChangesProduceANewArchiveName()
    {
        var service = new GuardianSurveyShareService(
            temporaryDirectory,
            new GuardianPublishedSiteCatalog([]));
        var path = SurveyPath("Body A-ruins-1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "first");
        var survey = Survey(
            path,
            new GuardianSurveyData
            {
                SiteType = "Alpha",
                SiteHeading = 90,
            });
        var data = new GuardianCommanderDataReadResult([survey], [], []);

        var first = await service.PrepareAsync(FrontierId, true, data);
        await File.WriteAllTextAsync(path, "second");
        var second = await service.PrepareAsync(FrontierId, true, data);

        Assert.NotEqual(first.ArchivePath, second.ArchivePath);
        Assert.True(File.Exists(first.ArchivePath));
        Assert.True(File.Exists(second.ArchivePath));
    }

    [Fact]
    public async Task PrepareRejectsSurveyOutsideCommanderFolder()
    {
        var service = new GuardianSurveyShareService(
            temporaryDirectory,
            new GuardianPublishedSiteCatalog([]));
        var outsidePath = Path.Combine(temporaryDirectory, "outside.json");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(outsidePath, "survey");
        var survey = Survey(
            outsidePath,
            new GuardianSurveyData
            {
                SiteType = "Alpha",
                SiteHeading = 90,
            });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PrepareAsync(
                FrontierId,
                true,
                new GuardianCommanderDataReadResult([survey], [], [])));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string SurveyPath(string fileName)
    {
        return Path.Combine(
            temporaryDirectory,
            "guardian",
            FrontierId,
            fileName);
    }

    private static GuardianPublishedSite Published(
        int siteHeading,
        IReadOnlyDictionary<string, GuardianPoiStatus> poiStatuses)
    {
        return new GuardianPublishedSite(
            1,
            GuardianSiteKind.Ruins,
            "Body A",
            "Alpha",
            1,
            siteHeading,
            -1,
            null,
            poiStatuses,
            new Dictionary<string, int>(),
            [],
            string.Empty,
            "Body A-ruins-1.json");
    }

    private static GuardianCommanderSiteSurvey Survey(
        string path,
        GuardianSurveyData data)
    {
        return new GuardianCommanderSiteSurvey(
            path,
            "GR 1",
            "Guardian Ruins A",
            "Tester",
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            DateTimeOffset.Parse("2026-07-25T13:00:00Z"),
            "Alpha",
            1,
            42,
            "Test System",
            1,
            "Body A",
            string.Empty,
            false,
            data,
            [],
            new HashSet<char>());
    }
}
