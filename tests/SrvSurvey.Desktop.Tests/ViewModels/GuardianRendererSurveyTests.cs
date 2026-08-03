using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianRendererSurveyTests
{
    [Fact]
    public void PublishedGuardianMapStateIsRenderedWithoutCommanderSurvey()
    {
        var published = Published(
            siteHeading: 123,
            towerHeading: 45,
            statuses: new Dictionary<string, GuardianPoiStatus>
            {
                ["p1"] = GuardianPoiStatus.Present,
            },
            relicHeadings: new Dictionary<string, int>
            {
                ["t1"] = 200,
            });

        var merged = GuardianViewModel.MergeRendererSurvey(
            "Alpha",
            commander: null,
            published,
            reference: null);

        Assert.Equal(123, merged.SiteHeading);
        Assert.Equal(45, merged.RelicTowerHeading);
        Assert.Equal(GuardianPoiStatus.Present, merged.PoiStatuses["p1"]);
        Assert.Equal(200, merged.RelicHeadings["t1"]);
    }

    [Fact]
    public void CommanderGuardianMapStateOverridesPublishedValues()
    {
        var published = Published(
            siteHeading: 123,
            towerHeading: 45,
            statuses: new Dictionary<string, GuardianPoiStatus>
            {
                ["p1"] = GuardianPoiStatus.Absent,
                ["p2"] = GuardianPoiStatus.Present,
            },
            relicHeadings: new Dictionary<string, int>
            {
                ["t1"] = 200,
                ["t2"] = 210,
            });
        var commander = new GuardianSurveyData
        {
            SiteHeading = 321,
            RelicTowerHeading = 54,
            PoiStatuses = new Dictionary<string, GuardianPoiStatus>
            {
                ["p1"] = GuardianPoiStatus.Empty,
            },
            RelicHeadings = new Dictionary<string, int>
            {
                ["t1"] = 220,
            },
        };

        var merged = GuardianViewModel.MergeRendererSurvey(
            "Alpha",
            commander,
            published,
            reference: null);

        Assert.Equal(321, merged.SiteHeading);
        Assert.Equal(54, merged.RelicTowerHeading);
        Assert.Equal(GuardianPoiStatus.Empty, merged.PoiStatuses["p1"]);
        Assert.Equal(GuardianPoiStatus.Present, merged.PoiStatuses["p2"]);
        Assert.Equal(220, merged.RelicHeadings["t1"]);
        Assert.Equal(210, merged.RelicHeadings["t2"]);
    }

    private static GuardianPublishedSite Published(
        int siteHeading,
        int towerHeading,
        IReadOnlyDictionary<string, GuardianPoiStatus> statuses,
        IReadOnlyDictionary<string, int> relicHeadings)
    {
        return new GuardianPublishedSite(
            1,
            GuardianSiteKind.Ruins,
            "Test A 1",
            "Alpha",
            1,
            siteHeading,
            towerHeading,
            null,
            statuses,
            relicHeadings,
            [],
            string.Empty,
            "test");
    }
}
