using System.IO.Compression;
using System.Text.Json;
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
    public async Task CompleteSurveyRoundTripsIntoLegacyShareArchive()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var survey = new GuardianCommanderSiteSurvey(
            string.Empty,
            "$Ancient_Tiny_001:#index=1;",
            "Guardian Structure",
            "Tester",
            DateTimeOffset.Parse("2026-08-20T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-20T13:00:00Z"),
            "Lacrosse",
            1,
            42,
            "Test System",
            1,
            "Test System A 1",
            "Complete field survey",
            false,
            new GuardianSurveyData
            {
                SiteType = "Lacrosse",
                SiteHeading = 45,
                Location = new GuardianSurfaceLocation(12.5, -34.25),
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["c1"] = GuardianPoiStatus.Present,
                    ["p1"] = GuardianPoiStatus.Absent,
                    ["p2"] = GuardianPoiStatus.Empty,
                    ["t1"] = GuardianPoiStatus.Present,
                },
                RelicHeadings = new Dictionary<string, int>
                {
                    ["t1"] = 135,
                },
                ComponentMaterials = new Dictionary<
                    string,
                    GuardianComponentLoadout>
                {
                    ["c1"] = new GuardianComponentLoadout(
                        "c1",
                        [
                            GuardianComponentMaterial.Cell,
                            GuardianComponentMaterial.Conduit,
                            GuardianComponentMaterial.Tech,
                        ]),
                    ["d1"] = new GuardianComponentLoadout(
                        "d1",
                        [GuardianComponentMaterial.Tech]),
                },
                RawPointsOfInterest =
                [
                    new GuardianPointOfInterest(
                        "x1",
                        GuardianPoiType.Urn,
                        10.5,
                        25,
                        180),
                ],
            },
            [new GuardianObelisk("A01", "H1", true, ["ca", "or"])],
            new HashSet<char>(['A', 'B']))
        {
            MapMarkerOffset = new GuardianMapPoint(6.5, -3.25),
        };

        await store.SaveAsync(FrontierId, true, survey);
        var commanderData = await new GuardianCommanderDataReader(
            temporaryDirectory).ReadAsync(FrontierId, true);
        var loaded = Assert.Single(commanderData.Surveys);
        var template = new GuardianSiteTemplate(
            "Lacrosse",
            "Lacrosse",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "c1", GuardianPoiType.Component, 0, 0, 0),
                new GuardianPointOfInterest(
                    "p1", GuardianPoiType.Orb, 0, 0, 0),
                new GuardianPointOfInterest(
                    "p2", GuardianPoiType.Casket, 0, 0, 0),
                new GuardianPointOfInterest(
                    "t1", GuardianPoiType.Relic, 0, 0, 0),
            ],
            [],
            new Dictionary<string, GuardianMapPoint>());
        var calculator = new GuardianSurveyCompletionCalculator(
            new GuardianSiteTemplateCatalog([template]));

        Assert.True(calculator.IsSurveyComplete(loaded.Survey));

        var service = new GuardianSurveyShareService(
            temporaryDirectory,
            new GuardianPublishedSiteCatalog([]));
        var result = await service.PrepareAsync(
            FrontierId,
            true,
            commanderData);

        var shared = Assert.Single(result.Sites);
        Assert.Contains("No published survey", shared.Reasons);
        Assert.Contains("Raw points of interest", shared.Reasons);
        Assert.Contains("Component materials", shared.Reasons);
        Assert.Contains("Map alignment offset", shared.Reasons);
        using var archive = ZipFile.OpenRead(result.ArchivePath);
        var entry = Assert.Single(archive.Entries);
        await using var entryStream = entry.Open();
        using var document = await JsonDocument.ParseAsync(entryStream);
        var root = document.RootElement;
        Assert.Equal("Tester", root.GetProperty("commander").GetString());
        Assert.Equal("Lacrosse", root.GetProperty("type").GetString());
        Assert.Equal(45, root.GetProperty("siteHeading").GetInt32());
        Assert.Equal(
            "Complete field survey",
            root.GetProperty("notes").GetString());
        Assert.Equal(
            12.5,
            root.GetProperty("location").GetProperty("lat").GetDouble());
        Assert.Equal(
            -34.25,
            root.GetProperty("location").GetProperty("long").GetDouble());
        Assert.Equal(
            6.5,
            root.GetProperty("mapMarkerOffset").GetProperty("x").GetDouble());
        Assert.Equal(
            -3.25,
            root.GetProperty("mapMarkerOffset").GetProperty("y").GetDouble());
        Assert.Equal("c1,t1", root.GetProperty("poiPresent").GetString());
        Assert.Equal("p1", root.GetProperty("poiAbsent").GetString());
        Assert.Equal("p2", root.GetProperty("poiEmpty").GetString());
        Assert.Equal("AB", root.GetProperty("obeliskGroups").GetString());
        Assert.Equal(
            "A01!-ca,or-H1-",
            root.GetProperty("activeObelisks")[0].GetString());
        Assert.Equal(
            135,
            root.GetProperty("relicHeadings").GetProperty("t1").GetInt32());
        Assert.Equal(
            "urn",
            root.GetProperty("rawPoi")[0].GetProperty("type").GetString());
        Assert.Equal(
            ["c1,cell,conduit,tech", "d1,tech"],
            root.GetProperty("components")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task SharedAlignmentOffsetLoadsForAnotherCommander()
    {
        const string otherFrontierId = "F999";
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var survey = Survey(string.Empty, new GuardianSurveyData
        {
            SiteType = "Alpha",
        }) with
        {
            MapMarkerOffset = new GuardianMapPoint(8.25, -4.5),
        };
        var path = await store.SaveAsync(FrontierId, true, survey);
        survey = survey with { Path = path };
        var service = new GuardianSurveyShareService(
            temporaryDirectory,
            new GuardianPublishedSiteCatalog(
                [Published(-1, new Dictionary<string, GuardianPoiStatus>())]));

        var result = await service.PrepareAsync(
            FrontierId,
            true,
            new GuardianCommanderDataReadResult([survey], [], []));

        var shared = Assert.Single(result.Sites);
        Assert.Equal(["Map alignment offset"], shared.Reasons);
        using (var archive = ZipFile.OpenRead(result.ArchivePath))
        {
            var entry = Assert.Single(archive.Entries);
            var destinationDirectory = Path.Combine(
                temporaryDirectory,
                "guardian",
                otherFrontierId);
            Directory.CreateDirectory(destinationDirectory);
            entry.ExtractToFile(Path.Combine(destinationDirectory, entry.Name));
        }

        var loaded = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync(otherFrontierId, true);

        Assert.Equal(
            new GuardianMapPoint(8.25, -4.5),
            Assert.Single(loaded.Surveys).MapMarkerOffset);
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
