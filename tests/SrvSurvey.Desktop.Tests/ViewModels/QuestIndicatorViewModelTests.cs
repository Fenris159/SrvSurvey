using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Quests;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class QuestIndicatorViewModelTests
{
    [Fact]
    public void IndicatorShowsVisibleObjectivesUnreadMessagesAndSurfaceTargets()
    {
        var viewModel = new QuestIndicatorViewModel();
        var snapshot = CreateSnapshot();
        var status = new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
            Latitude = 0,
            Longitude = 0,
            Heading = 0,
            PlanetRadius = 6_371_000,
        };

        viewModel.Update([snapshot], status, enabled: true);

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Indicator Quest", viewModel.QuestTitle);
        Assert.True(viewModel.HasUnreadMessages);
        Assert.Equal("2 unread messages", viewModel.UnreadMessageText);
        var objective = Assert.Single(viewModel.Objectives);
        Assert.Equal("Scan the beacon", objective.Label);
        Assert.Equal("1 / 3", objective.Progress);
        var location = Assert.Single(viewModel.Locations);
        Assert.Equal("Beacon", location.Label);
        Assert.Equal("111 m", location.Distance);
        Assert.Equal("90° relative", location.Bearing);
        Assert.True(location.IsWithinTarget);
        Assert.Equal("✓", location.StateGlyph);
    }

    [Fact]
    public void GuiFocusedMenusUseLegacyQuestVisibilityModes()
    {
        var viewModel = new QuestIndicatorViewModel();
        var snapshot = CreateSnapshot();
        var status = new EliteStatus
        {
            Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
            GuiFocus = GuiFocus.GalaxyMap,
        };

        viewModel.Update([snapshot], status, enabled: true);
        Assert.False(viewModel.ShouldShow);

        viewModel.Update(
            [snapshot],
            status with { GuiFocus = GuiFocus.ExternalPanel },
            enabled: true);
        Assert.True(viewModel.ShouldShow);

        viewModel.Update(
            [snapshot],
            status with { GuiFocus = GuiFocus.NoFocus },
            enabled: true,
            musicTrack: "SystemMap");
        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public void DisabledOrEmptyIndicatorIsHiddenAndInvalidTargetsAreIgnored()
    {
        var viewModel = new QuestIndicatorViewModel();
        var snapshot = CreateSnapshot() with
        {
            BodyLocations = new Dictionary<string, string>
            {
                ["bad"] = "not-coordinates",
            },
        };

        viewModel.Update([snapshot], null, enabled: false);

        Assert.False(viewModel.ShouldShow);
        Assert.Empty(viewModel.Locations);

        viewModel.Update([], null, enabled: true);

        Assert.False(viewModel.ShouldShow);
    }

    private static QuestRuntimeSnapshot CreateSnapshot()
    {
        return new QuestRuntimeSnapshot(
            new RavenQuestReference("Raven", "indicator", 1),
            "Indicator Quest",
            "Subtitle",
            false,
            false,
            null,
            2,
            new Dictionary<string, string>
            {
                ["scan"] = "visible,1,3",
                ["hidden"] = "hidden",
            },
            new Dictionary<string, string>
            {
                ["scan"] = "Scan the beacon",
                ["hidden"] = "Hidden task",
            },
            [],
            new HashSet<string>(),
            new Dictionary<string, string>
            {
                ["Beacon"] = "0,0.001,200",
            },
            []);
    }
}
