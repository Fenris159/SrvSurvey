using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public interface INearestSystemsClient
{
    Task<NearestSystemsSearchResult> SearchCanonnAsync(
        GalacticCoordinate reference,
        string codexName,
        string commanderName,
        int limit = 20,
        CancellationToken cancellationToken = default
    );

    Task<NearestSystemsSearchResult> SearchLandmarksAsync(
        GalacticCoordinate reference,
        CodexLandmarkFilter filter,
        CancellationToken cancellationToken = default
    );

    Task<NearestSystemsSearchResult> SearchCodexAsync(
        GalacticCoordinate reference,
        CodexSearchQuery query,
        string commanderName,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Finds published Codex locations without a distance cutoff, preserving the entry's provider taxonomy.</summary>
public sealed partial class NearestSystemsClient : INearestSystemsClient
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const int ResultLimit = 20;
    private static readonly Uri DefaultCanonnBaseUri = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/query/"
    );
    private static readonly Uri DefaultSpanshBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private static readonly CodexSearchCatalog CodexCatalog = new();
    private readonly HttpClient client;
    private readonly Uri canonnBaseUri;
    private readonly Uri spanshBaseUri;

    public NearestSystemsClient(HttpClient? client = null, Uri? canonnBaseUri = null, Uri? spanshBaseUri = null)
    {
        this.client = client ?? SharedClient;
        this.canonnBaseUri = EnsureTrailingSlash(canonnBaseUri ?? DefaultCanonnBaseUri);
        this.spanshBaseUri = EnsureTrailingSlash(spanshBaseUri ?? DefaultSpanshBaseUri);
    }

    public async Task<NearestSystemsSearchResult> SearchCanonnAsync(
        GalacticCoordinate reference,
        string codexName,
        string commanderName,
        int limit = ResultLimit,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexName);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, ResultLimit);
        Uri uri = CreateCanonnUri(
            "nearest/codex",
            new Dictionary<string, string>
            {
                ["x"] = reference.X.ToString("R", CultureInfo.InvariantCulture),
                ["y"] = reference.Y.ToString("R", CultureInfo.InvariantCulture),
                ["z"] = reference.Z.ToString("R", CultureInfo.InvariantCulture),
                ["name"] = codexName.Trim(),
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            }
        );
        CanonnNearest payload = await ReadAsync<CanonnNearest>(uri, "Canonn nearest-Codex", cancellationToken)
            .ConfigureAwait(false);
        CanonnNearestEntry[] nearest = (payload.Nearest ?? [])
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.System) && ValidLocation(entry.Distance, entry.X, entry.Y, entry.Z)
            )
            .OrderBy(entry => entry.Distance)
            .DistinctBy(entry => entry.System, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
        NearestSystemSearchRow[] rows = nearest
            .Select(entry => new NearestSystemSearchRow(
                entry.System!,
                entry.Distance,
                (entry.EnglishName ?? codexName.Trim()) + " · reported Codex location",
                new(entry.X, entry.Y, entry.Z),
                null,
                NearestSystemSource.Canonn
            ))
            .ToArray();
        return new(rows, null)
        {
            CoverageNote =
                "Canonn reports candidate locations; its nearest ordering is approximate. No distance cutoff is applied.",
        };
    }

    private async Task<T> ReadAsync<T>(Uri uri, string operation, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await BoundedHttpContent
                .ReadFromJsonAsync<T>(
                    response.Content,
                    MaximumResponseBytes,
                    operation,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false)
            ?? throw new InvalidDataException($"{operation} returned an empty response.");
    }

    private Uri CreateCanonnUri(string relativePath, IReadOnlyDictionary<string, string> query) =>
        new UriBuilder(new Uri(canonnBaseUri, relativePath))
        {
            Query = string.Join(
                "&",
                query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")
            ),
        }.Uri;

    private static bool ValidLocation(double distance, double x, double y, double z) =>
        distance >= 0 && double.IsFinite(distance) && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("The service URI must be absolute.");
        }

        return UriPath.EnsureTrailingSeparator(uri);
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }

    private sealed record CanonnNearest(IReadOnlyList<CanonnNearestEntry>? Nearest);

    private sealed record CanonnNearestEntry(
        double Distance,
        string? System,
        double X,
        double Y,
        double Z,
        [property: JsonPropertyName("english_name")] string? EnglishName
    );
}

public sealed record NearestSystemsSearchResult(
    IReadOnlyList<NearestSystemSearchRow> Rows,
    string? SpanshSearchReference
)
{
    public string? CoverageNote { get; init; }
}

public sealed record NearestSystemSearchRow(
    string SystemName,
    double Distance,
    string Notes,
    GalacticCoordinate Coordinate,
    long? SystemAddress,
    NearestSystemSource Source
)
{
    public string? SpanshSearchReference { get; init; }
}

public enum NearestSystemSource
{
    Canonn,
    Spansh,
    Combined,
}
