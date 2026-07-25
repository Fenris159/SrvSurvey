using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exobiology;

public interface ICodexDiscoveryLocationClient
{
    Task<CodexDiscoveryLocationLoadResult> GetAsync(
        long systemAddress,
        int bodyId,
        CancellationToken cancellationToken = default);
}

public sealed record CodexDiscoveryLocation(
    long SystemAddress,
    int BodyId,
    string SystemName,
    string BodyName,
    GalacticRegion? Region,
    GalacticCoordinate? Position,
    Uri SpanshUri);

public sealed record CodexDiscoveryLocationLoadResult(
    CodexDiscoveryLocation? Location,
    string? Error)
{
    public bool IsSuccess => Location is not null;

    public static CodexDiscoveryLocationLoadResult Failed(string error)
    {
        return new CodexDiscoveryLocationLoadResult(null, error);
    }
}

public sealed class CodexDiscoveryLocationClient : ICodexDiscoveryLocationClient
{
    private static readonly Uri DefaultBaseUri = new(
        "https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly TimeSpan requestTimeout;

    public CodexDiscoveryLocationClient(
        HttpClient? client = null,
        Uri? baseUri = null,
        TimeSpan? requestTimeout = null)
    {
        this.client = client ?? SharedClient;
        this.baseUri = baseUri ?? DefaultBaseUri;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                "The Codex location timeout must be positive.");
        }
    }

    public async Task<CodexDiscoveryLocationLoadResult> GetAsync(
        long systemAddress,
        int bodyId,
        CancellationToken cancellationToken = default)
    {
        if (systemAddress <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(systemAddress),
                "A positive system address is required.");
        }

        var requestUri = new Uri(
            baseUri,
            "dump/" + systemAddress.ToString(CultureInfo.InvariantCulture) + "/");
        using var timeoutCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(requestTimeout);
        var operationToken = timeoutCancellation.Token;
        try
        {
            using var response = await client.GetAsync(
                    requestUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    operationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(
                    operationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: operationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("system", out var system)
                || system.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "The Spansh dump has no system object.");
            }

            var systemName = GetString(system, "name")
                ?? systemAddress.ToString(CultureInfo.InvariantCulture);
            GalacticCoordinate? position = null;
            if (system.TryGetProperty("coords", out var coords)
                && coords.ValueKind == JsonValueKind.Object
                && GetDouble(coords, "x") is { } x
                && GetDouble(coords, "y") is { } y
                && GetDouble(coords, "z") is { } z)
            {
                position = new GalacticCoordinate(x, y, z);
            }

            string? bodyName = null;
            long? bodyAddress = null;
            if (system.TryGetProperty("bodies", out var bodies)
                && bodies.ValueKind == JsonValueKind.Array)
            {
                foreach (var body in bodies.EnumerateArray())
                {
                    if (body.ValueKind != JsonValueKind.Object
                        || GetInt32(body, "bodyId") != bodyId)
                    {
                        continue;
                    }

                    bodyName = GetString(body, "name");
                    bodyAddress = GetInt64(body, "id64");
                    break;
                }
            }

            var spanshUri = bodyAddress is > 0
                ? new Uri("https://spansh.co.uk/body/" + bodyAddress.Value
                    .ToString(CultureInfo.InvariantCulture))
                : new Uri("https://spansh.co.uk/system/" + systemAddress
                    .ToString(CultureInfo.InvariantCulture));
            return new CodexDiscoveryLocationLoadResult(
                new CodexDiscoveryLocation(
                    systemAddress,
                    bodyId,
                    systemName,
                    bodyName ?? $"{systemName} #{bodyId}",
                    position is null ? null : GalacticRegionMap.Find(position.Value),
                    position,
                    spanshUri),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CodexDiscoveryLocationLoadResult.Failed(
                "The Spansh location request timed out.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or JsonException
                or InvalidDataException)
        {
            return CodexDiscoveryLocationLoadResult.Failed(exception.Message);
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static double? GetDouble(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result))
        {
            return result;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result)
                    ? result
                    : null;
    }
}
