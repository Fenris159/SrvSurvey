using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
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
    private const int MaximumResponseBytes = 32 * 1024 * 1024;

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
        this.baseUri = baseUri ?? WellKnownUris.SpanshApiBase;
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

        var requestUri = CreateRequestUri(systemAddress);
        using var timeoutCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(requestTimeout);
        return await LoadLocationSafelyAsync(
                requestUri,
                systemAddress,
                bodyId,
                timeoutCancellation.Token,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Uri CreateRequestUri(long systemAddress)
    {
        return new Uri(
            baseUri,
            UriPath.CombineWithTrailingSeparator(
                "dump",
                systemAddress.ToString(CultureInfo.InvariantCulture)));
    }

    private async Task<CodexDiscoveryLocationLoadResult> LoadLocationSafelyAsync(
        Uri requestUri,
        long systemAddress,
        int bodyId,
        CancellationToken operationToken,
        CancellationToken callerToken)
    {
        try
        {
            return await LoadLocationAsync(
                    requestUri,
                    systemAddress,
                    bodyId,
                    operationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
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

    private async Task<CodexDiscoveryLocationLoadResult> LoadLocationAsync(
        Uri requestUri,
        long systemAddress,
        int bodyId,
        CancellationToken operationToken)
    {
        using var response = await client.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                operationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await BoundedHttpContent.ReadJsonDocumentAsync(
                response.Content,
                MaximumResponseBytes,
                "The Spansh system-dump response",
                operationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("system", out var system)
            || system.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The Spansh dump has no system object.");
        }

        var systemName = GetString(system, "name")
            ?? systemAddress.ToString(CultureInfo.InvariantCulture);
        var position = TryReadPosition(system);
        var (bodyName, bodyAddress) = FindBody(system, bodyId);
        var spanshUri = CreateSpanshUri(systemAddress, bodyAddress);
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

    private static GalacticCoordinate? TryReadPosition(JsonElement system)
    {
        if (system.TryGetProperty("coords", out var coords)
            && coords.ValueKind == JsonValueKind.Object
            && GetDouble(coords, "x") is { } x
            && GetDouble(coords, "y") is { } y
            && GetDouble(coords, "z") is { } z)
        {
            return new GalacticCoordinate(x, y, z);
        }

        return null;
    }

    private static (string? BodyName, long? BodyAddress) FindBody(
        JsonElement system,
        int bodyId)
    {
        if (!system.TryGetProperty("bodies", out var bodies)
            || bodies.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        foreach (var body in bodies.EnumerateArray())
        {
            if (body.ValueKind != JsonValueKind.Object
                || GetInt32(body, "bodyId") != bodyId)
            {
                continue;
            }

            return (GetString(body, "name"), GetInt64(body, "id64"));
        }

        return (null, null);
    }

    private static Uri CreateSpanshUri(long systemAddress, long? bodyAddress)
    {
        if (bodyAddress is > 0)
        {
            return new Uri(
                WellKnownUris.SpanshBodyPrefix
                    + bodyAddress.Value.ToString(CultureInfo.InvariantCulture));
        }

        return new Uri(
            WellKnownUris.SpanshSystemPrefix
                + systemAddress.ToString(CultureInfo.InvariantCulture));
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
