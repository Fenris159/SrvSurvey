using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteVehicleTrackerTests
{
    [Fact]
    public void TouchdownAndLiftoffRetainFormerShipLocation()
    {
        var tracker = new HumanSiteVehicleTracker();
        var status = Status(latitude: 12.5, longitude: -45.25, heading: 271);

        Assert.True(tracker.Apply(
            Event("Touchdown", """{"Latitude":12.4,"Longitude":-45.2}"""),
            status));
        Assert.Equal(new SurfaceCoordinate(12.4, -45.2), tracker.ShipLocation);
        Assert.Equal(271, tracker.ShipHeading);
        Assert.False(tracker.HasShipDeparted);

        Assert.True(tracker.Apply(Event("Liftoff"), status));
        Assert.Equal(new SurfaceCoordinate(12.4, -45.2), tracker.ShipLocation);
        Assert.True(tracker.HasShipDeparted);
    }

    [Fact]
    public void DockedUsesStatusLocationAndNormalizesHeading()
    {
        var tracker = new HumanSiteVehicleTracker();

        Assert.True(tracker.Apply(
            Event("Docked"),
            Status(latitude: 1, longitude: 2, heading: -5)));

        Assert.Equal(new SurfaceCoordinate(1, 2), tracker.ShipLocation);
        Assert.Equal(355, tracker.ShipHeading);
        Assert.False(tracker.HasShipDeparted);
    }

    [Fact]
    public void SrvDisembarkAndEmbarkTrackSurfaceLocation()
    {
        var tracker = new HumanSiteVehicleTracker();
        var status = Status(latitude: -10, longitude: 120, heading: 0);

        Assert.True(tracker.Apply(
            Event("Disembark", """{"SRV":true}"""),
            status));
        Assert.Equal(new SurfaceCoordinate(-10, 120), tracker.SrvLocation);

        Assert.False(tracker.Apply(
            Event("Embark", """{"SRV":false}"""),
            status));
        Assert.NotNull(tracker.SrvLocation);

        Assert.True(tracker.Apply(
            Event("Embark", """{"SRV":true}"""),
            status));
        Assert.Null(tracker.SrvLocation);
    }

    [Theory]
    [InlineData("LeaveBody")]
    [InlineData("FSDJump")]
    [InlineData("CarrierJump")]
    [InlineData("Shutdown")]
    [InlineData("Died")]
    [InlineData("Resurrect")]
    public void LeavingSurfaceContextClearsVehicleLocations(string eventName)
    {
        var tracker = new HumanSiteVehicleTracker();
        var status = Status(latitude: 1, longitude: 2, heading: 90);
        tracker.Apply(Event("Touchdown"), status);
        tracker.Apply(Event("Disembark", """{"SRV":true}"""), status);

        Assert.True(tracker.Apply(Event(eventName), status));

        Assert.Null(tracker.ShipLocation);
        Assert.Null(tracker.ShipHeading);
        Assert.Null(tracker.SrvLocation);
        Assert.False(tracker.HasShipDeparted);
    }

    [Fact]
    public void MainMenuMusicClearsVehicleLocations()
    {
        var tracker = new HumanSiteVehicleTracker();
        var status = Status(latitude: 1, longitude: 2, heading: 90);
        tracker.Apply(Event("Touchdown"), status);

        Assert.True(tracker.Apply(
            Event("Music", """{"MusicTrack":"MainMenu"}"""),
            status));

        Assert.Null(tracker.ShipLocation);
    }

    [Fact]
    public void InvalidCoordinatesAreIgnored()
    {
        var tracker = new HumanSiteVehicleTracker();

        Assert.False(tracker.Apply(
            Event("Touchdown", """{"Latitude":95,"Longitude":0}"""),
            null));
        Assert.Null(tracker.ShipLocation);
    }

    private static EliteStatus Status(
        double latitude,
        double longitude,
        int heading)
    {
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = latitude,
            Longitude = longitude,
            Heading = heading,
        };
    }

    private static JournalEventEnvelope Event(
        string eventName,
        string json = "{}")
    {
        using var document = JsonDocument.Parse(json);
        return new JournalEventEnvelope(
            eventName,
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            json,
            document.RootElement.Clone());
    }
}
