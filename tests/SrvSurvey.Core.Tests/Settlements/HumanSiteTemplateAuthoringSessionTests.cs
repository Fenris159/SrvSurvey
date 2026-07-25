using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteTemplateAuthoringSessionTests
{
    [Fact]
    public void PolygonAndCircleCommitAsPortableBuildingPaths()
    {
        var source = Template();
        var session = new HumanSiteTemplateAuthoringSession(source);

        session.BeginPolygon(new HumanSiteMapPoint(0, 0));
        session.AddPolygonPoint(new HumanSiteMapPoint(10, 0));
        session.EndPolygon(new HumanSiteMapPoint(10, 10), closePath: true);
        session.AddCircle(new HumanSiteMapPoint(20, 20), radius: 5);
        var building = session.CommitBuilding("HAB");

        Assert.Equal(2, building.Paths.Count);
        Assert.Equal([0, 1, 129], building.Paths[0].PointTypes);
        Assert.Equal(13, building.Paths[1].Points.Count);
        Assert.Equal(131, building.Paths[1].PointTypes[^1]);
        Assert.Equal("HAB", session.Template.Buildings[^1].Name);
        Assert.Empty(session.PendingBuildingPaths);
        Assert.Empty(source.Buildings);
        var projection = new HumanSiteMapProjector().Project(session.Template);
        Assert.Equal(2, projection.Buildings[^1].Paths.Count);
        Assert.Contains(
            projection.Buildings[^1].Paths[1].Segments,
            segment => segment.Kind == HumanSitePathSegmentKind.CubicBezier);
    }

    [Fact]
    public void NamedTerminalAndDoorPointsRetainFloorAndSecurity()
    {
        var session = new HumanSiteTemplateAuthoringSession(Template());
        var offset = new HumanSiteMapPoint(12.5, -3.5);

        session.AddNamedPoint("Battery", offset, securityLevel: 2, floor: 3);
        session.AddDataTerminal(offset, securityLevel: 1, floor: 2);
        session.AddSecureDoor(
            offset,
            rotation: -90,
            securityLevel: 3,
            floor: 1);

        var named = Assert.Single(session.Template.NamedPoints);
        Assert.Equal("Battery", named.Name);
        Assert.Equal(2, named.SecurityLevel);
        Assert.Equal(3, named.Floor);
        Assert.Equal(270, Assert.Single(
            session.Template.SecureDoors).Rotation);
        Assert.Equal(2, Assert.Single(
            session.Template.DataTerminals).Floor);
    }

    [Fact]
    public void InvalidOrIncompleteDraftsDoNotMutateTemplate()
    {
        var session = new HumanSiteTemplateAuthoringSession(Template());

        Assert.Throws<InvalidOperationException>(() =>
            session.CommitBuilding("HAB"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.AddCircle(new HumanSiteMapPoint(0, 0), radius: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.AddNamedPoint(
                "Battery",
                new HumanSiteMapPoint(double.NaN, 0),
                securityLevel: 0,
                floor: 1));

        Assert.Empty(session.Template.Buildings);
        Assert.Empty(session.Template.NamedPoints);
    }

    [Fact]
    public void PendingAndCommittedElementsCanBeUndoneIndependently()
    {
        var session = new HumanSiteTemplateAuthoringSession(Template());
        session.AddCircle(new HumanSiteMapPoint(0, 0), radius: 2);
        Assert.True(session.RemoveLastPendingPath());
        Assert.False(session.RemoveLastPendingPath());
        session.AddNamedPoint(
            "Medkit",
            new HumanSiteMapPoint(1, 1),
            securityLevel: 0,
            floor: 1);

        Assert.True(session.RemoveLastNamedPoint());
        Assert.False(session.RemoveLastNamedPoint());
    }

    private static HumanSiteTemplate Template()
    {
        return new HumanSiteTemplate(
            HumanSiteEconomy.Agriculture,
            1,
            "Test",
            [
                new HumanSiteLandingPad(
                    new HumanSiteMapPoint(1, 1),
                    0,
                    0,
                    0,
                    HumanSiteLandingPadSize.Small),
            ],
            [],
            [],
            [],
            [],
            []);
    }
}
