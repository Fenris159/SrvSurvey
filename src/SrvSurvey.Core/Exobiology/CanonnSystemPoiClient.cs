using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Exobiology;

public interface ICanonnSystemPoiClient
{
    Task<CanonnSystemPoiResult> GetAsync(
        string systemName,
        string commanderName,
        CancellationToken cancellationToken = default);
}

public sealed class CanonnSystemPoiClient : ICanonnSystemPoiClient
{
    private static readonly Uri DefaultBaseUri = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/query/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri baseUri;

    public CanonnSystemPoiClient(
        HttpClient? client = null,
        Uri? baseUri = null)
    {
        this.client = client ?? SharedClient;
        this.baseUri = EnsureTrailingSlash(baseUri ?? DefaultBaseUri);
    }

    public async Task<CanonnSystemPoiResult> GetAsync(
        string systemName,
        string commanderName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        var requestUri = new Uri(
            baseUri,
            "getSystemPoi?system="
                + Uri.EscapeDataString(systemName.Trim())
                + "&odyssey=Y&cmdr="
                + Uri.EscapeDataString(commanderName?.Trim() ?? string.Empty));
        using var response = await client.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Canonn returned an invalid system POI response.");
        }

        var returnedSystem = GetString(root, "system");
        if (!string.IsNullOrWhiteSpace(returnedSystem)
            && !string.Equals(
                returnedSystem.Trim(),
                systemName.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Canonn returned POIs for {returnedSystem.Trim()} instead of "
                    + systemName.Trim()
                    + ".");
        }

        var signals = new List<CanonnSurfaceBiologySignal>();
        if (root.TryGetProperty("codex", out var codex)
            && codex.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in codex.EnumerateArray())
            {
                if (TryReadSignal(entry, out var signal))
                {
                    signals.Add(signal);
                }
            }
        }

        return new CanonnSystemPoiResult(
            string.IsNullOrWhiteSpace(returnedSystem)
                ? systemName.Trim()
                : returnedSystem.Trim(),
            signals);
    }

    private static bool TryReadSignal(
        JsonElement entry,
        out CanonnSurfaceBiologySignal signal)
    {
        signal = null!;
        if (entry.ValueKind != JsonValueKind.Object
            || !string.Equals(
                GetString(entry, "hud_category"),
                "Biology",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = GetString(entry, "body")?.Trim();
        var entryId = GetInt64(entry, "entryid");
        var latitude = GetDouble(entry, "latitude");
        var longitude = GetDouble(entry, "longitude");
        if (string.IsNullOrWhiteSpace(body)
            || entryId is not > 0
            || latitude is not >= -90 or > 90
            || longitude is not >= -180 or > 180)
        {
            return false;
        }

        signal = new CanonnSurfaceBiologySignal(
            body,
            GetString(entry, "english_name")?.Trim(),
            entryId.Value,
            new SurfaceCoordinate(latitude.Value, longitude.Value),
            GetBoolean(entry, "scanned") ?? false);
        return true;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
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

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var parsed)
                ? parsed
                : null;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        return uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SrvSurvey-Avalonia/1.0");
        return client;
    }
}

public sealed record CanonnSystemPoiResult(
    string SystemName,
    IReadOnlyList<CanonnSurfaceBiologySignal> Signals);

public sealed record CanonnSurfaceBiologySignal(
    string BodyName,
    string? DisplayName,
    long EntryId,
    SurfaceCoordinate Location,
    bool IsCommanderScan);
