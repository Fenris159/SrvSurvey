using System.Text.Json;

namespace SrvSurvey.Core.Exobiology;

public interface ICanonnCodexChallengeClient
{
    Task<CanonnCodexChallengeLoadResult> GetAsync(
        string commanderName,
        CancellationToken cancellationToken = default);
}

public sealed record CanonnCodexChallengeGroup(
    string HudCategory,
    IReadOnlyList<string> FoundTypes);

public sealed record CanonnCodexChallengeLoadResult(
    IReadOnlyList<CanonnCodexChallengeGroup> Groups,
    string? Error)
{
    public bool IsSuccess => Error is null;

    public static CanonnCodexChallengeLoadResult Failed(string error)
    {
        return new CanonnCodexChallengeLoadResult([], error);
    }
}

public sealed class CanonnCodexChallengeClient : ICanonnCodexChallengeClient
{
    private static readonly Uri DefaultEndpoint = new(
        "https://us-central1-canonn-api-236217.cloudfunctions.net/"
            + "query/challenge/status");
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly HttpClient client;
    private readonly Uri endpoint;
    private readonly TimeSpan requestTimeout;

    public CanonnCodexChallengeClient(
        HttpClient? client = null,
        Uri? endpoint = null,
        TimeSpan? requestTimeout = null)
    {
        this.client = client ?? SharedClient;
        this.endpoint = endpoint ?? DefaultEndpoint;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                "The Canonn Challenge timeout must be positive.");
        }
    }

    public async Task<CanonnCodexChallengeLoadResult> GetAsync(
        string commanderName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        var requestUri = new UriBuilder(endpoint)
        {
            Query = "cmdr=" + Uri.EscapeDataString(commanderName.Trim()),
        }.Uri;
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "The Canonn Challenge response is not an object.");
            }

            var groups = new List<CanonnCodexChallengeGroup>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty("types_found", out var found)
                    || found.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var hudCategory = value.TryGetProperty(
                        "hud_category",
                        out var category)
                    && category.ValueKind == JsonValueKind.String
                        ? category.GetString()
                        : null;
                if (string.IsNullOrWhiteSpace(hudCategory))
                {
                    continue;
                }

                var foundTypes = found.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                groups.Add(new CanonnCodexChallengeGroup(
                    hudCategory,
                    foundTypes));
            }

            return new CanonnCodexChallengeLoadResult(groups, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CanonnCodexChallengeLoadResult.Failed(
                "The Canonn Challenge request timed out.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or JsonException
                or InvalidDataException)
        {
            return CanonnCodexChallengeLoadResult.Failed(exception.Message);
        }
    }
}

public sealed class CanonnCodexChallengeImporter(
    ICanonnCodexChallengeClient client,
    CommanderCodexStore store,
    ExobiologyReferenceCatalog catalog)
{
    private readonly ICanonnCodexChallengeClient client = client
        ?? throw new ArgumentNullException(nameof(client));
    private readonly CommanderCodexStore store = store
        ?? throw new ArgumentNullException(nameof(store));
    private readonly ExobiologyReferenceCatalog catalog = catalog
        ?? throw new ArgumentNullException(nameof(catalog));

    public async Task<CanonnCodexImportResult> ImportAsync(
        string frontierId,
        string commanderName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        var challenge = await client.GetAsync(commanderName, cancellationToken)
            .ConfigureAwait(false);
        if (!challenge.IsSuccess)
        {
            return CanonnCodexImportResult.Failed(
                challenge.Error ?? "Canonn Challenge data is unavailable.");
        }

        var matches = new HashSet<long>();
        var unmatched = 0;
        foreach (var group in challenge.Groups)
        {
            foreach (var foundType in group.FoundTypes)
            {
                var entry = catalog.Entries.FirstOrDefault(reference =>
                    string.Equals(
                        reference.DisplayName,
                        foundType,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        reference.HudCategory,
                        group.HudCategory,
                        StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    unmatched++;
                }
                else
                {
                    matches.Add(entry.EntryId);
                }
            }
        }

        var timestamp = DateTimeOffset.Now;
        var tracked = await store.TrackBatchAsync(
                frontierId,
                commanderName,
                matches.Select(entryId => new CommanderCodexDiscovery(
                        entryId,
                        timestamp,
                        -1,
                        -1))
                    .ToArray(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return tracked.IsSuccess
            ? new CanonnCodexImportResult(
                matches.Count,
                tracked.ChangedEntryCount,
                unmatched,
                null)
            : CanonnCodexImportResult.Failed(
                tracked.Error ?? "The Commander Codex ledger could not be updated.");
    }
}

public sealed record CanonnCodexImportResult(
    int MatchedEntryCount,
    int AddedEntryCount,
    int UnmatchedEntryCount,
    string? Error)
{
    public bool IsSuccess => Error is null;

    public static CanonnCodexImportResult Failed(string error)
    {
        return new CanonnCodexImportResult(0, 0, 0, error);
    }
}
