using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;

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
    private const int MaximumResponseBytes = 8 * 1024 * 1024;

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
        using var document = await BoundedHttpContent.ReadJsonDocumentAsync(
                response.Content,
                MaximumResponseBytes,
                "The Canonn system-POI response",
                cancellationToken)
            .ConfigureAwait(false);
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

public sealed class CachingCanonnSystemPoiClient(
    ICanonnSystemPoiClient inner) : ICanonnSystemPoiClient
{
    private readonly ICanonnSystemPoiClient inner = inner
        ?? throw new ArgumentNullException(nameof(inner));
    private readonly object gate = new();
    private readonly Dictionary<string, Task<CanonnSystemPoiResult>> requests =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<CanonnSystemPoiResult> GetAsync(
        string systemName,
        string commanderName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        var normalizedSystem = systemName.Trim();
        var normalizedCommander = commanderName?.Trim() ?? string.Empty;
        var key = normalizedSystem + "\n" + normalizedCommander;
        Task<CanonnSystemPoiResult> request;
        lock (gate)
        {
            if (!requests.TryGetValue(key, out request!))
            {
                var completion = new TaskCompletionSource<CanonnSystemPoiResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                request = completion.Task;
                requests.Add(key, request);
                _ = LoadAsync(
                    key,
                    normalizedSystem,
                    normalizedCommander,
                    completion);
            }
        }

        return await request.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadAsync(
        string key,
        string systemName,
        string commanderName,
        TaskCompletionSource<CanonnSystemPoiResult> completion)
    {
        try
        {
            var result = await inner.GetAsync(
                    systemName,
                    commanderName,
                    CancellationToken.None)
                .ConfigureAwait(false);
            completion.TrySetResult(result);
            lock (gate)
            {
                foreach (var oldKey in requests
                             .Where(entry => entry.Key != key
                                 && entry.Value.IsCompletedSuccessfully)
                             .Select(entry => entry.Key)
                             .Take(Math.Max(0, requests.Count - 8))
                             .ToArray())
                {
                    requests.Remove(oldKey);
                }
            }

        }
        catch (Exception exception)
        {
            lock (gate)
            {
                requests.Remove(key);
            }

            completion.TrySetException(exception);
        }
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
