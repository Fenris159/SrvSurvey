using System.Globalization;
using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public interface ICanonnHumanSiteClient
{
    Task<CanonnHumanSiteLookupResult> GetStationsAsync(
        long systemAddress,
        CancellationToken cancellationToken = default);
}

public sealed class CanonnHumanSiteClient : ICanonnHumanSiteClient
{
    private const int MaximumRows = 4_096;
    private const int MaximumRawJsonCharacters = 128 * 1024;
    private const long MaximumResponseBytes = 8L * 1024 * 1024;
    private static readonly Uri DefaultBaseUri = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/query/srvsurvey/system/");
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient client;
    private readonly Uri baseUri;

    public CanonnHumanSiteClient(
        HttpClient? client = null,
        Uri? baseUri = null)
    {
        this.client = client ?? SharedClient;
        this.baseUri = baseUri ?? DefaultBaseUri;
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
