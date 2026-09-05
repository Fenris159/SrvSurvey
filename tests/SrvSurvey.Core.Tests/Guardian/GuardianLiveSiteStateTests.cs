using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianLiveSiteStateTests
{
    [Fact]
    public void ApproachSettlementMatchesEmbeddedRuinReference()
    {
        var state = new GuardianLiveSiteState(GuardianSiteCatalog.LoadEmbedded());

        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"Location","StarSystem":"Synuefe XR-H d11-102","SystemAddress":3515254557027}""")));
        Assert.True(state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:05:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":3515254557027,"BodyID":13,"BodyName":"Synuefe XR-H d11-102 1 b","Latitude":-46.576923,"Longitude":133.985107}""")));

        var site = Assert.IsType<GuardianLiveSiteSnapshot>(state.CurrentSite);
        Assert.Equal(GuardianSiteKind.Ruins, site.Kind);
        Assert.Equal(1, site.Index);
        Assert.Equal("Beta", site.SiteType);
        Assert.Equal("Synuefe XR-H d11-102", site.SystemName);
        Assert.Equal("GR 1", site.Reference?.DisplayId);
        Assert.Equal(
            new GuardianSurfaceLocation(-46.576923, 133.985107),
            site.Location);
        Assert.True(site.IsKnownReference);
    }

    [Theory]
    [InlineData("$Ancient_Tiny_001:#index=1;", "Lacrosse")]
    [InlineData("$Ancient_Tiny_002:#index=1;", "Crossroads")]
    [InlineData("$Ancient_Tiny_003:#index=1;", "Fistbump")]
    [InlineData("$Ancient_Small_001:#index=1;", "Hammerbot")]
    [InlineData("$Ancient_Small_002:#index=1;", "Bear")]
    [InlineData("$Ancient_Small_003:#index=1;", "Bowl")]
    [InlineData("$Ancient_Small_005:#index=1;", "Turtle")]
    [InlineData("$Ancient_Medium_001:#index=1;", "Robolobster")]
    [InlineData("$Ancient_Medium_002:#index=1;", "Squid")]
    [InlineData("$Ancient_Medium_003:#index=1;", "Stickyhand")]
    public void UnknownStructuresInferLegacySiteType(string name, string siteType)
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));

        Assert.True(state.Apply(Parse($$"""
            {"timestamp":"2026-07-24T10:00:00Z","event":"ApproachSettlement","Name":"{{name}}","Name_Localised":"Guardian Structure","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":1.25,"Longitude":-2.5}
            """)));

        Assert.Equal(siteType, state.CurrentSite?.SiteType);
        Assert.Equal(GuardianSiteKind.Structure, state.CurrentSite?.Kind);
    }

    [Fact]
    public void FsdJumpSuppliesSystemNameForUnknownSite()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));

        state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"FSDJump","StarSystem":"Test System","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:05:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=12;","SystemAddress":42,"BodyID":7,"BodyName":"Test System A 1","Latitude":1,"Longitude":2}"""));

        Assert.Equal("Test System", state.CurrentSite?.SystemName);
        Assert.Equal(12, state.CurrentSite?.Index);
    }

    [Fact]
    public void RepeatedVisitPreservesFirstAndAdvancesLastTimestamp()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        var first = Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=12;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":1,"Longitude":2}""");
        var second = Parse(
            """{"timestamp":"2026-07-24T11:00:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=12;","Name_Localised":"Ancient Ruins (12)","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":3,"Longitude":4}""");

        state.Apply(first);
        state.Apply(second);

        Assert.Equal(first.Timestamp, state.CurrentSite?.FirstVisited);
        Assert.Equal(second.Timestamp, state.CurrentSite?.LastVisited);
        Assert.Equal("Ancient Ruins (12)", state.CurrentSite?.LocalizedName);
        Assert.Equal(new GuardianSurfaceLocation(3, 4), state.CurrentSite?.Location);
    }

    [Theory]
    [InlineData("Human Settlement")]
    [InlineData("$Ancient:#index=x;")]
    [InlineData("$Ancient:#index=0;")]
    [InlineData("$Ancient_Tiny_001")]
    public void NonGuardianOrMalformedSettlementIsIgnored(string name)
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));

        var applied = state.Apply(Parse($$"""
            {"timestamp":"2026-07-24T10:00:00Z","event":"ApproachSettlement","Name":"{{name}}","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1"}
            """));

        Assert.False(applied);
        Assert.Null(state.CurrentSite);
    }

    [Theory]
    [InlineData("StartJump")]
    [InlineData("SupercruiseEntry")]
    [InlineData("Shutdown")]
    [InlineData("CarrierJump")]
    [InlineData("Died")]
    [InlineData("Resurrect")]
    public void DepartureClearsCurrentSite(string eventName)
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"timestamp":"2026-07-24T10:00:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1"}"""));

        Assert.True(state.Apply(Parse($$"""
            {"timestamp":"2026-07-24T11:00:00Z","event":"{{eventName}}","StarSystem":"Elsewhere","SystemAddress":99}
            """)));

        Assert.Null(state.CurrentSite);
    }

    [Fact]
    public void MainMenuMusicClearsCurrentSite()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1"}"""));

        Assert.True(state.Apply(Parse(
            """{"event":"Music","MusicTrack":"MainMenu"}""")));

        Assert.Null(state.CurrentSite);
    }

    [Fact]
    public void ProximityUsesLegacyAltitudeGateInsteadOfHorizontalCutoff()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""));

        Assert.True(state.Apply(Parse(
            """{"event":"SupercruiseExit","StarSystem":"Test","SystemAddress":42}""")));
        Assert.NotNull(state.CurrentSite);

        var near = SurfaceStatus(latitude: 0.01);
        var far = SurfaceStatus(latitude: 0.5);
        Assert.False(state.SynchronizeProximity(near, retainDuringGlide: false));
        Assert.False(state.SynchronizeProximity(far, retainDuringGlide: false));
        Assert.NotNull(state.CurrentSite);
        Assert.True(state.SynchronizeProximity(
            far with { Altitude = 4_001 },
            retainDuringGlide: false));
        Assert.Null(state.CurrentSite);
        Assert.True(state.SynchronizeProximity(near, retainDuringGlide: false));
        Assert.NotNull(state.CurrentSite);
    }

    [Fact]
    public void StatusRestoresAndSwitchesNearestCatalogSiteWithoutApproachEvent()
    {
        var first = CreateReference(1, 0);
        var second = CreateReference(2, 0.02);
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.SetRecoveryReferences([first, second]);
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42,"Body":"Test A 1"}"""));

        Assert.True(state.SynchronizeProximity(
            SurfaceStatus(latitude: 0.001),
            retainDuringGlide: false));
        Assert.Equal(1, state.CurrentSite?.Index);
        Assert.Equal("$Ancient:#index=1;", state.CurrentSite?.Name);

        Assert.True(state.SynchronizeProximity(
            SurfaceStatus(latitude: 0.019),
            retainDuringGlide: false));
        Assert.Equal(2, state.CurrentSite?.Index);
    }

    [Fact]
    public void GlideRetainsSiteAndHumanSettlementClearsIt()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""));

        Assert.False(state.SynchronizeProximity(
            SurfaceStatus(latitude: 1),
            retainDuringGlide: true));
        Assert.NotNull(state.CurrentSite);
        Assert.True(state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"Human Settlement","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1"}""")));
        Assert.Null(state.CurrentSite);
    }

    [Fact]
    public void SurveyUpdatePreservesCollectedDataAndCorrectedSurfaceOrigin()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"timestamp":"2026-07-24T11:00:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=12;","Name_Localised":"Ancient Ruins (12)","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":3,"Longitude":4}"""));
        var existing = new GuardianCommanderSiteSurvey(
            "existing.json",
            "$Ancient:#index=12;",
            string.Empty,
            "Old name",
            DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-01T09:00:00Z"),
            "Beta",
            12,
            42,
            "Test",
            7,
            "Test A 1",
            "keep this note",
            false,
            new GuardianSurveyData
            {
                SiteType = "Beta",
                SiteHeading = 123,
                RelicTowerHeading = 45,
                Location = new GuardianSurfaceLocation(1, 2),
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["p1"] = GuardianPoiStatus.Present,
                },
                RelicHeadings = new Dictionary<string, int>
                {
                    ["t1"] = 90,
                },
            },
            [new GuardianObelisk("A01", "H1", true, ["ca"])],
            new HashSet<char> { 'A' })
        {
            MapMarkerOffset = new GuardianMapPoint(4, -6),
        };

        var survey = state.CreateOrUpdateSurvey("Drew", legacy: true, existing);

        Assert.Equal(existing.FirstVisited, survey.FirstVisited);
        Assert.Equal(state.CurrentSite?.LastVisited, survey.LastVisited);
        Assert.Equal("Drew", survey.Commander);
        Assert.Equal("Beta", survey.SiteType);
        Assert.Equal("keep this note", survey.Notes);
        Assert.Equal(123, survey.Survey.SiteHeading);
        Assert.Equal(new GuardianSurfaceLocation(1, 2), survey.Survey.Location);
        Assert.Equal(GuardianPoiStatus.Present, survey.Survey.PoiStatuses["p1"]);
        Assert.Single(survey.ActiveObelisks);
        Assert.Contains('A', survey.ObeliskGroups);
        Assert.Equal(new GuardianMapPoint(4, -6), survey.MapMarkerOffset);
        Assert.True(survey.Legacy);
    }

    [Fact]
    public void SurveyUpdateRejectsDifferentExistingSite()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"timestamp":"2026-07-24T11:00:00Z","event":"ApproachSettlement","Name":"$Ancient:#index=2;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1"}"""));
        var existing = new GuardianCommanderSiteSurvey(
            string.Empty,
            "$Ancient:#index=1;",
            string.Empty,
            string.Empty,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            "Unknown",
            1,
            42,
            "Test",
            7,
            "Test A 1",
            string.Empty,
            false,
            new GuardianSurveyData(),
            [],
            new HashSet<char>());

        Assert.Throws<ArgumentException>(
            () => state.CreateOrUpdateSurvey("Drew", false, existing));
    }

    [Fact]
    public void CreateOrUpdateSurveyRequiresActiveSite()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        Assert.Throws<InvalidOperationException>(
            () => state.CreateOrUpdateSurvey("Drew", legacy: false));
    }

    [Fact]
    public void HighAltitudeClearsRecoveredSiteDuringProximitySync()
    {
        var state = new GuardianLiveSiteState(
            new GuardianSiteCatalog([CreateReference(1, latitude: 0)]));
        state.Apply(Parse(
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""));
        Assert.NotNull(state.CurrentSite);

        Assert.True(state.SynchronizeProximity(
            new EliteStatus
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
                BodyName = "Test A 1",
                Latitude = 0,
                Longitude = 0,
                PlanetRadius = 1_000_000,
                Altitude = 4_500,
            },
            retainDuringGlide: false));
        Assert.Null(state.CurrentSite);
    }

    [Fact]
    public void SupercruiseExitUpdatesSystemWithoutClearingSite()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""));
        Assert.True(state.Apply(Parse(
            """{"event":"SupercruiseExit","StarSystem":"Test","SystemAddress":42,"Body":"Test A 1","BodyID":7}""")));
        Assert.NotNull(state.CurrentSite);
    }

    [Fact]
    public void CarrierJumpClearsCurrentSite()
    {
        var state = new GuardianLiveSiteState(new GuardianSiteCatalog([]));
        state.Apply(Parse(
            """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","SystemAddress":42,"BodyID":7,"BodyName":"Test A 1","Latitude":0,"Longitude":0}"""));
        Assert.True(state.Apply(Parse(
            """{"event":"CarrierJump","StarSystem":"Other","SystemAddress":99}""")));
        Assert.Null(state.CurrentSite);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        var success = JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private static EliteStatus SurfaceStatus(double latitude)
    {
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
            Latitude = latitude,
            Longitude = 0,
            PlanetRadius = 1_000_000,
            BodyName = "Test A 1",
        };
    }

    private static GuardianSiteReference CreateReference(int index, double latitude)
    {
        return new GuardianSiteReference(
            index,
            GuardianSiteKind.Ruins,
            "Test",
            42,
            "A 1",
            7,
            "Alpha",
            index,
            0,
            new SrvSurvey.Core.Search.GalacticCoordinate(0, 0, 0),
            latitude,
            0,
            90,
            0,
            100,
            null,
            null,
            null);
    }
}
