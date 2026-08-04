using System.Text.Json.Nodes;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianCommanderSurveyStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-survey-store-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveRoundTripsCompactLegacyContractAndUnknownFields()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var survey = CreateSurvey();
        var path = store.GetSurveyPath(
            "F123",
            true,
            survey.BodyName,
            survey.Index,
            isRuins: true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "futureSurveyOption":{"enabled":true},
              "location":{"lat":0,"long":0,"futureCoordinate":8},
              "poiStatus":{"old":"present"},
              "confirmedPOI":{"older":true},
              "components":[
                "future-format",
                "c1,unknown,unknown,unknown",
                "future,quantum"
              ]
            }
            """);

        var savedPath = await store.SaveAsync("F123", true, survey);

        Assert.Equal(path, savedPath);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureSurveyOption"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(8, root["location"]!["futureCoordinate"]!.GetValue<int>());
        Assert.Null(root["poiStatus"]);
        Assert.Null(root["confirmedPOI"]);
        Assert.Equal("p1,t1", root["poiPresent"]!.GetValue<string>());
        Assert.Equal("p2", root["poiAbsent"]!.GetValue<string>());
        Assert.Equal("p3", root["poiEmpty"]!.GetValue<string>());
        Assert.Equal("ACD", root["obeliskGroups"]!.GetValue<string>());
        Assert.Equal(
            "A08!-ca,ca-H9-",
            root["activeObelisks"]![0]!.GetValue<string>());
        Assert.Equal(
            "brokeObelisk",
            root["rawPoi"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(
            [
                "future-format",
                "c1,cell,conduit,tech",
                "future,quantum",
                "d1,tech",
            ],
            root["components"]!.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray());

        var loaded = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync("F123", true);
        var roundTrip = Assert.Single(loaded.Surveys);
        Assert.Empty(loaded.Errors);
        Assert.Equal(survey.Name, roundTrip.Name);
        Assert.Equal(survey.Survey.Location, roundTrip.Survey.Location);
        Assert.Equal(
            GuardianPoiStatus.Present,
            roundTrip.Survey.PoiStatuses["p1"]);
        Assert.Equal(45, roundTrip.Survey.RelicHeadings["t1"]);
        Assert.Equal(
            GuardianComponentMaterial.Conduit,
            roundTrip.Survey.ComponentMaterials["c1"].GetItem(1));
        Assert.Equal(
            GuardianComponentMaterial.Tech,
            roundTrip.Survey.ComponentMaterials["d1"].GetItem(0));
        Assert.Equal(['A', 'C', 'D'], roundTrip.ObeliskGroups.Order());
        Assert.True(Assert.Single(roundTrip.ActiveObelisks).Scanned);
        Assert.Equal(
            GuardianPoiType.BrokenObelisk,
            Assert.Single(roundTrip.Survey.RawPointsOfInterest!).Type);
    }

    [Fact]
    public async Task SaveUsesLegacyFolderAndStructureFilename()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var source = CreateSurvey();
        var survey = source with
        {
            Name = "$Ancient_Tiny_001:#index=1;",
            SiteType = "Lacrosse",
            Survey = new GuardianSurveyData
            {
                SiteType = "Lacrosse",
                SiteHeading = source.Survey.SiteHeading,
                RelicTowerHeading = source.Survey.RelicTowerHeading,
                Location = source.Survey.Location,
                PoiStatuses = source.Survey.PoiStatuses,
                RelicHeadings = source.Survey.RelicHeadings,
                ComponentMaterials = source.Survey.ComponentMaterials,
                RawPointsOfInterest = source.Survey.RawPointsOfInterest,
            },
        };

        var path = await store.SaveAsync("F123", false, survey);

        Assert.EndsWith(
            Path.Combine(
                "guardian",
                "F123",
                "legacy",
                $"{survey.BodyName}-structure-1.json"),
            path,
            StringComparison.Ordinal);
        var loaded = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync("F123", false);
        Assert.True(Assert.Single(loaded.Surveys).Legacy);
    }

    [Fact]
    public async Task SavePreservesStaleComponentsIfMissingFromMemory()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var source = CreateSurvey();
        var path = store.GetSurveyPath(
            "F123",
            true,
            source.BodyName,
            source.Index,
            isRuins: true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "components":[
                "c1,cell,conduit,tech",
                "future-format",
                "future,quantum"
              ]
            }
            """);
        var survey = source with
        {
            Survey = new GuardianSurveyData
            {
                SiteType = source.Survey.SiteType,
                SiteHeading = source.Survey.SiteHeading,
                RelicTowerHeading = source.Survey.RelicTowerHeading,
                Location = source.Survey.Location,
                PoiStatuses = source.Survey.PoiStatuses,
                RelicHeadings = source.Survey.RelicHeadings,
                ComponentMaterials = new Dictionary<
                    string,
                    GuardianComponentLoadout>(),
                RawPointsOfInterest = source.Survey.RawPointsOfInterest,
            },
        };

        await store.SaveAsync("F123", true, survey);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(
            ["c1,cell,conduit,tech", "future-format", "future,quantum"],
            root["components"]!.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray());
        var loaded = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync("F123", true);
        var loadedSurvey = Assert.Single(loaded.Surveys);
        Assert.Empty(loadedSurvey.Survey.ComponentMaterials);
    }

    [Fact]
    public async Task SaveRefusesMalformedExistingSurveyAndUnsafeNames()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var survey = CreateSurvey();
        var path = store.GetSurveyPath(
            "F123",
            true,
            survey.BodyName,
            survey.Index,
            isRuins: true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string malformed = "{\"name\":";
        await File.WriteAllTextAsync(path, malformed);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync("F123", true, survey));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
        Assert.Throws<ArgumentException>(
            () => store.GetSurveyPath(
                "../F123",
                true,
                survey.BodyName,
                1,
                true));
        Assert.Throws<ArgumentException>(
            () => store.GetSurveyPath(
                "F123",
                true,
                "Body/escape",
                1,
                true));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static GuardianCommanderSiteSurvey CreateSurvey()
    {
        return new GuardianCommanderSiteSurvey(
            string.Empty,
            "$Ancient:#index=1;",
            "Guardian Ruins",
            "Drew",
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-02T11:00:00Z"),
            "Beta",
            1,
            3515254557027,
            "Synuefe XR-H d11-102",
            13,
            "Synuefe XR-H d11-102 1 b",
            "survey note",
            false,
            new GuardianSurveyData
            {
                SiteType = "Beta",
                SiteHeading = 332,
                RelicTowerHeading = 93,
                Location = new GuardianSurfaceLocation(-46.5, 133.9),
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["p3"] = GuardianPoiStatus.Empty,
                    ["p1"] = GuardianPoiStatus.Present,
                    ["t1"] = GuardianPoiStatus.Present,
                    ["p2"] = GuardianPoiStatus.Absent,
                    ["unknown"] = GuardianPoiStatus.Unknown,
                },
                RelicHeadings = new Dictionary<string, int>
                {
                    ["t1"] = 45,
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
                        GuardianPoiType.BrokenObelisk,
                        12.5,
                        30,
                        180),
                ],
            },
            [new GuardianObelisk("A08", "H9", true, ["ca", "ca"])],
            new HashSet<char>(['D', 'A', 'C']));
    }
}
