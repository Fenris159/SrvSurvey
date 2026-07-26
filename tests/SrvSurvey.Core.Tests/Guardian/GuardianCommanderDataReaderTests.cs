using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianCommanderDataReaderTests
{
    [Fact]
    public async Task ReadsCompactSiteAndBeaconFormatsWhileIsolatingBadFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var folder = Path.Combine(root, "guardian", "F123");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "Synuefe XR-H d11-102 1 b-ruins-1.json"),
                """
                {
                  "name":"$Ancient:#index=1;",
                  "nameLocalised":"Guardian Ruins",
                  "commander":"Drew",
                  "firstVisited":"2026-07-01T10:00:00Z",
                  "lastVisited":"2026-07-02T11:00:00Z",
                  "type":"Beta",
                  "index":1,
                  "location":{"lat":-46.5,"long":133.9},
                  "systemAddress":3515254557027,
                  "systemName":"Synuefe XR-H d11-102",
                  "bodyId":13,
                  "bodyName":"Synuefe XR-H d11-102 1 b",
                  "siteHeading":332,
                  "relicTowerHeading":93,
                  "notes":"survey note",
                  "obeliskGroups":"ACD",
                  "activeObelisks":["A08!-ca,ca-H9-"],
                  "relicHeadings":{"t1":45},
                  "components":[
                    "c1,cell,conduit,tech",
                    "d1,tech",
                    "future,quantum"
                  ],
                  "poiPresent":"p1,t1",
                  "poiAbsent":"p2",
                  "poiEmpty":"p3"
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "IC 2391 Sector CQ-Y c16-beacon.json"),
                """
                {
                  "firstVisited":"2026-07-03T10:00:00Z",
                  "lastVisited":"2026-07-04T10:00:00Z",
                  "systemName":"IC 2391 Sector CQ-Y c16",
                  "systemAddress":4482838500042,
                  "bodyName":"2",
                  "bodyId":8,
                  "notes":"beacon note",
                  "scannedLocations":{
                    "2026-07-04T10:00:00+00:00":{"lat":1.25,"long":-2.5}
                  }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "bad-structure-1.json"),
                "{not-json");

            var result = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);

            var survey = Assert.Single(result.Surveys);
            Assert.Equal("Drew", survey.Commander);
            Assert.Equal("survey note", survey.Notes);
            Assert.Equal(
                new GuardianSurfaceLocation(-46.5, 133.9),
                survey.Survey.Location);
            Assert.Equal(GuardianPoiStatus.Present, survey.Survey.PoiStatuses["p1"]);
            Assert.Equal(GuardianPoiStatus.Absent, survey.Survey.PoiStatuses["p2"]);
            Assert.Equal(GuardianPoiStatus.Empty, survey.Survey.PoiStatuses["p3"]);
            Assert.Equal(45, survey.Survey.RelicHeadings["t1"]);
            Assert.Equal(
                [
                    GuardianComponentMaterial.Cell,
                    GuardianComponentMaterial.Conduit,
                    GuardianComponentMaterial.Tech,
                ],
                survey.Survey.ComponentMaterials["c1"].Items);
            Assert.Equal(
                GuardianComponentMaterial.Tech,
                survey.Survey.ComponentMaterials["d1"].GetItem(0));
            Assert.DoesNotContain(
                "future",
                survey.Survey.ComponentMaterials.Keys);
            Assert.Equal(['A', 'C', 'D'], survey.ObeliskGroups.Order());
            var obelisk = Assert.Single(survey.ActiveObelisks);
            Assert.True(obelisk.Scanned);
            Assert.Equal("H9", obelisk.LogCode);

            var beacon = Assert.Single(result.Beacons);
            Assert.Equal("beacon note", beacon.Notes);
            Assert.Equal(
                new GuardianSurfaceLocation(1.25, -2.5),
                Assert.Single(beacon.ScannedLocations).Value);
            Assert.Single(result.Errors);
            Assert.Contains("bad-structure-1.json", result.Errors[0]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReadsOldDictionaryAndConfirmedPoiFormats()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var folder = Path.Combine(root, "guardian", "F123");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "one-ruins-1.json"),
                """
                {
                  "type":"Alpha","index":1,"systemAddress":1,"bodyId":2,
                  "poiStatus":{"p1":"present","p2":2},
                  "activeObelisks":{"A01":{"msg":"H1","scanned":true}},
                  "obeliskGroups":["A","B"]
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "two-ruins-1.json"),
                """
                {
                  "type":"Alpha","index":1,"systemAddress":2,"bodyId":3,
                  "confirmedPOI":{"p3":true,"p4":false}
                }
                """);

            var result = await new GuardianCommanderDataReader(root)
                .ReadAsync("F123", isOdyssey: true);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Surveys.Count);
            var first = result.Surveys.Single(survey => survey.SystemAddress == 1);
            Assert.Equal(GuardianPoiStatus.Present, first.Survey.PoiStatuses["p1"]);
            Assert.Equal(GuardianPoiStatus.Absent, first.Survey.PoiStatuses["p2"]);
            Assert.True(Assert.Single(first.ActiveObelisks).Scanned);
            var second = result.Surveys.Single(survey => survey.SystemAddress == 2);
            Assert.Equal(GuardianPoiStatus.Present, second.Survey.PoiStatuses["p3"]);
            Assert.Equal(GuardianPoiStatus.Absent, second.Survey.PoiStatuses["p4"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SelectsLegacySubfolderAndRejectsFrontierIdPaths()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var live = Path.Combine(root, "guardian", "F123");
            var legacy = Path.Combine(live, "legacy");
            Directory.CreateDirectory(legacy);
            await File.WriteAllTextAsync(
                Path.Combine(live, "live-beacon.json"),
                "{\"systemAddress\":1}");
            await File.WriteAllTextAsync(
                Path.Combine(legacy, "legacy-beacon.json"),
                "{\"systemAddress\":2}");
            await File.WriteAllTextAsync(
                Path.Combine(legacy, "legacy-body-ruins-1.json"),
                "{\"type\":\"Alpha\",\"index\":1,\"bodyName\":\"legacy-body\"}");
            var reader = new GuardianCommanderDataReader(root);

            var result = await reader.ReadAsync("F123", isOdyssey: false);

            Assert.Equal(2, Assert.Single(result.Beacons).SystemAddress);
            Assert.True(Assert.Single(result.Beacons).Legacy);
            Assert.True(Assert.Single(result.Surveys).Legacy);
            await Assert.ThrowsAsync<ArgumentException>(
                () => reader.ReadAsync("../F123", isOdyssey: true));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MergeAppliesCommanderVisitsAndLegacyCompletionRules()
    {
        var references = GuardianSiteCatalog.LoadEmbedded();
        var published = GuardianPublishedSiteCatalog.LoadEmbedded();
        var calculator = new GuardianSurveyCompletionCalculator(
            GuardianSiteTemplateCatalog.LoadEmbedded());
        var target = Assert.Single(
            references.Sites,
            site => site.Kind == GuardianSiteKind.Ruins && site.SiteId == 162);
        var survey = new GuardianCommanderSiteSurvey(
            "survey.json",
            string.Empty,
            string.Empty,
            "Drew",
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-02T10:00:00Z"),
            target.SiteType,
            target.Index,
            target.SystemAddress,
            target.SystemName,
            target.BodyId,
            target.FullBodyName,
            "local note",
            false,
            new GuardianSurveyData
            {
                SiteType = target.SiteType,
                SiteHeading = target.SiteHeading,
                RelicTowerHeading = target.RelicTowerHeading,
                Location = new GuardianSurfaceLocation(
                    target.Latitude!.Value,
                    target.Longitude!.Value),
            },
            [],
            new HashSet<char>());
        var commanderData = new GuardianCommanderDataReadResult(
            [survey],
            [],
            []);

        var merged = GuardianSiteVisitCatalog.Merge(
            references,
            commanderData,
            published,
            calculator);
        var visit = merged.Visits.Single(
            item => item.Reference == target);

        Assert.True(visit.IsVisited);
        Assert.True(visit.HasCommanderData);
        Assert.Equal("local note", visit.Notes);
        Assert.Equal(target.SurveyProgress, visit.SurveyProgress);
        Assert.False(visit.IsSurveyComplete);
        Assert.NotNull(visit.Completion);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-reader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
