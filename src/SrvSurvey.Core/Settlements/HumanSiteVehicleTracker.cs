using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteVehicleTracker
{
    public SurfaceCoordinate? ShipLocation { get; private set; }

    public double? ShipHeading { get; private set; }

    public bool HasShipDeparted { get; private set; }

    public SurfaceCoordinate? SrvLocation { get; private set; }

    public int Version { get; private set; }

    public bool Apply(
        JournalEventEnvelope journalEvent,
        EliteStatus? status)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var changed = journalEvent.EventName switch
        {
            "Touchdown" => SetShipLocation(
                GetCoordinate(journalEvent.Payload) ?? GetCoordinate(status),
                status?.NormalizedHeading),
            "Docked" => SetShipLocation(
                GetCoordinate(status),
                status?.NormalizedHeading),
            "Liftoff" or "ShipDismissed" => MarkShipDeparted(),
            "Disembark" => ApplyDisembark(journalEvent.Payload, status),
            "Embark" => ApplyEmbark(journalEvent.Payload),
            "LeaveBody" or "StartJump" or "SupercruiseEntry" or "FSDJump"
                or "CarrierJump" or "Shutdown" or "Died" or "Resurrect" =>
                Clear(),
            "Music" when string.Equals(
                GetString(journalEvent.Payload, "MusicTrack"),
                "MainMenu",
                StringComparison.Ordinal) => Clear(),
            _ => false,
        };
        if (changed)
        {
            Version++;
        }

        return changed;
    }

    private bool SetShipLocation(
        SurfaceCoordinate? location,
        double? heading)
    {
        if (location is null)
        {
            return false;
        }

        var normalizedHeading = heading is { } value
            ? SurfaceNavigation.NormalizeDegrees(value)
            : ShipHeading;
        if (ShipLocation == location
            && EquivalentHeading(ShipHeading, normalizedHeading)
            && !HasShipDeparted)
        {
            return false;
        }

        ShipLocation = location;
        ShipHeading = normalizedHeading;
        HasShipDeparted = false;
        return true;
    }

    private bool MarkShipDeparted()
    {
        if (ShipLocation is null || HasShipDeparted)
        {
            return false;
        }

        HasShipDeparted = true;
        return true;
    }

    private bool ApplyDisembark(JsonElement root, EliteStatus? status)
    {
        if (!(GetBoolean(root, "SRV") ?? false)
            || GetCoordinate(status) is not { } location
            || SrvLocation == location)
        {
            return false;
        }

        SrvLocation = location;
        return true;
    }

    private bool ApplyEmbark(JsonElement root)
    {
        if (!(GetBoolean(root, "SRV") ?? false) || SrvLocation is null)
        {
            return false;
        }

        SrvLocation = null;
        return true;
    }

    private bool Clear()
    {
        if (ShipLocation is null
            && ShipHeading is null
            && !HasShipDeparted
            && SrvLocation is null)
        {
            return false;
        }

        ShipLocation = null;
        ShipHeading = null;
        HasShipDeparted = false;
        SrvLocation = null;
        return true;
    }

    private static SurfaceCoordinate? GetCoordinate(EliteStatus? status)
    {
        return status?.HasLatitudeLongitude == true
            ? CreateCoordinate(status.Latitude, status.Longitude)
            : null;
    }

    private static SurfaceCoordinate? GetCoordinate(JsonElement root)
    {
        var latitude = GetDouble(root, "Latitude");
        var longitude = GetDouble(root, "Longitude");
        return latitude is not null && longitude is not null
            ? CreateCoordinate(latitude.Value, longitude.Value)
            : null;
    }

    private static SurfaceCoordinate? CreateCoordinate(
        double latitude,
        double longitude)
    {
        try
        {
            return new SurfaceCoordinate(latitude, longitude);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number)
                ? number
                : null;
    }

    private static bool EquivalentHeading(double? left, double? right)
    {
        if (left.HasValue != right.HasValue)
        {
            return false;
        }

        return !left.HasValue
            || Math.Abs(left.Value - right!.Value) <= 0.0001d;
    }
}
