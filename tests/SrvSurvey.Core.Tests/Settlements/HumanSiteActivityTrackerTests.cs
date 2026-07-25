using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteActivityTrackerTests
{
    private const double Radius = 6_000_000;

    [Fact]
    public void DataPickupMarksClosestTerminalAndTracksOneMeterAhead()
    {
        var (site, terminal, status) = CreateSiteAtTerminal();
        var tracker = new HumanSiteActivityTracker();

        var result = tracker.Apply(
            Parse(
                """
                {"timestamp":"2026-07-25T12:00:00Z","event":"BackpackChange","Added":[{"Name":"evacuationprotocols","Name_Localised":"Evacuation Protocols","Count":2,"Type":"Data"}]}
                """),
            site,
            status,
            trackMaterialCollection: true);

        Assert.True(result.ProcessedTerminalsChanged);
        Assert.Contains(0, tracker.ProcessedTerminalIndexes);
        var material = Assert.Single(tracker.CollectedMaterials);
        Assert.Equal("evacuationprotocols", material.Name);
        Assert.Equal("Data", material.Type);
        Assert.Equal(2, material.Count);
        Assert.Equal(terminal.Offset.X, material.Offset.X, 1);
        Assert.Equal(terminal.Offset.Y + 1, material.Offset.Y, 1);
    }

    [Fact]
    public void TerminalProcessingDoesNotRequireCollectionSurvey()
    {
        var (site, _, status) = CreateSiteAtTerminal();
        var tracker = new HumanSiteActivityTracker();

        var result = tracker.Apply(
            Parse(
                """
                {"event":"BackpackChange","Added":[{"Name":"opinionpolls","Count":1,"Type":"Data"}]}
                """),
            site,
            status,
            trackMaterialCollection: false);

        Assert.True(result.ProcessedTerminalsChanged);
        Assert.Empty(result.AddedMaterials);
        Assert.Empty(tracker.CollectedMaterials);
    }

    [Fact]
    public void ComponentPickupIsTrackedButDataCollectItemsIsIgnored()
    {
        var (site, _, status) = CreateSiteAtTerminal();
        var tracker = new HumanSiteActivityTracker();

        var component = tracker.Apply(
            Parse(
                """
                {"event":"CollectItems","Name":"graphene","Type":"Component","Count":1}
                """),
            site,
            status,
            trackMaterialCollection: true);
        var data = tracker.Apply(
            Parse(
                """
                {"event":"CollectItems","Name":"opinionpolls","Type":"Data","Count":1}
                """),
            site,
            status,
            trackMaterialCollection: true);

        Assert.Single(component.AddedMaterials);
        Assert.Empty(data.AddedMaterials);
        Assert.Single(tracker.CollectedMaterials);
    }

    [Fact]
    public void PickupOutsideFiveMetersDoesNotProcessTerminal()
    {
        var (site, terminal, status) = CreateSiteAtTerminal();
        var distant = HumanSiteNavigation.GetSurfaceLocation(
            new SurfaceCoordinate(
                site.Location.Latitude,
                site.Location.Longitude),
            new HumanSiteMapPoint(terminal.Offset.X + 10, terminal.Offset.Y),
            Radius,
            site.Heading!.Value);
        var tracker = new HumanSiteActivityTracker();

        var result = tracker.Apply(
            Parse(
                """
                {"event":"BackpackChange","Added":[{"Name":"opinionpolls","Count":1,"Type":"Data"}]}
                """),
            site,
            status with
            {
                Latitude = distant.Latitude,
                Longitude = distant.Longitude,
            },
            trackMaterialCollection: false);

        Assert.False(result.ProcessedTerminalsChanged);
        Assert.Empty(tracker.ProcessedTerminalIndexes);
    }

    [Fact]
    public void ChangingSettlementsClearsTransientActivity()
    {
        var (site, _, status) = CreateSiteAtTerminal();
        var tracker = new HumanSiteActivityTracker();
        tracker.Apply(
            Parse(
                """
                {"event":"BackpackChange","Added":[{"Name":"opinionpolls","Count":1,"Type":"Data"}]}
                """),
            site,
            status,
            trackMaterialCollection: true);

        var result = tracker.Apply(
            Parse("""{"event":"ApproachSettlement"}"""),
            site with { MarketId = site.MarketId + 1 },
            status,
            trackMaterialCollection: true);

        Assert.True(result.Reset);
        Assert.Empty(tracker.ProcessedTerminalIndexes);
        Assert.Empty(tracker.CollectedMaterials);
    }

    private static (
        HumanSiteLiveSnapshot Site,
        HumanSitePointOfInterest Terminal,
        EliteStatus Status) CreateSiteAtTerminal()
    {
        var template = HumanSiteTemplateCatalog.LoadEmbedded()
            .Find(HumanSiteEconomy.Extraction, 5)!;
        var terminal = template.DataTerminals[0];
        var origin = new SurfaceCoordinate(-12.5, 44.25);
        const double heading = 231;
        var terminalLocation = HumanSiteNavigation.GetSurfaceLocation(
            origin,
            terminal.Offset,
            Radius,
            heading);
        var site = new HumanSiteLiveSnapshot(
            "Test Settlement",
            "Test Settlement",
            12345,
            42,
            3,
            "Test 1",
            new HumanSiteSurfaceLocation(
                origin.Latitude,
                origin.Longitude),
            HumanSiteEconomy.Extraction,
            "$economy_Extraction;",
            "Extraction",
            "Raven Colonial",
            "Boom",
            "$government_Democracy;",
            "Democracy",
            ["dock"],
            "OnFootSettlement",
            HumanSiteLandingPads.From(template),
            template.SubType,
            template,
            heading,
            HumanSiteDockingStatus.None,
            0,
            null,
            true,
            default,
            default);
        var status = new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Flags2 = StatusFlags2.OnFoot
                | StatusFlags2.OnFootOnPlanet
                | StatusFlags2.OnFootExterior,
            Latitude = terminalLocation.Latitude,
            Longitude = terminalLocation.Longitude,
            Heading = (int)heading,
            PlanetRadius = (decimal)Radius,
        };
        return (site, terminal, status);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var value, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(value);
    }
}
