using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class HumanSiteViewModelTests
{
    [Fact]
    public async Task CompatibleApproachUsesLegacyVisibilityAndSuppressionRules()
    {
        var viewModel = new HumanSiteViewModel();
        var status = OnFootStatus(0, 0, 0);

        await viewModel.ApplyUpdateAsync([Parse(Approach())], status, "foot");

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Haberlandt Survey", viewModel.SiteName);
        Assert.Equal("Agriculture · type not identified",
            viewModel.TemplateText);
        Assert.False(viewModel.HasKnownGeometry);

        viewModel.SetStationInfoVisible(true);
        Assert.False(viewModel.ShouldShow);
        viewModel.SetStationInfoVisible(false);
        viewModel.SetActiveBuildProjects(true);
        Assert.False(viewModel.ShouldShow);
        viewModel.SuppressForActiveBuildProjects = false;
        Assert.True(viewModel.ShouldShow);

        viewModel.UpdateStatus(status with { GuiFocus = GuiFocus.Fss });
        Assert.False(viewModel.ShouldShow);
        viewModel.UpdateStatus(status with { GuiFocus = GuiFocus.RolePanel });
        Assert.True(viewModel.ShouldShow);
    }

    [Fact]
    public async Task FootPositionAtKnownPadInfersAlignedSettlementMap()
    {
        const double radius = 6_000_000;
        const double siteHeading = 231;
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Find(HumanSiteEconomy.Extraction, 5)!;
        var origin = new SurfaceCoordinate(-12.5, 44.25);
        var pad = Assert.Single(template.LandingPads);
        var observerHeading = SurfaceNavigation.NormalizeDegrees(
            siteHeading + pad.Rotation);
        var location = HumanSiteNavigation.GetSurfaceLocation(
            origin,
            pad.Offset,
            radius,
            siteHeading);
        var viewModel = new HumanSiteViewModel(templateCatalog: catalog);
        var status = OnFootStatus(
            location.Latitude,
            location.Longitude,
            (int)Math.Round(observerHeading)) with
        {
            PlanetRadius = (decimal)radius,
        };

        await viewModel.ApplyUpdateAsync(
            [
                Parse(Approach(
                    economy: "$economy_Extraction;",
                    economyLocalized: "Extraction",
                    latitude: origin.Latitude,
                    longitude: origin.Longitude)),
                Parse(
                    """
                    {"event":"DockingRequested","MarketID":12345,"StationType":"OnFootSettlement","LandingPads":{"Small":1,"Medium":0,"Large":0}}
                    """),
            ],
            status,
            "foot");

        Assert.True(viewModel.HasKnownGeometry);
        Assert.Equal(5, viewModel.ActiveSite!.SubType);
        Assert.Equal("Ourea", viewModel.ActiveSite.Template!.Name);
        Assert.Equal(siteHeading, viewModel.ActiveSite.Heading!.Value, 0);
        Assert.NotNull(viewModel.MapProjection);
        Assert.NotNull(viewModel.CommanderOffset);
        Assert.InRange(viewModel.DistanceToOriginMeters, 0, 500);
        Assert.Equal(2, viewModel.Zoom);
    }

    [Fact]
    public void AutomaticAndManualZoomMatchLegacyModePrecedence()
    {
        var viewModel = new HumanSiteViewModel();
        var exterior = OnFootStatus(0, 0, 0);

        viewModel.UpdateStatus(exterior);
        Assert.Equal(2, viewModel.Zoom);
        viewModel.UpdateStatus(exterior with
        {
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet,
        });
        Assert.Equal(4, viewModel.Zoom);
        viewModel.UpdateStatus(exterior with
        {
            SelectedWeapon = "$humanoid_companalyser_name;",
        });
        Assert.Equal(6, viewModel.Zoom);

        viewModel.AdjustZoom(zoomIn: false);
        Assert.False(viewModel.AutoZoom);
        Assert.Equal(5.8, viewModel.Zoom);
        viewModel.EnableAutomaticZoom();
        Assert.True(viewModel.AutoZoom);
        Assert.Equal(6, viewModel.Zoom);
    }

    [Fact]
    public async Task DockingDenialAndPoiSettingsAreExposedToOverlay()
    {
        var viewModel = new HumanSiteViewModel();
        var status = OnFootStatus(0, 0, 0);
        await viewModel.ApplyUpdateAsync(
            [
                Parse(Approach()),
                Parse(
                    """{"event":"DockingDenied","MarketID":12345,"Reason":"NoSpace"}"""),
            ],
            status,
            "foot");

        Assert.True(viewModel.HasDockingStatus);
        Assert.Equal("Docking denied · NoSpace", viewModel.DockingStatusText);

        viewModel.ShowMedkits = false;
        viewModel.ShowBatteries = false;
        viewModel.ShowDataTerminals = false;
        Assert.False(viewModel.ShowMedkits);
        Assert.False(viewModel.ShowBatteries);
        Assert.False(viewModel.ShowDataTerminals);
    }

    [Fact]
    public async Task SettlementMapTracksShipSrvAndDismissalBoundary()
    {
        const double radius = 6_000_000;
        const double siteHeading = 231;
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Find(HumanSiteEconomy.Extraction, 5)!;
        var origin = new SurfaceCoordinate(-12.5, 44.25);
        var pad = Assert.Single(template.LandingPads);
        var observerHeading = SurfaceNavigation.NormalizeDegrees(
            siteHeading + pad.Rotation);
        var shipLocation = HumanSiteNavigation.GetSurfaceLocation(
            origin,
            pad.Offset,
            radius,
            siteHeading);
        var landedStatus = OnFootStatus(
            shipLocation.Latitude,
            shipLocation.Longitude,
            (int)Math.Round(observerHeading)) with
        {
            PlanetRadius = (decimal)radius,
        };
        var viewModel = new HumanSiteViewModel(templateCatalog: catalog);
        await viewModel.ApplyUpdateAsync(
            [
                Parse(Approach(
                    economy: "$economy_Extraction;",
                    economyLocalized: "Extraction",
                    latitude: origin.Latitude,
                    longitude: origin.Longitude)),
                Parse(
                    """
                    {"event":"DockingRequested","MarketID":12345,"StationType":"OnFootSettlement","LandingPads":{"Small":1,"Medium":0,"Large":0}}
                    """),
                Parse($$"""
                    {"event":"Touchdown","Latitude":{{shipLocation.Latitude}},"Longitude":{{shipLocation.Longitude}}}
                    """),
            ],
            landedStatus,
            "sidewinder");

        Assert.NotNull(viewModel.ShipOffset);
        Assert.False(viewModel.HasShipDeparted);
        Assert.True(viewModel.ShowShipDismissalBoundary);

        var distantLocation = HumanSiteNavigation.GetSurfaceLocation(
            shipLocation,
            new HumanSiteMapPoint(0, 1_900),
            radius,
            siteHeading: 0);
        viewModel.UpdateStatus(landedStatus with
        {
            Latitude = distantLocation.Latitude,
            Longitude = distantLocation.Longitude,
        });
        Assert.InRange(viewModel.DistanceToShipMeters, 1_899, 1_901);
        Assert.True(viewModel.ShowShipDismissalWarning);

        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"Disembark","SRV":true}""")],
            landedStatus with
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                Flags2 = StatusFlags2.None,
            },
            "sidewinder");
        Assert.NotNull(viewModel.SrvOffset);

        await viewModel.ApplyUpdateAsync(
            [Parse("""{"event":"Liftoff"}""")],
            landedStatus,
            "sidewinder");
        Assert.True(viewModel.HasShipDeparted);
        Assert.False(viewModel.ShowShipDismissalBoundary);
        Assert.False(viewModel.ShowShipDismissalWarning);
    }

    private static EliteStatus OnFootStatus(
        double latitude,
        double longitude,
        int heading)
    {
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Flags2 = StatusFlags2.OnFoot
                | StatusFlags2.OnFootOnPlanet
                | StatusFlags2.OnFootExterior,
            GuiFocus = GuiFocus.NoFocus,
            Latitude = latitude,
            Longitude = longitude,
            Heading = heading,
            PlanetRadius = 6_000_000,
        };
    }

    private static string Approach(
        string economy = "$economy_Agri;",
        string economyLocalized = "Agriculture",
        double latitude = 0,
        double longitude = 0)
    {
        return $$"""
            {"timestamp":"2026-07-25T03:00:00Z","event":"ApproachSettlement","Name":"Haberlandt Survey","Name_Localised":"Haberlandt Survey","MarketID":12345,"SystemAddress":42,"BodyID":3,"BodyName":"Test 1","Latitude":{{latitude}},"Longitude":{{longitude}},"StationEconomy":"{{economy}}","StationEconomy_Localised":"{{economyLocalized}}","StationFaction":{"Name":"Raven Colonial","FactionState":"War"},"StationGovernment":"$government_Democracy;","StationGovernment_Localised":"Democracy","StationServices":["dock","refuel"]}
            """;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var value, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(value);
    }
}
