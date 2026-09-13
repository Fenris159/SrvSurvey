using System.Globalization;
using System.Text;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Mining;

/// <summary>
/// Writes a stable, self-contained surface-mining survey table for spreadsheet and third-party imports.
/// Survey fields are repeated on every deposit row so consumers do not need to infer parent records.
/// </summary>
public static class MineMapCsvExporter
{
    public const int SchemaVersion = 1;

    private static readonly string[] Headers =
    [
        "SchemaVersion",
        "SurveyId",
        "FrontierId",
        "Commander",
        "System",
        "SystemAddress",
        "SystemX",
        "SystemY",
        "SystemZ",
        "Body",
        "BodyId",
        "BodyType",
        "ArrivalDistanceLs",
        "LocationSignalNumber",
        "LocationName",
        "SurveyNotes",
        "BorderRadiusKm",
        "PlanetRadiusKm",
        "CenterLatitude",
        "CenterLongitude",
        "SurveyCreatedUtc",
        "SurveyUpdatedUtc",
        "MarkerIndex",
        "MarkerId",
        "Commodity",
        "MineralAmount",
        "Density",
        "RigCount",
        "MarkerLatitude",
        "MarkerLongitude",
        "MarkerDistanceFromCenterKm",
        "MarkerBearingFromCenterDegrees",
        "MarkerCreatedUtc",
        "SplatTraceActive",
        "SplatBoundaryPointCount",
        "SuggestedRigLocationCount",
        "SplatBoundaryCoordinates",
        "SuggestedRigCoordinates",
    ];

    public static string Write(MineMapSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var csv = new StringBuilder();
        AppendRow(csv, Headers);
        if (survey.Markers.Count == 0)
        {
            AppendRow(csv, CreateRow(survey, null, null));
            return csv.ToString();
        }

        for (int index = 0; index < survey.Markers.Count; index++)
        {
            AppendRow(csv, CreateRow(survey, survey.Markers[index], index + 1));
        }

        return csv.ToString();
    }

    public static string CreateSuggestedFileName(MineMapSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        string body = GalacticBookmark.TrimSystemPrefix(survey.SystemName, survey.BodyName);
        string stem = $"{survey.SystemName}-{body}-signal-{survey.LocationSignal}-surface-mining";
        char[] invalid = [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        string safe = new(stem.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.Join('-', safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) + ".csv";
    }

    private static string?[] CreateRow(MineMapSurvey survey, MineMapMarker? marker, int? markerIndex)
    {
        double? markerDistance = marker is null
            ? null
            : SurfaceNavigation.GetDistance(survey.Center, marker.Location, survey.PlanetRadiusMeters) / 1000;
        double? markerBearing = marker is null ? null : SurfaceNavigation.GetBearing(survey.Center, marker.Location);
        return
        [
            FormatValue(SchemaVersion),
            survey.Id.ToString("D"),
            Text(survey.FrontierId),
            Text(survey.CommanderName),
            Text(survey.SystemName),
            FormatValue(survey.SystemAddress),
            FormatDouble(survey.SystemPosition.X, "0.#####"),
            FormatDouble(survey.SystemPosition.Y, "0.#####"),
            FormatDouble(survey.SystemPosition.Z, "0.#####"),
            Text(survey.BodyName),
            FormatValue(survey.BodyId),
            Text(survey.BodyType),
            FormatDouble(survey.ArrivalDistanceLs, "0.##"),
            FormatValue(survey.LocationSignal),
            Text(survey.Name),
            Text(survey.Notes),
            FormatDouble(survey.LocationRadiusMeters / 1000, "0.###"),
            FormatDouble(survey.PlanetRadiusMeters / 1000, "0.###"),
            FormatDouble(survey.Center.Latitude, "0.########"),
            FormatDouble(survey.Center.Longitude, "0.########"),
            Format(survey.CreatedAt),
            Format(survey.UpdatedAt),
            FormatOptional(markerIndex),
            marker?.Id.ToString("D"),
            Text(marker?.Material),
            marker?.MineralAmount.ToString(),
            marker?.Density.ToString(),
            FormatOptional(marker?.RigCount),
            FormatOptionalDouble(marker?.Location.Latitude, "0.########"),
            FormatOptionalDouble(marker?.Location.Longitude, "0.########"),
            FormatOptionalDouble(markerDistance, "0.###"),
            FormatOptionalDouble(markerBearing, "0.##"),
            marker is null ? null : Format(marker.CreatedAt),
            marker is null ? null : Format(marker.IsSplatTraceActive),
            marker is null ? null : FormatValue(marker.SplatBoundary.Count),
            marker is null ? null : FormatValue(marker.SuggestedRigLocations.Count),
            marker is null ? null : FormatCoordinates(marker.SplatBoundary),
            marker is null ? null : FormatCoordinates(marker.SuggestedRigLocations),
        ];
    }

    private static string FormatCoordinates(IReadOnlyList<SurfaceCoordinate> coordinates) =>
        "["
        + string.Join(
            ',',
            coordinates.Select(point =>
                $"[{FormatDouble(point.Latitude, "0.########")},{FormatDouble(point.Longitude, "0.########")}]"
            )
        )
        + "]";

    private static string FormatValue<T>(T value)
        where T : struct, IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

    private static string? FormatOptional<T>(T? value)
        where T : struct, IFormattable => value?.ToString(null, CultureInfo.InvariantCulture);

    private static string FormatDouble(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    private static string? FormatOptionalDouble(double? value, string format) =>
        value?.ToString(format, CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string Format(bool value) => value ? "true" : "false";

    private static string? Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        ReadOnlySpan<char> trimmed = value.AsSpan().TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> values)
    {
        csv.AppendJoin(',', values.Select(Escape));
        csv.Append("\r\n");
    }

    private static string Escape(string? value)
    {
        string text = value ?? string.Empty;
        return text.IndexOfAny([',', '"', '\r', '\n']) < 0 ? text : $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
