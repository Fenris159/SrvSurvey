using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteLiveStateTests
{
    [Fact]
    public void ApproachSettlementCapturesCompatibleOdysseySite()
    {
        var state = CreateState();

        var changed = state.Apply(Parse(ApproachJson));

        Assert.True(changed);
        var site = Assert.IsType<HumanSiteLiveSnapshot>(state.CurrentSite);
        Assert.Equal("Haberlandt Survey", site.Name);
        Assert.Equal(12_345, site.MarketId);
        Assert.Equal(42, site.SystemAddress);
        Assert.Equal(3, site.BodyId);
        Assert.Equal(new HumanSiteSurfaceLocation(12.5, -45.25), site.Location);
        Assert.Equal(HumanSiteEconomy.Agriculture, site.Economy);
        Assert.Equal("Agriculture", site.EconomyLocalized);
        Assert.Equal("Raven Colonial", site.FactionName);
        Assert.Equal("War", site.FactionState);
        Assert.Equal(["dock", "refuel"], site.Services);
        Assert.Equal(DateTimeOffset.Parse("2026-07-25T03:00:00Z"),
            site.FirstApproached);
    }

    [Fact]
    public void IncompatibleSettlementsDoNotBecomeHumanSites()
    {
        var state = CreateState();

        Assert.False(state.Apply(Parse(ApproachJson.Replace(
            "Haberlandt Survey",
            "$Ancient:#index=1;"))));
        Assert.False(state.Apply(Parse(ApproachJson.Replace(
            "\"dock\",\"refuel\"",
            "\"dock\",\"socialspace\""))));
        Assert.False(state.Apply(Parse(ApproachJson
            .Replace("Haberlandt Survey", "Planetary Construction Site: Raven")
            .Replace("\"dock\",\"refuel\"",
                "\"dock\",\"colonisationcontribution\""))));
        Assert.False(state.Apply(Parse(ApproachJson.Replace(
            "$government_Democracy;",
            "$government_Engineer;"))));
        Assert.Null(state.CurrentSite);
    }

    [Fact]
    public void DockingEventsInferUniqueTemplateAndTrackProgress()
    {
        var state = CreateState();
        state.Apply(Parse(ApproachJson));

        Assert.True(state.Apply(Parse(
            """
            {"event":"DockingRequested","StationName":"Haberlandt Survey","MarketID":12345,"StationType":"OnFootSettlement","LandingPads":{"Small":2,"Medium":0,"Large":1}}
            """)));
        Assert.Equal(HumanSiteDockingStatus.Requested,
            state.CurrentSite!.Docking);
        Assert.Equal(4, state.CurrentSite.SubType);
        Assert.Equal("Fornax", state.CurrentSite.Template!.Name);

        Assert.True(state.Apply(Parse(
            """
            {"event":"DockingGranted","StationName":"Haberlandt Survey","MarketID":12345,"StationType":"OnFootSettlement","LandingPad":3}
            """)));
        Assert.Equal(HumanSiteDockingStatus.Granted,
            state.CurrentSite.Docking);
        Assert.Equal(3, state.CurrentSite.GrantedPad);

        Assert.True(state.Apply(Parse(
            """
            {"event":"Docked","StationName":"Haberlandt Survey","MarketID":12345,"StationType":"OnFootSettlement","LandingPads":{"Small":2,"Medium":0,"Large":1}}
            """)));
        Assert.Equal(HumanSiteDockingStatus.Docked,
            state.CurrentSite.Docking);
        Assert.True(state.CurrentSite.HasLanded);
    }

    [Fact]
    public void AmbiguousPadsDoNotGuessSettlementSubtype()
    {
        var state = CreateState();
        state.Apply(Parse(ApproachJson));

        state.Apply(Parse(
            """
            {"event":"DockingRequested","MarketID":12345,"StationType":"OnFootSettlement","LandingPads":{"Small":1,"Medium":0,"Large":0}}
            """));

        Assert.Equal(0, state.CurrentSite!.SubType);
        Assert.Null(state.CurrentSite.Template);
    }

    [Fact]
    public void DenialCancellationAndDepartureFollowCurrentMarket()
    {
        var state = CreateState();
        state.Apply(Parse(ApproachJson));

        Assert.False(state.Apply(Parse(
            """{"event":"DockingDenied","MarketID":999,"Reason":"NoSpace"}""")));
        Assert.True(state.Apply(Parse(
            """{"event":"DockingDenied","MarketID":12345,"Reason":"NoSpace"}""")));
        Assert.Equal(HumanSiteDockingStatus.Denied,
            state.CurrentSite!.Docking);
        Assert.Equal("NoSpace", state.CurrentSite.DockingDeniedReason);

        Assert.True(state.Apply(Parse(
            """{"event":"DockingCancelled","MarketID":12345}""")));
        Assert.Equal(HumanSiteDockingStatus.None,
            state.CurrentSite.Docking);

        Assert.True(state.Apply(Parse(
            """{"event":"SupercruiseEntry"}""")));
        Assert.Null(state.CurrentSite);
    }

    [Fact]
    public void RepeatedApproachRetainsLearnedTemplateAndFirstVisit()
    {
        var state = CreateState();
        state.Apply(Parse(ApproachJson));
        state.Apply(Parse(
            """
            {"event":"DockingRequested","MarketID":12345,"StationType":"OnFootSettlement","LandingPads":{"Small":2,"Medium":0,"Large":1}}
            """));

        state.Apply(Parse(ApproachJson.Replace(
            "2026-07-25T03:00:00Z",
            "2026-07-25T03:10:00Z")));

        Assert.Equal(4, state.CurrentSite!.SubType);
        Assert.Equal("Fornax", state.CurrentSite.Template!.Name);
        Assert.Equal(DateTimeOffset.Parse("2026-07-25T03:00:00Z"),
            state.CurrentSite.FirstApproached);
        Assert.Equal(DateTimeOffset.Parse("2026-07-25T03:10:00Z"),
            state.CurrentSite.LastUpdated);
    }

    [Fact]
    public void InferredGeometryUpdatesTemplateAndNormalizesHeading()
    {
        var state = CreateState();
        var template = HumanSiteTemplateCatalog.LoadEmbedded()
            .Find(HumanSiteEconomy.Agriculture, 4)!;
        state.Apply(Parse(ApproachJson));

        var changed = state.ApplyGeometry(new HumanSiteGeometrySolution(
            4,
            template,
            -10,
            1,
            0.5));

        Assert.True(changed);
        Assert.Equal(4, state.CurrentSite!.SubType);
        Assert.Same(template, state.CurrentSite.Template);
        Assert.Equal(350, state.CurrentSite.Heading);
    }

    private static HumanSiteLiveState CreateState()
    {
        return new HumanSiteLiveState(
            HumanSiteTemplateCatalog.LoadEmbedded());
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var value, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(value);
    }

    private const string ApproachJson =
        """
        {"timestamp":"2026-07-25T03:00:00Z","event":"ApproachSettlement","Name":"Haberlandt Survey","Name_Localised":"Haberlandt Survey","MarketID":12345,"SystemAddress":42,"BodyID":3,"BodyName":"Raven 1 a","Latitude":12.5,"Longitude":-45.25,"StationEconomy":"$economy_Agri;","StationEconomy_Localised":"Agriculture","StationFaction":{"Name":"Raven Colonial","FactionState":"War"},"StationGovernment":"$government_Democracy;","StationGovernment_Localised":"Democracy","StationServices":["dock","refuel"]}
        """;
}
