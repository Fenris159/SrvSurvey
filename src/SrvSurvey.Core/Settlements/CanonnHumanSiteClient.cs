using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public interface ICanonnHumanSiteClient
{
    Task<CanonnHumanSiteLookupResult> GetStationsAsync(
        long systemAddress,
        CancellationToken cancellationToken = default);
}

public interface ICanonnHumanSitePublisher
{
    Task<string> PublishStationAsync(
        CanonnHumanSiteSubmission submission,
        CancellationToken cancellationToken = default);
}

public sealed class CanonnHumanSiteClient
    : ICanonnHumanSiteClient, ICanonnHumanSitePublisher
{
    private const int MaximumRows = 4_096;
    private const int MaximumRawJsonCharacters = 128 * 1024;
    private const long MaximumResponseBytes = 8L * 1024 * 1024;
    private static readonly Uri DefaultBaseUri = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/query/srvsurvey/system/");
    private static readonly Uri DefaultPublicationUri = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/postEvent/srvsurvey/stations");
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly Uri publicationUri;

    public CanonnHumanSiteClient(
        HttpClient? client = null,
        Uri? baseUri = null,
        Uri? publicationUri = null)
    {
        this.client = client ?? SharedClient;
        this.baseUri = baseUri ?? DefaultBaseUri;
        this.publicationUri = publicationUri ?? DefaultPublicationUri;
    }

    public async Task<CanonnHumanSiteLookupResult> GetStationsAsync(
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        if (systemAddress <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(systemAddress));
        }

        var uri = new Uri(
            baseUri,
            systemAddress.ToString(CultureInfo.InvariantCulture));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return CanonnHumanSiteLookupResult.Empty;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(response, uri, cancellationToken)
            .ConfigureAwait(false);
        return Parse(bytes, systemAddress);
    }

    public async Task<string> PublishStationAsync(
        CanonnHumanSiteSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ValidateSubmission(submission);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            publicationUri)
        {
            Content = JsonContent.Create(
                new
                {
                    timestamp = submission.Timestamp,
                    clientVer = submission.ClientVersion.ToString(),
                    name = submission.Name,
                    marketId = submission.MarketId,
                    systemAddress = submission.SystemAddress,
                    bodyId = submission.BodyId,
                    stationEconomy = submission.EconomyToken,
                    stationType = submission.StationType,
                    lat = submission.Location.Latitude,
                    @long = submission.Location.Longitude,
                    heading = submission.Heading,
                    subType = submission.SubType,
                    calcMethod = submission.GeometrySource.ToString(),
                    cmdrHeading = submission.CommanderHeading,
                    cmdrLat = submission.CommanderLocation.Latitude,
                    cmdrLong = submission.CommanderLocation.Longitude,
                    cmdrShip = submission.CommanderVehicle,
                    cmdrPad = submission.CommanderPad,
                    bodyRadius = submission.BodyRadiusMeters,
                    availblePads = new
                    {
                        Large = submission.AvailablePads.Large,
                        Medium = submission.AvailablePads.Medium,
                        Small = submission.AvailablePads.Small,
                    },
                },
                options: new JsonSerializerOptions()),
        };
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        var responseText = await ReadBoundedTextAsync(
                response,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var compact = responseText.Trim().ReplaceLineEndings(" ");
            if (compact.Length > 512)
            {
                compact = compact[..512] + "...";
            }

            var detail = string.IsNullOrWhiteSpace(compact)
                ? string.Empty
                : ": " + compact;
            throw new HttpRequestException(
                $"Canonn rejected the settlement geometry "
                    + $"({(int)response.StatusCode} {response.ReasonPhrase}){detail}.",
                null,
                response.StatusCode);
        }

        return responseText;
    }

    internal static CanonnHumanSiteLookupResult Parse(
        byte[] bytes,
        long expectedSystemAddress)
    {
        if (expectedSystemAddress <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSystemAddress));
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The Canonn settlement response is not an array.");
            }

            var stations = new Dictionary<long, HumanSiteKnowledge>();
            var warnings = new List<string>();
            var index = 0;
            foreach (var row in document.RootElement.EnumerateArray())
            {
                index++;
                if (index > MaximumRows)
                {
                    throw new InvalidDataException(
                        "The Canonn settlement response contains too many rows.");
                }

                if (row.ValueKind != JsonValueKind.Object
                    || !TryGetProperty(row, "raw_json", out var rawValue)
                    || rawValue.ValueKind != JsonValueKind.String)
                {
                    warnings.Add($"Canonn settlement row {index:N0} has no raw_json payload.");
                    continue;
                }

                var raw = rawValue.GetString();
                if (string.IsNullOrWhiteSpace(raw)
                    || raw.Length > MaximumRawJsonCharacters)
                {
                    warnings.Add($"Canonn settlement row {index:N0} has an invalid payload size.");
                    continue;
                }

                try
                {
                    using var stationDocument = JsonDocument.Parse(raw);
                    if (!TryReadStation(
                            stationDocument.RootElement,
                            expectedSystemAddress,
                            out var station))
                    {
                        warnings.Add($"Canonn settlement row {index:N0} is incomplete or incompatible.");
                        continue;
                    }

                    stations.TryAdd(station!.MarketId, station);
                }
                catch (JsonException)
                {
                    warnings.Add($"Canonn settlement row {index:N0} contains malformed JSON.");
                }
            }

            return new CanonnHumanSiteLookupResult(
                stations.Values.ToArray(),
                warnings);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The Canonn settlement response is not valid JSON.",
                exception);
        }
    }

    private static bool TryReadStation(
        JsonElement root,
        long expectedSystemAddress,
        out HumanSiteKnowledge? station)
    {
        station = null;
        var name = ReadString(root, "name");
        var marketId = ReadInt64(root, "marketId");
        var bodyId = ReadInt32(root, "bodyId");
        var economyToken = ReadString(root, "stationEconomy");
        var economy = HumanSiteEconomyParser.ParseJournalValue(economyToken);
        var latitude = ReadDouble(root, "lat");
        var longitude = ReadDouble(root, "long");
        if (root.ValueKind != JsonValueKind.Object
            || string.IsNullOrWhiteSpace(name)
            || marketId is not > 0
            || ReadInt64(root, "systemAddress") != expectedSystemAddress
            || bodyId is not >= 0
            || economy == HumanSiteEconomy.Unknown
            || latitude is not >= -90 or > 90
            || longitude is not >= -180 or > 180)
        {
            return false;
        }

        var subType = Math.Max(0, ReadInt32(root, "subType") ?? 0);
        var heading = ReadDouble(root, "heading");
        if (heading is not null)
        {
            heading = !double.IsFinite(heading.Value) || heading < 0
                ? null
                : SurfaceNavigation.NormalizeDegrees(heading.Value);
        }

        station = new HumanSiteKnowledge(
            name.Trim(),
            marketId.Value,
            expectedSystemAddress,
            bodyId.Value,
            economy,
            economyToken!,
            new HumanSiteSurfaceLocation(latitude.Value, longitude.Value),
            subType,
            heading,
            ReadLandingPads(root),
            ReadGeometrySource(root));
        return true;
    }

    private static HumanSiteLandingPads ReadLandingPads(JsonElement root)
    {
        if (!(TryGetProperty(root, "availblePads", out var pads)
                || TryGetProperty(root, "availablePads", out pads))
            || pads.ValueKind != JsonValueKind.Object)
        {
            return HumanSiteLandingPads.Empty;
        }

        return new HumanSiteLandingPads(
            Math.Max(0, ReadInt32(pads, "Small") ?? 0),
            Math.Max(0, ReadInt32(pads, "Medium") ?? 0),
            Math.Max(0, ReadInt32(pads, "Large") ?? 0));
    }

    private static HumanSiteGeometrySource ReadGeometrySource(JsonElement root)
    {
        return Enum.TryParse<HumanSiteGeometrySource>(
            ReadString(root, "calcMethod"),
            ignoreCase: true,
            out var source)
                ? source
                : HumanSiteGeometrySource.Unknown;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? ReadInt32(JsonElement root, string propertyName)
    {
        var value = ReadInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static long? ReadInt64(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                    ? number
                    : null;
    }

    private static double? ReadDouble(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
                    ? number
                    : null;
    }

    private static bool TryGetProperty(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                $"Canonn settlement response exceeded {MaximumResponseBytes:N0} bytes: {uri}");
        }

        await using var input = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        await using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    $"Canonn settlement response exceeded {MaximumResponseBytes:N0} bytes: {uri}");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const long maximumBytes = 64 * 1024;
        if (response.Content.Headers.ContentLength is > maximumBytes)
        {
            throw new InvalidDataException(
                "The Canonn publication response exceeded the safety limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        await using var output = new MemoryStream();
        var buffer = new byte[8 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "The Canonn publication response exceeded the safety limit.");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private static void ValidateSubmission(CanonnHumanSiteSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.Timestamp == default
            || submission.ClientVersion is null
            || string.IsNullOrWhiteSpace(submission.Name)
            || submission.MarketId <= 0
            || submission.SystemAddress <= 0
            || submission.BodyId < 0
            || string.IsNullOrWhiteSpace(submission.EconomyToken)
            || HumanSiteEconomyParser.ParseJournalValue(
                submission.EconomyToken) == HumanSiteEconomy.Unknown
            || string.IsNullOrWhiteSpace(submission.StationType)
            || !double.IsFinite(submission.Location.Latitude)
            || submission.Location.Latitude is < -90 or > 90
            || !double.IsFinite(submission.Location.Longitude)
            || submission.Location.Longitude is < -180 or > 180
            || submission.SubType <= 0
            || !double.IsFinite(submission.Heading)
            || submission.Heading is < 0 or >= 360
            || submission.GeometrySource == HumanSiteGeometrySource.Unknown
            || !double.IsFinite(submission.CommanderHeading)
            || submission.CommanderHeading is < 0 or >= 360
            || !double.IsFinite(submission.CommanderLocation.Latitude)
            || submission.CommanderLocation.Latitude is < -90 or > 90
            || !double.IsFinite(submission.CommanderLocation.Longitude)
            || submission.CommanderLocation.Longitude is < -180 or > 180
            || string.IsNullOrWhiteSpace(submission.CommanderVehicle)
            || submission.CommanderPad < 0
            || !double.IsFinite(submission.BodyRadiusMeters)
            || submission.BodyRadiusMeters <= 0
            || submission.AvailablePads.Small < 0
            || submission.AvailablePads.Medium < 0
            || submission.AvailablePads.Large < 0)
        {
            throw new ArgumentException(
                "The Canonn settlement submission is incomplete or invalid.",
                nameof(submission));
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }
}

public sealed record CanonnHumanSiteLookupResult(
    IReadOnlyList<HumanSiteKnowledge> Stations,
    IReadOnlyList<string> Warnings)
{
    public static CanonnHumanSiteLookupResult Empty { get; } = new([], []);
}

public sealed record CanonnHumanSiteSubmission(
    DateTimeOffset Timestamp,
    Version ClientVersion,
    string Name,
    long MarketId,
    long SystemAddress,
    int BodyId,
    string EconomyToken,
    string StationType,
    HumanSiteSurfaceLocation Location,
    int SubType,
    double Heading,
    HumanSiteGeometrySource GeometrySource,
    double CommanderHeading,
    HumanSiteSurfaceLocation CommanderLocation,
    string CommanderVehicle,
    int CommanderPad,
    double BodyRadiusMeters,
    HumanSiteLandingPads AvailablePads);

public sealed record CanonnHumanSitePublicationResult(
    CanonnHumanSiteSubmission? Published,
    string? Warning);
