using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationSystemSiteJournalTrackerTests
{
    [Fact]
    public void SignalsCreateMappedSitesAndSkipNonStationSignals()
    {
        var nextId = 10L;
        var tracker = Tracker(() => nextId++);
        var sites = new List<ColonizationSystemSite>();

        var changed = tracker.ApplyJournalEvents(sites,
        [
            Event("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Alpha Hub","SignalType":"StationCoriolis"}"""),
            Event("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Carrier","SignalType":"FleetCarrier"}"""),
            Event("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"$MULTIPLAYER_SCENARIO42;","SignalName_Localised":"Resource site","SignalType":"Installation"}"""),
            Event("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Beta Construction Site","SignalType":"Installation"}"""),
        ]);

        Assert.Equal(1, changed);
        var site = Assert.Single(sites);
        Assert.Equal("y10", site.Id);
        Assert.Equal("Alpha Hub", site.Name);
        Assert.Equal(-1, site.BodyNumber);
        Assert.Equal("no_truss?", site.BuildType);
        Assert.Equal(ColonizationSystemSiteStatus.Complete, site.Status);
    }

    [Fact]
    public void OtherSystemEventsAreRejected()
    {
        var tracker = Tracker();
        var sites = new List<ColonizationSystemSite>();

        var changed = tracker.ApplyJournalEvent(
            sites,
            Event("""{"event":"FSSSignalDiscovered","SystemAddress":99,"SignalName":"Wrong Port","SignalType":"Outpost"}"""));

        Assert.False(changed);
        Assert.Empty(sites);
    }

    [Fact]
    public void ScanProgressCombinesDiscoveryAndBodyScans()
    {
        var tracker = Tracker();
        var sites = new List<ColonizationSystemSite>();

        tracker.ApplyJournalEvents(sites,
        [
            Event("""{"event":"FSSDiscoveryScan","SystemAddress":42,"BodyCount":2}"""),
            Event("""{"event":"Scan","SystemAddress":42,"BodyID":1}"""),
            Event("""{"event":"ScanBaryCentre","SystemAddress":42,"BodyID":2}"""),
            Event("""{"event":"NavBeaconScan","SystemAddress":42,"NumBodies":2}"""),
        ]);

        Assert.True(tracker.HasDiscoveryScan);
        Assert.True(tracker.HasNavBeaconScan);
        Assert.Equal(2, tracker.ExpectedBodyCount);
        Assert.Equal(2, tracker.ScannedBodyCount);
        Assert.True(tracker.IsBodyScanComplete);
    }

    [Fact]
    public void StatusRequiresExplicitCaptureBeforeCreatingSurfaceSite()
    {
        var nextId = 20L;
        var tracker = Tracker(() => nextId++);
        var sites = new List<ColonizationSystemSite>();
        var status = new EliteStatus
        {
            Destination = new StatusDestination
            {
                System = 42,
                Body = 2,
                Name = "Surface Point",
            },
        };

        Assert.False(tracker.ApplyStatusDestination(
            sites,
            status,
            captureUnknownSurfaceSite: false));
        Assert.True(tracker.ApplyStatusDestination(
            sites,
            status,
            captureUnknownSurfaceSite: true));

        var site = Assert.Single(sites);
        Assert.Equal("y20", site.Id);
        Assert.Equal(2, site.BodyNumber);
        Assert.Equal("settlement?", site.BuildType);
    }

    [Fact]
    public void StatusUpdatesKnownSignalWithoutCaptureMode()
    {
        var tracker = Tracker();
        var sites = new List<ColonizationSystemSite>();
        tracker.ApplyJournalEvent(
            sites,
            Event("""{"event":"FSSSignalDiscovered","SystemAddress":42,"SignalName":"Orbital One","SignalType":"Outpost"}"""));

        var changed = tracker.ApplyStatusDestination(
            sites,
            new EliteStatus
            {
                Destination = new StatusDestination
                {
                    System = 42,
                    Body = 1,
                    Name = "Orbital One",
                },
            },
            captureUnknownSurfaceSite: false);

        Assert.True(changed);
        Assert.Equal(1, Assert.Single(sites).BodyNumber);
    }

    [Fact]
    public void ApproachAndDockedEnrichExistingSite()
    {
        var tracker = Tracker();
        var sites = new List<ColonizationSystemSite>
        {
            Site("one", "Odyssey Point", body: -1),
        };

        Assert.True(tracker.ApplyJournalEvent(
            sites,
            Event("""{"event":"ApproachSettlement","SystemAddress":42,"Name":"Odyssey Point","BodyID":2,"MarketID":123}""")));
        Assert.True(tracker.ApplyJournalEvent(
            sites,
            Event("""{"event":"Docked","SystemAddress":42,"StationName":"Odyssey Point","MarketID":456,"StationType":"CraterPort"}""")));

        var site = Assert.Single(sites);
        Assert.Equal(2, site.BodyNumber);
        Assert.Equal(456, site.MarketId);
        Assert.Equal("aphrodite?", site.BuildType);
    }

    [Theory]
    [InlineData(3, 1, "$economy_Industrial;", "plutus")]
    [InlineData(4, 1, "$economy_Industrial;", "vesta")]
    [InlineData(1, 1, "$economy_HighTech;", "prometheus")]
    [InlineData(1, 1, "$economy_Industrial;", "vulcan")]
    [InlineData(1, 1, "$economy_Military;", "nemesis")]
    [InlineData(1, 1, "$economy_Service;", "dysnomia")]
    public void DockedOutpostUsesLegacyPadAndEconomyMappings(
        int small,
        int medium,
        string economy,
        string expected)
    {
        var tracker = Tracker();
        var sites = new List<ColonizationSystemSite>
        {
            Site("one", "Orbital One", body: 1),
        };
        var json = $$"""
            {"event":"Docked","SystemAddress":42,"StationName":"Orbital One","MarketID":12,"StationType":"Outpost","StationEconomy":"{{economy}}","LandingPads":{"Small":{{small}},"Medium":{{medium}},"Large":0},"StationEconomies":[{"Name":"{{economy}}","Proportion":100.0}]}
            """;

        Assert.True(tracker.ApplyJournalEvent(sites, Event(json)));

        Assert.Equal(expected, Assert.Single(sites).BuildType);
    }

    private static ColonizationSystemSiteJournalTracker Tracker(
        Func<long>? nextId = null)
    {
        return new ColonizationSystemSiteJournalTracker(
            42,
            "Test System",
            [0, 1, 2],
            nextId);
    }

    private static ColonizationSystemSite Site(
        string id,
        string name,
        int body)
    {
        return new ColonizationSystemSite
        {
            Id = id,
            Name = name,
            BodyNumber = body,
            Status = ColonizationSystemSiteStatus.Plan,
        };
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }
}
