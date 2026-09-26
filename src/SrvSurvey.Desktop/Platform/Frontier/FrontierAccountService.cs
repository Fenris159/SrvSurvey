using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Inara;

namespace SrvSurvey.Desktop.Platform.Frontier;

public interface IFrontierAccountService : IDisposable
{
    event EventHandler? AuthorizationCallbackReceived;

    void SetActiveCommander(string? frontierId, string? commanderName);

    void SetInaraApiKey(string? apiKey) { }

    Task<IReadOnlyList<FrontierLinkedCommander>> GetLinkedCommandersAsync(
        CancellationToken cancellationToken = default
    );

    Task<FrontierAccountState> GetStateAsync(CancellationToken cancellationToken = default);

    Task<FrontierAccountSnapshot> ConnectAsync(CancellationToken cancellationToken = default);

    Task CancelConnectionAsync(CancellationToken cancellationToken = default);

    Task<FrontierAccountSnapshot> RefreshAsync(CancellationToken cancellationToken = default);

    Task<FrontierAccountSnapshot> RefreshAsync(bool forceCarrierRefresh, CancellationToken cancellationToken = default);

    Task UnlinkAsync(CancellationToken cancellationToken = default);
}

public sealed record FrontierAccountState(
    bool IsLinked,
    FrontierAccountSnapshot? Snapshot,
    DateTimeOffset? LastCapiRefreshAt,
    DateTimeOffset? LastCapiAttemptAt = null
);

public sealed record FrontierLinkedCommander(string FrontierId, string CommanderName);

public sealed record FrontierAccountServiceOptions(
    FrontierProfileCacheStore? LegacyCache = null,
    Func<DateTimeOffset>? UtcNow = null,
    Func<Uri, CancellationToken, Task>? OpenBrowser = null,
    Func<CancellationToken, Task>? RegisterProtocol = null,
    IInaraCommunityGoalClient? InaraCommunityGoals = null
);

internal sealed record FrontierCommanderIdentity(string FrontierId, string CommanderName)
{
    public static FrontierCommanderIdentity? Create(string? frontierId, string? commanderName)
    {
        string? normalizedId = frontierId?.Trim().ToUpperInvariant();
        if (
            normalizedId is null
            || normalizedId.Length < 2
            || normalizedId[0] != 'F'
            || !normalizedId[1..].All(char.IsAsciiDigit)
        )
        {
            return null;
        }

        return new FrontierCommanderIdentity(normalizedId, commanderName?.Trim() ?? string.Empty);
    }

    public bool Matches(FrontierAccountSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return !string.IsNullOrWhiteSpace(CommanderName)
            && string.Equals(CommanderName, snapshot.CommanderName, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FrontierRefreshCooldownException(TimeSpan remaining)
    : InvalidOperationException(
        $"Please wait {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} seconds before refreshing Frontier again."
    )
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class FrontierAccountService : IFrontierAccountService
{
    public const string ClientId = "66818020-d5ee-4c33-b909-b2632506a937";

    private const string AuthorizationEndpoint = "https://auth.frontierstore.net/auth";
    private const string TokenEndpoint = "https://auth.frontierstore.net/token";
    private const string ProfileEndpoint = "https://companion.orerve.net/profile?language=en";
    private const string CarrierEndpoint = "https://companion.orerve.net/fleetcarrier?language=en";
    private const string SquadronEndpoint = "https://companion.orerve.net/squadron?language=en";
    private const string MarketEndpoint = "https://companion.orerve.net/market?language=en";
    private const string ShipyardEndpoint = "https://companion.orerve.net/shipyard?language=en";
    private const string CommunityGoalsEndpoint = "https://companion.orerve.net/communitygoals?language=en";
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinimumCarrierRefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumSquadronRefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumCapiRequestSpacing = TimeSpan.FromMilliseconds(650);
    private const long MaximumTokenResponseBytes = 1024 * 1024;
    private const long MaximumCapiResponseBytes = 16 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly IFrontierCredentialStore credentials;
    private readonly Func<string, FrontierProfileCacheStore> cacheFactory;
    private readonly FrontierProfileCacheStore? legacyCache;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<Uri, CancellationToken, Task> openBrowser;
    private readonly Func<CancellationToken, Task> registerProtocol;
    private readonly IInaraCommunityGoalClient? inaraCommunityGoals;
    private readonly SemaphoreSlim capiGate = new(1, 1);
    private DateTimeOffset? lastCapiRequestAt;
    private FrontierCommanderIdentity? activeCommander;
    private bool disposed;

    public event EventHandler? AuthorizationCallbackReceived;

    public FrontierAccountService(
        HttpClient httpClient,
        IFrontierCredentialStore credentials,
        FrontierProfileCacheStore cache,
        FrontierAccountServiceOptions? options = null
    )
        : this(httpClient, credentials, _ => cache, options)
    {
        ArgumentNullException.ThrowIfNull(cache);
    }

    public FrontierAccountService(
        HttpClient httpClient,
        IFrontierCredentialStore credentials,
        Func<string, FrontierProfileCacheStore> cacheFactory,
        FrontierAccountServiceOptions? options = null
    )
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        this.cacheFactory = cacheFactory ?? throw new ArgumentNullException(nameof(cacheFactory));
        options ??= new FrontierAccountServiceOptions();
        legacyCache = options.LegacyCache;
        utcNow = options.UtcNow ?? (() => DateTimeOffset.UtcNow);
        openBrowser = options.OpenBrowser ?? OpenBrowserAsync;
        registerProtocol = options.RegisterProtocol ?? FrontierProtocolRegistration.RegisterCurrentAsync;
        inaraCommunityGoals = options.InaraCommunityGoals;
    }

    public static FrontierAccountService CreateCurrent(string dataDirectory)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        string version = typeof(FrontierAccountService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"SrvSurvey/{version} (+https://github.com/nithomson/SrvSurvey)"
        );
        string? inaraApiKey = InaraApplicationKeyProvider.GetApplicationKey();
        IInaraCommunityGoalClient? inaraCommunityGoals = string.IsNullOrWhiteSpace(inaraApiKey)
            ? null
            : new InaraCommunityGoalClient(
                client,
                inaraApiKey,
                version,
                Path.Combine(dataDirectory, "inara-community-goals.json")
            );
        return new FrontierAccountService(
            client,
            FrontierCredentialStore.CreateCurrent(dataDirectory),
            frontierId => new FrontierProfileCacheStore(
                Path.Combine(dataDirectory, "frontier-profile-cache", frontierId + ".json")
            ),
            new FrontierAccountServiceOptions(
                LegacyCache: new FrontierProfileCacheStore(Path.Combine(dataDirectory, "frontier-profile-cache.json")),
                InaraCommunityGoals: inaraCommunityGoals
            )
        );
    }

    public void SetActiveCommander(string? frontierId, string? commanderName)
    {
        ThrowIfDisposed();
        activeCommander = FrontierCommanderIdentity.Create(frontierId, commanderName);
    }

    public void SetInaraApiKey(string? apiKey)
    {
        if (inaraCommunityGoals is IInaraCommunityGoalApiKeySink sink)
        {
            sink.SetPersonalApiKey(apiKey);
        }
    }

    public async Task<IReadOnlyList<FrontierLinkedCommander>> GetLinkedCommandersAsync(
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        FrontierCredentialDocument? document = activeCommander is { } commander
            ? await LoadAndMigrateLegacyAsync(commander, cancellationToken).ConfigureAwait(false)
            : await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        Dictionary<string, string> linked = CollectLinkedCommanderIds(document);
        await ResolveLinkedCommanderNamesAsync(linked, cancellationToken).ConfigureAwait(false);
        return linked
            .Select(pair => new FrontierLinkedCommander(
                pair.Key,
                string.IsNullOrWhiteSpace(pair.Value) ? pair.Key : pair.Value
            ))
            .OrderBy(commander => commander.CommanderName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(commander => commander.FrontierId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private Dictionary<string, string> CollectLinkedCommanderIds(FrontierCredentialDocument document)
    {
        var linked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            KeyValuePair<string, FrontierAccountCredential> account in document.Accounts.Where(pair =>
                pair.Value.IsLinked
            )
        )
        {
            var identity = FrontierCommanderIdentity.Create(account.Key, null);
            if (identity is not null)
            {
                linked[identity.FrontierId] = string.Empty;
            }
        }

        var legacy = FrontierCommanderIdentity.Create(document.LegacyFrontierId, document.LegacyCommanderName);
        if (document.IsLinked && legacy is not null)
        {
            linked.TryAdd(legacy.FrontierId, legacy.CommanderName);
        }

        if (document.IsLinked && activeCommander is { } active && LegacyMayBelongTo(document, active))
        {
            linked.TryAdd(active.FrontierId, active.CommanderName);
        }

        return linked;
    }

    private async Task ResolveLinkedCommanderNamesAsync(
        Dictionary<string, string> linked,
        CancellationToken cancellationToken
    )
    {
        foreach (string? frontierId in linked.Keys.ToArray())
        {
            FrontierCommanderIdentity identity = FrontierCommanderIdentity.Create(frontierId, null)!;
            try
            {
                FrontierAccountSnapshot? snapshot = await CacheFor(identity)
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is not null)
                {
                    linked[frontierId] = snapshot.CommanderName;
                }
            }
            catch (JsonException)
            {
                // Keep the account selectable by its stable Frontier ID even
                // when its optional display cache needs to be refreshed.
            }

            if (
                string.IsNullOrWhiteSpace(linked[frontierId])
                && activeCommander is { } current
                && string.Equals(current.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase)
            )
            {
                linked[frontierId] = current.CommanderName;
            }
        }
    }

    public async Task<FrontierAccountState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FrontierCommanderIdentity? commander = activeCommander;
        if (commander is null)
        {
            return new FrontierAccountState(false, null, null);
        }

        FrontierCredentialDocument document = await LoadAndMigrateLegacyAsync(commander, cancellationToken)
            .ConfigureAwait(false);
        LoadedCredential? loaded = FindCredential(document, commander);
        if (loaded?.Credential.IsLinked != true)
        {
            return new FrontierAccountState(false, null, null);
        }

        FrontierAccountSnapshot? snapshot = null;
        FrontierProfileCacheStore cache = CacheFor(commander);
        try
        {
            snapshot = await cache.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && !commander.Matches(snapshot))
            {
                snapshot = null;
                if (!string.IsNullOrWhiteSpace(commander.CommanderName))
                {
                    await cache.ClearAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (JsonException)
        {
            await cache.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        if (snapshot is not null)
        {
            snapshot = await TryEnrichCommunityGoalsAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        return new FrontierAccountState(
            true,
            snapshot,
            loaded.Credential.LastCapiRefreshAt,
            loaded.Credential.LastCapiAttemptAt
        );
    }

    public async Task<FrontierAccountSnapshot> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FrontierCommanderIdentity commander = RequireActiveCommander();
        EnsureCommanderNameIsAvailable(commander);
        await registerProtocol(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = utcNow();
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var pending = new FrontierPendingAuthorization(
            state,
            verifier,
            now,
            commander.FrontierId,
            commander.CommanderName
        );
        await SavePendingAuthorizationAsync(pending, cancellationToken).ConfigureAwait(false);

        Uri authorizationUri = BuildAuthorizationUri(challenge, state);
        try
        {
            await openBrowser(authorizationUri, cancellationToken).ConfigureAwait(false);
            await WaitForAuthorizationAsync(state, commander.FrontierId, cancellationToken).ConfigureAwait(false);
            return await RefreshAsync(commander, forceCarrierRefresh: false, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ClearPendingAuthorizationAsync(state, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task CancelConnectionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FrontierCommanderIdentity? commander = activeCommander;
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument? document = await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (document is null || commander is null)
        {
            return;
        }

        FrontierPendingAuthorization? pending = AllPending(document)
            .Where(candidate =>
                string.Equals(candidate.FrontierId, commander.FrontierId, StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(candidate => candidate.StartedAt)
            .FirstOrDefault();
        if (pending is null)
        {
            return;
        }

        await credentials
            .SaveAsync(
                CompleteAuthorization(
                    document,
                    pending,
                    new FrontierAuthorizationResult(
                        pending.State,
                        false,
                        "Frontier authorization was cancelled.",
                        utcNow()
                    )
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task<FrontierAccountSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(forceCarrierRefresh: false, cancellationToken);
    }

    public async Task<FrontierAccountSnapshot> RefreshAsync(
        bool forceCarrierRefresh,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        return await RefreshAsync(RequireActiveCommander(), forceCarrierRefresh, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<FrontierAccountSnapshot> RefreshAsync(
        FrontierCommanderIdentity commander,
        bool forceCarrierRefresh,
        CancellationToken cancellationToken
    )
    {
        EnsureCommanderNameIsAvailable(commander);
        FrontierProfileCacheStore cache = CacheFor(commander);
        await using IAsyncDisposable refreshLease = await cache
            .AcquireRefreshLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument document = await LoadAndMigrateLegacyAsync(commander, cancellationToken)
            .ConfigureAwait(false);
        LoadedCredential? loaded = FindCredential(document, commander);
        if (loaded?.Credential.IsLinked != true)
        {
            throw new InvalidOperationException("Connect your Frontier account before refreshing this page.");
        }

        FrontierAccountCredential credential = loaded.Credential;

        DateTimeOffset now = utcNow();
        DateTimeOffset? lastRequest = Latest(credential.LastCapiRefreshAt, credential.LastCapiAttemptAt);
        if (lastRequest is { } priorRequest)
        {
            TimeSpan remaining = MinimumRefreshInterval - (now - priorRequest);
            if (remaining > TimeSpan.Zero)
            {
                throw new FrontierRefreshCooldownException(remaining);
            }
        }

        credential = credential with { LastCapiAttemptAt = now };
        await SaveAccountCredentialAsync(commander, credential, loaded.IsLegacy, cancellationToken)
            .ConfigureAwait(false);

        credential = await EnsureAccessTokenAsync(commander, credential, loaded.IsLegacy, false, cancellationToken)
            .ConfigureAwait(false);
        FrontierAccountSnapshot? previousSnapshot = null;
        try
        {
            previousSnapshot = await cache.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await cache.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        CapiResponse profile = await RequestCapiAsync(
                ProfileEndpoint,
                commander,
                credential,
                loaded.IsLegacy,
                allowNoContent: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        credential = profile.Credential;
        DateTimeOffset fetchedAt = utcNow();
        FrontierAccountSnapshot snapshot = FrontierCapiSnapshotParser.Parse(
            profile.Content ?? throw new InvalidDataException("Frontier did not return commander profile data."),
            null,
            fetchedAt
        );
        if (!commander.Matches(snapshot))
        {
            await RejectMismatchedAuthorizationAsync(commander, snapshot, loaded.IsLegacy, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Frontier authorized {snapshot.CommanderName}, but the active journal belongs to {commander.CommanderName}. No authorization was attached to the active commander."
            );
        }

        if (loaded.IsLegacy)
        {
            await MigrateLegacyCredentialAsync(commander, credential, previousSnapshot, cancellationToken)
                .ConfigureAwait(false);
            loaded = new LoadedCredential(credential, false);
        }

        bool carrierDue =
            forceCarrierRefresh
            || previousSnapshot?.CarrierFetchedAt is not { } carrierFetched
            || now - carrierFetched >= MinimumCarrierRefreshInterval;
        OptionalCapiResponse carrier = carrierDue
            ? await TryRequestOptionalCapiAsync(
                    CarrierEndpoint,
                    commander,
                    credential,
                    loaded.IsLegacy,
                    cancellationToken
                )
                .ConfigureAwait(false)
            : OptionalCapiResponse.Skipped(credential);
        credential = carrier.Credential;
        bool squadronDue =
            forceCarrierRefresh
            || previousSnapshot?.SquadronCarrierFetchedAt is not { } squadronFetched
            || now - squadronFetched >= MinimumSquadronRefreshInterval;
        OptionalCapiResponse squadron = squadronDue
            ? await TryRequestOptionalCapiAsync(
                    SquadronEndpoint,
                    commander,
                    credential,
                    loaded.IsLegacy,
                    cancellationToken
                )
                .ConfigureAwait(false)
            : OptionalCapiResponse.Skipped(credential);
        credential = squadron.Credential;
        OptionalCapiResponse market = await TryRequestOptionalCapiAsync(
                MarketEndpoint,
                commander,
                credential,
                loaded.IsLegacy,
                cancellationToken
            )
            .ConfigureAwait(false);
        credential = market.Credential;
        OptionalCapiResponse shipyard = await TryRequestOptionalCapiAsync(
                ShipyardEndpoint,
                commander,
                credential,
                loaded.IsLegacy,
                cancellationToken
            )
            .ConfigureAwait(false);
        credential = shipyard.Credential;
        OptionalCapiResponse communityGoals = await TryRequestOptionalCapiAsync(
                CommunityGoalsEndpoint,
                commander,
                credential,
                loaded.IsLegacy,
                cancellationToken
            )
            .ConfigureAwait(false);
        credential = communityGoals.Credential;

        snapshot = ApplyCarrierResult(snapshot, previousSnapshot, carrier, fetchedAt);
        snapshot = ApplySquadronResult(snapshot, previousSnapshot, squadron, fetchedAt);
        snapshot = ApplyMarketResult(snapshot, previousSnapshot, market, fetchedAt);
        snapshot = ApplyShipyardResult(snapshot, previousSnapshot, shipyard, fetchedAt);
        snapshot = ApplyCommunityGoalsResult(snapshot, previousSnapshot, communityGoals, fetchedAt);
        snapshot = await TryEnrichCommunityGoalsAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await cache.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await SaveAccountCredentialAsync(
                commander,
                credential with
                {
                    LastCapiRefreshAt = fetchedAt,
                    LastCapiAttemptAt = credential.LastCapiAttemptAt,
                },
                isLegacy: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        return snapshot;
    }

    private async Task<OptionalCapiResponse> TryRequestOptionalCapiAsync(
        string endpoint,
        FrontierCommanderIdentity commander,
        FrontierAccountCredential credential,
        bool isLegacy,
        CancellationToken cancellationToken
    )
    {
        try
        {
            CapiResponse response = await RequestCapiAsync(
                    endpoint,
                    commander,
                    credential,
                    isLegacy,
                    allowNoContent: true,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return new OptionalCapiResponse(response.Credential, response.Content, string.Empty, true, true);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.UnprocessableEntity)
        {
            FrontierCredentialDocument? latestDocument = await credentials
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            LoadedCredential? latest = FindCredential(latestDocument, commander);
            return new OptionalCapiResponse(
                latest?.Credential.IsLinked == true ? latest.Credential : credential,
                null,
                exception.Message,
                true,
                false
            );
        }
        catch (InvalidDataException exception)
        {
            return new OptionalCapiResponse(credential, null, exception.Message, true, false);
        }
    }

    private static FrontierAccountSnapshot ApplyCarrierResult(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        OptionalCapiResponse result,
        DateTimeOffset fetchedAt
    )
    {
        previous = IsSameCommander(snapshot, previous) ? previous : null;
        if (!result.Queried)
        {
            return PreserveCarrierState(snapshot, previous, previous?.CarrierError ?? string.Empty);
        }

        if (!result.Succeeded)
        {
            return PreserveCarrierState(snapshot, previous, result.Error);
        }

        return ApplySucceededCarrierResult(snapshot, previous, result, fetchedAt);
    }

    private static FrontierAccountSnapshot ApplySucceededCarrierResult(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        OptionalCapiResponse result,
        DateTimeOffset fetchedAt
    )
    {
        if (string.IsNullOrWhiteSpace(result.Content))
        {
            return ClearCarrierState(snapshot, previous, fetchedAt);
        }

        try
        {
            return ApplyCarrierEndpoint(snapshot, previous, result.Content, fetchedAt);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return PreserveCarrierState(
                snapshot,
                previous,
                "Frontier fleet-carrier data could not be read: " + exception.Message
            );
        }
    }

    private static FrontierAccountSnapshot PreserveCarrierState(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        string carrierError
    )
    {
        return snapshot with
        {
            Carrier = previous?.Carrier,
            CarrierFetchedAt = previous?.CarrierFetchedAt,
            CarrierError = carrierError,
            CommanderReputation = MergeReputation(previous?.CommanderReputation, snapshot.CommanderReputation),
            CommanderReputationFetchedAt =
                snapshot.CommanderReputation?.Count > 0
                    ? snapshot.CommanderReputationFetchedAt
                    : previous?.CommanderReputationFetchedAt,
            CarrierEndpointData = previous?.CarrierEndpointData,
        };
    }

    private static FrontierAccountSnapshot ClearCarrierState(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        DateTimeOffset fetchedAt
    )
    {
        return snapshot with
        {
            Carrier = null,
            CarrierFetchedAt = fetchedAt,
            CarrierError = string.Empty,
            CommanderReputation = MergeReputation(previous?.CommanderReputation, snapshot.CommanderReputation),
            CommanderReputationFetchedAt =
                snapshot.CommanderReputation?.Count > 0
                    ? snapshot.CommanderReputationFetchedAt
                    : previous?.CommanderReputationFetchedAt,
            CarrierEndpointData = [],
        };
    }

    private static FrontierAccountSnapshot ApplyCarrierEndpoint(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        string content,
        DateTimeOffset fetchedAt
    )
    {
        FrontierCarrierEndpointSnapshot endpoint = FrontierCapiSnapshotParser.ParseCarrierEndpoint(content, fetchedAt);
        FrontierReputationSnapshot[] commanderReputation = MergeReputation(
            previous?.CommanderReputation,
            snapshot.CommanderReputation,
            endpoint.CommanderReputation
        );
        return snapshot with
        {
            Carrier = endpoint.Carrier,
            CarrierFetchedAt = fetchedAt,
            CarrierError = string.Empty,
            CommanderReputation = commanderReputation,
            CommanderReputationFetchedAt =
                snapshot.CommanderReputation?.Count > 0 || endpoint.CommanderReputation.Count > 0
                    ? fetchedAt
                    : previous?.CommanderReputationFetchedAt,
            CarrierEndpointData = endpoint.DataPoints,
        };
    }

    private static FrontierAccountSnapshot ApplySquadronResult(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        OptionalCapiResponse result,
        DateTimeOffset fetchedAt
    )
    {
        previous = IsSameCommander(snapshot, previous) ? previous : null;
        if (!result.Queried)
        {
            return PreserveSquadronState(snapshot, previous, previous?.SquadronCarrierError ?? string.Empty);
        }

        if (!result.Succeeded)
        {
            return PreserveSquadronState(snapshot, previous, result.Error);
        }

        if (string.IsNullOrWhiteSpace(result.Content))
        {
            return ClearSquadronState(snapshot, fetchedAt);
        }

        try
        {
            FrontierCarrierEndpointSnapshot endpoint = FrontierCapiSnapshotParser.ParseSquadronEndpoint(
                result.Content,
                fetchedAt
            );
            FrontierReputationSnapshot[] commanderReputation = MergeReputation(
                previous?.CommanderReputation,
                snapshot.CommanderReputation,
                endpoint.CommanderReputation
            );
            return snapshot with
            {
                SquadronCarrier = endpoint.Carrier,
                SquadronCarrierFetchedAt = fetchedAt,
                SquadronCarrierError = string.Empty,
                CommanderReputation = commanderReputation,
                CommanderReputationFetchedAt =
                    endpoint.CommanderReputation.Count > 0 ? fetchedAt : snapshot.CommanderReputationFetchedAt,
                SquadronEndpointData = endpoint.DataPoints,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return PreserveSquadronState(
                snapshot,
                previous,
                "Frontier squadron data could not be read: " + exception.Message
            );
        }
    }

    private static FrontierAccountSnapshot PreserveSquadronState(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        string squadronError
    )
    {
        return snapshot with
        {
            SquadronCarrier = previous?.SquadronCarrier,
            SquadronCarrierFetchedAt = previous?.SquadronCarrierFetchedAt,
            SquadronCarrierError = squadronError,
            SquadronEndpointData = previous?.SquadronEndpointData,
        };
    }

    private static FrontierAccountSnapshot ClearSquadronState(
        FrontierAccountSnapshot snapshot,
        DateTimeOffset fetchedAt
    )
    {
        return snapshot with
        {
            SquadronCarrier = null,
            SquadronCarrierFetchedAt = fetchedAt,
            SquadronCarrierError = string.Empty,
            SquadronEndpointData = [],
        };
    }

    private static bool IsSameCommander(FrontierAccountSnapshot current, FrontierAccountSnapshot? previous)
    {
        return previous is not null
            && string.Equals(current.CommanderName, previous.CommanderName, StringComparison.OrdinalIgnoreCase);
    }

    private static FrontierReputationSnapshot[] MergeReputation(
        params IReadOnlyList<FrontierReputationSnapshot>?[] sources
    )
    {
        return sources
            .Where(source => source is not null)
            .SelectMany(source => source!)
            .GroupBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.Faction, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static FrontierAccountSnapshot ApplyMarketResult(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        OptionalCapiResponse result,
        DateTimeOffset fetchedAt
    )
    {
        if (!result.Succeeded)
        {
            return snapshot with
            {
                Market = previous?.Market,
                MarketFetchedAt = previous?.MarketFetchedAt,
                MarketError = result.Error,
            };
        }

        try
        {
            return snapshot with
            {
                Market = string.IsNullOrWhiteSpace(result.Content)
                    ? null
                    : FrontierCapiSnapshotParser.ParseMarket(result.Content, fetchedAt),
                MarketFetchedAt = fetchedAt,
                MarketError = string.Empty,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return snapshot with
            {
                Market = previous?.Market,
                MarketFetchedAt = previous?.MarketFetchedAt,
                MarketError = "Frontier market data could not be read: " + exception.Message,
            };
        }
    }

    private static FrontierAccountSnapshot ApplyShipyardResult(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        OptionalCapiResponse result,
        DateTimeOffset fetchedAt
    )
    {
        if (!result.Succeeded)
        {
            return snapshot with
            {
                Shipyard = previous?.Shipyard,
                ShipyardFetchedAt = previous?.ShipyardFetchedAt,
                ShipyardError = result.Error,
            };
        }

        try
        {
            return snapshot with
            {
                Shipyard = string.IsNullOrWhiteSpace(result.Content)
                    ? null
                    : FrontierCapiSnapshotParser.ParseShipyard(result.Content, fetchedAt),
                ShipyardFetchedAt = fetchedAt,
                ShipyardError = string.Empty,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return snapshot with
            {
                Shipyard = previous?.Shipyard,
                ShipyardFetchedAt = previous?.ShipyardFetchedAt,
                ShipyardError = "Frontier shipyard data could not be read: " + exception.Message,
            };
        }
    }

    private static FrontierAccountSnapshot ApplyCommunityGoalsResult(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        OptionalCapiResponse result,
        DateTimeOffset fetchedAt
    )
    {
        if (!result.Succeeded)
        {
            return snapshot with
            {
                CommunityGoals = previous?.CommunityGoals,
                CommunityGoalsData = previous?.CommunityGoalsData,
                CommunityGoalsFetchedAt = previous?.CommunityGoalsFetchedAt,
                CommunityGoalsError = result.Error,
                InaraCommunityGoalsFetchedAt = previous?.InaraCommunityGoalsFetchedAt,
                InaraCommunityGoalsError = previous?.InaraCommunityGoalsError ?? string.Empty,
            };
        }

        try
        {
            return snapshot with
            {
                CommunityGoals = string.IsNullOrWhiteSpace(result.Content)
                    ? []
                    : FrontierCapiSnapshotParser.ParseCommunityGoals(result.Content),
                CommunityGoalsData = string.IsNullOrWhiteSpace(result.Content)
                    ? []
                    : FrontierCapiSnapshotParser.ParseDataPoints(result.Content, "communitygoals"),
                CommunityGoalsFetchedAt = fetchedAt,
                CommunityGoalsError = string.Empty,
                InaraCommunityGoalsFetchedAt = previous?.InaraCommunityGoalsFetchedAt,
                InaraCommunityGoalsError = previous?.InaraCommunityGoalsError ?? string.Empty,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return snapshot with
            {
                CommunityGoals = previous?.CommunityGoals,
                CommunityGoalsData = previous?.CommunityGoalsData,
                CommunityGoalsFetchedAt = previous?.CommunityGoalsFetchedAt,
                CommunityGoalsError = "Frontier community-goal data could not be read: " + exception.Message,
                InaraCommunityGoalsFetchedAt = previous?.InaraCommunityGoalsFetchedAt,
                InaraCommunityGoalsError = previous?.InaraCommunityGoalsError ?? string.Empty,
            };
        }
    }

    private async Task<FrontierAccountSnapshot> TryEnrichCommunityGoalsAsync(
        FrontierAccountSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        if (inaraCommunityGoals is null)
        {
            return snapshot;
        }

        try
        {
            InaraCommunityGoalsResult result = await inaraCommunityGoals
                .GetRecentAsync(cancellationToken)
                .ConfigureAwait(false);
            return snapshot with
            {
                CommunityGoals = InaraCommunityGoalEnricher.Enrich(snapshot.CommunityGoals, result),
                InaraCommunityGoalsFetchedAt = result.FetchedAt,
                InaraCommunityGoalsError = result.Warning,
            };
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or JsonException
                        or TimeoutException
            )
        {
            return snapshot with
            {
                InaraCommunityGoalsError =
                    "Inara Community Goal enrichment could not be refreshed: " + exception.Message,
            };
        }
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FrontierCommanderIdentity commander = RequireActiveCommander();
        await RemoveAccountCredentialAsync(commander, cancellationToken).ConfigureAwait(false);
        await CacheFor(commander).ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleCallbackAsync(FrontierOAuthCallback callback, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(callback);
        FrontierCredentialDocument document;
        await using (
            IAsyncDisposable lease = await credentials.AcquireLeaseAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            document =
                await credentials.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No Frontier authorization is waiting for this callback.");
        }
        FrontierPendingAuthorization pending =
            AllPending(document)
                .FirstOrDefault(candidate => FrontierOAuthCallback.FixedTimeEquals(callback.State, candidate.State))
            ?? throw new InvalidOperationException("No Frontier authorization is waiting for this callback.");

        FrontierCommanderIdentity commander =
            FrontierCommanderIdentity.Create(pending.FrontierId, pending.CommanderName)
            ?? throw new InvalidOperationException(
                "This Frontier authorization was started by an older application version. Return to SrvSurvey and connect the active commander again."
            );

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            string detail = string.IsNullOrWhiteSpace(callback.ErrorDescription)
                ? callback.Error
                : callback.ErrorDescription;
            await SaveCallbackFailureAsync(pending.State, detail, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            await SaveCallbackFailureAsync(
                    pending.State,
                    "Frontier did not return an authorization code.",
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        try
        {
            FrontierAccountCredential tokens = await RequestTokensAsync(
                    new Dictionary<string, string>
                    {
                        ["redirect_uri"] = FrontierOAuthCallback.RedirectUri,
                        ["code"] = callback.Code,
                        ["grant_type"] = "authorization_code",
                        ["code_verifier"] = pending.CodeVerifier,
                        ["client_id"] = ClientId,
                    },
                    GetAccount(document, commander.FrontierId) ?? new FrontierAccountCredential(),
                    cancellationToken
                )
                .ConfigureAwait(false);
            await SaveCallbackSuccessAsync(pending, tokens with { AuthorizedAt = utcNow() }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or InvalidOperationException
                        or TaskCanceledException
            )
        {
            await SaveCallbackFailureAsync(
                    pending.State,
                    "Frontier could not complete the token exchange. Please connect again.",
                    cancellationToken
                )
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<CapiResponse> RequestCapiAsync(
        string endpoint,
        FrontierCommanderIdentity commander,
        FrontierAccountCredential credential,
        bool isLegacy,
        bool allowNoContent,
        CancellationToken cancellationToken
    )
    {
        HttpResponseMessage response = await SendCapiAsync(endpoint, credential, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.UnprocessableEntity)
        {
            response.Dispose();
            credential = await EnsureAccessTokenAsync(
                    commander,
                    credential,
                    isLegacy,
                    forceRefresh: true,
                    cancellationToken
                )
                .ConfigureAwait(false);
            response = await SendCapiAsync(endpoint, credential, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            if (allowNoContent && response.StatusCode == HttpStatusCode.NoContent)
            {
                return new CapiResponse(credential, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                string detail = await ReadBoundedStringAsync(response.Content, 4096, cancellationToken)
                    .ConfigureAwait(false);
                if ((int)response.StatusCode == 429)
                {
                    TimeSpan? retry = response.Headers.RetryAfter?.Delta;
                    throw new InvalidOperationException(
                        retry is null
                            ? "Frontier is rate limiting requests. Please wait before trying again."
                            : $"Frontier is rate limiting requests. Please wait {Math.Ceiling(retry.Value.TotalSeconds):N0} seconds before trying again."
                    );
                }

                throw new HttpRequestException(
                    $"Frontier request failed ({(int)response.StatusCode}): "
                        + FirstNonEmpty(detail, response.ReasonPhrase, "Unknown response"),
                    null,
                    response.StatusCode
                );
            }

            string content = await ReadBoundedStringAsync(response.Content, MaximumCapiResponseBytes, cancellationToken)
                .ConfigureAwait(false);
            return new CapiResponse(credential, content);
        }
    }

    private async Task<HttpResponseMessage> SendCapiAsync(
        string endpoint,
        FrontierAccountCredential credential,
        CancellationToken cancellationToken
    )
    {
        await capiGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (lastCapiRequestAt is { } prior)
            {
                TimeSpan delay = MinimumCapiRequestSpacing - (utcNow() - prior);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                FirstNonEmpty(credential.TokenType, "Bearer"),
                credential.AccessToken
            );
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            lastCapiRequestAt = utcNow();
            return await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            capiGate.Release();
        }
    }

    private async Task<FrontierAccountCredential> EnsureAccessTokenAsync(
        FrontierCommanderIdentity commander,
        FrontierAccountCredential credential,
        bool isLegacy,
        bool forceRefresh,
        CancellationToken cancellationToken
    )
    {
        if (
            !forceRefresh
            && !string.IsNullOrWhiteSpace(credential.AccessToken)
            && credential.ExpiresAt > utcNow().AddMinutes(1)
        )
        {
            return credential;
        }

        if (string.IsNullOrWhiteSpace(credential.RefreshToken))
        {
            throw new InvalidOperationException("Frontier authorization expired. Unlink and reconnect your account.");
        }

        try
        {
            FrontierAccountCredential refreshed = await RequestTokensAsync(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = ClientId,
                        ["refresh_token"] = credential.RefreshToken,
                    },
                    credential,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await SaveAccountCredentialAsync(commander, refreshed, isLegacy, cancellationToken).ConfigureAwait(false);
            return refreshed;
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            if (isLegacy)
            {
                await ClearLegacyCredentialAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await RemoveAccountCredentialAsync(commander, CancellationToken.None).ConfigureAwait(false);
            }

            await CacheFor(commander).ClearAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException(
                "Frontier authorization expired or was revoked. Connect your account again.",
                exception
            );
        }
    }

    private async Task<FrontierAccountCredential> RequestTokensAsync(
        IReadOnlyDictionary<string, string> values,
        FrontierAccountCredential previous,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values),
        };
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        string json = await ReadBoundedStringAsync(response.Content, MaximumTokenResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = TryReadTokenError(json);
            throw new HttpRequestException(
                $"Frontier token request failed ({(int)response.StatusCode}): "
                    + FirstNonEmpty(detail, response.ReasonPhrase, "Unknown response"),
                null,
                response.StatusCode
            );
        }

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string? accessToken = root.TryGetProperty("access_token", out JsonElement access) ? access.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidDataException("Frontier did not return an access token.");
        }

        string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement refresh)
            ? refresh.GetString()
            : null;
        string? tokenType = root.TryGetProperty("token_type", out JsonElement type) ? type.GetString() : null;
        int expiresIn =
            root.TryGetProperty("expires_in", out JsonElement expires) && expires.TryGetInt32(out int seconds)
                ? seconds
                : 0;
        return previous with
        {
            AccessToken = accessToken,
            RefreshToken = FirstNonEmpty(refreshToken, previous.RefreshToken),
            TokenType = FirstNonEmpty(tokenType, previous.TokenType, "Bearer"),
            ExpiresAt = utcNow().AddSeconds(Math.Max(0, expiresIn)),
        };
    }

    private FrontierCommanderIdentity RequireActiveCommander()
    {
        return activeCommander
            ?? throw new InvalidOperationException(
                "Wait for SrvSurvey to detect the active journal commander before connecting Frontier."
            );
    }

    private FrontierProfileCacheStore CacheFor(FrontierCommanderIdentity commander) =>
        cacheFactory(commander.FrontierId);

    private static FrontierAccountCredential? GetAccount(FrontierCredentialDocument? document, string frontierId)
    {
        if (document is null)
        {
            return null;
        }

        return document
            .Accounts.FirstOrDefault(pair => string.Equals(pair.Key, frontierId, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static LoadedCredential? FindCredential(
        FrontierCredentialDocument? document,
        FrontierCommanderIdentity commander
    )
    {
        FrontierAccountCredential? scoped = GetAccount(document, commander.FrontierId);
        if (scoped?.IsLinked == true)
        {
            return new LoadedCredential(scoped, false);
        }

        return document?.IsLinked == true && LegacyMayBelongTo(document, commander)
            ? new LoadedCredential(document.LegacyCredential, true)
            : null;
    }

    private static bool LegacyMayBelongTo(FrontierCredentialDocument document, FrontierCommanderIdentity commander)
    {
        if (!string.IsNullOrWhiteSpace(document.LegacyFrontierId))
        {
            return string.Equals(document.LegacyFrontierId, commander.FrontierId, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrWhiteSpace(document.LegacyCommanderName)
            || string.Equals(document.LegacyCommanderName, commander.CommanderName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<FrontierCredentialDocument> LoadAndMigrateLegacyAsync(
        FrontierCommanderIdentity commander,
        CancellationToken cancellationToken
    )
    {
        FrontierCredentialDocument document =
            await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new FrontierCredentialDocument();
        document = await MigrateMiskeyedCapiAccountsAsync(commander, document, cancellationToken).ConfigureAwait(false);
        if (!document.IsLinked || GetAccount(document, commander.FrontierId)?.IsLinked == true || legacyCache is null)
        {
            return document;
        }

        FrontierAccountSnapshot? snapshot;
        try
        {
            snapshot = await legacyCache.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            snapshot = null;
        }

        if (snapshot is null)
        {
            return document;
        }

        if (!commander.Matches(snapshot))
        {
            await SaveLegacyOwnerAsync(null, snapshot.CommanderName, cancellationToken).ConfigureAwait(false);
            return await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? document;
        }

        await MigrateLegacyCredentialAsync(commander, document.LegacyCredential, snapshot, cancellationToken)
            .ConfigureAwait(false);
        return await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new FrontierCredentialDocument();
    }

    private async Task<FrontierCredentialDocument> MigrateMiskeyedCapiAccountsAsync(
        FrontierCommanderIdentity commander,
        FrontierCredentialDocument document,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(commander.CommanderName))
        {
            return document;
        }

        List<(string FrontierId, FrontierAccountSnapshot Snapshot)> candidates = await CollectMiskeyedCandidatesAsync(
                commander,
                document,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return document;
        }

        HashSet<string> migratedAliases = await RelinkMiskeyedAccountsAsync(commander, candidates, cancellationToken)
            .ConfigureAwait(false);
        if (migratedAliases.Count == 0)
        {
            return await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? document;
        }

        await ConsolidateMiskeyedCachesAsync(commander, candidates, migratedAliases, cancellationToken)
            .ConfigureAwait(false);
        return await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? document;
    }

    private async Task<List<(string FrontierId, FrontierAccountSnapshot Snapshot)>> CollectMiskeyedCandidatesAsync(
        FrontierCommanderIdentity commander,
        FrontierCredentialDocument document,
        CancellationToken cancellationToken
    )
    {
        var candidates = new List<(string FrontierId, FrontierAccountSnapshot Snapshot)>();
        foreach (
            KeyValuePair<string, FrontierAccountCredential> account in document.Accounts.Where(pair =>
                IsForeignLinkedAccount(pair, commander.FrontierId)
            )
        )
        {
            var candidate = FrontierCommanderIdentity.Create(account.Key, null);
            if (candidate is null)
            {
                continue;
            }

            FrontierAccountSnapshot? snapshot = await TryLoadCandidateSnapshotAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            if (!IsMiskeyedCandidate(commander, candidate, snapshot))
            {
                continue;
            }

            candidates.Add((candidate.FrontierId, snapshot!));
        }

        return candidates;
    }

    private static bool IsForeignLinkedAccount(
        KeyValuePair<string, FrontierAccountCredential> pair,
        string commanderFrontierId
    )
    {
        return pair.Value.IsLinked && !string.Equals(pair.Key, commanderFrontierId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<FrontierAccountSnapshot?> TryLoadCandidateSnapshotAsync(
        FrontierCommanderIdentity candidate,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await CacheFor(candidate).LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsMiskeyedCandidate(
        FrontierCommanderIdentity commander,
        FrontierCommanderIdentity candidate,
        FrontierAccountSnapshot? snapshot
    )
    {
        return snapshot?.CommanderId is { } capiCommanderId
            && string.Equals(candidate.FrontierId, $"F{capiCommanderId}", StringComparison.OrdinalIgnoreCase)
            && commander.Matches(snapshot);
    }

    private async Task<HashSet<string>> RelinkMiskeyedAccountsAsync(
        FrontierCommanderIdentity commander,
        IReadOnlyList<(string FrontierId, FrontierAccountSnapshot Snapshot)> candidates,
        CancellationToken cancellationToken
    )
    {
        var migratedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument document =
            await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new FrontierCredentialDocument();
        Dictionary<string, FrontierAccountCredential> accounts = CopyAccounts(document);
        FrontierAccountCredential? target = GetAccount(document, commander.FrontierId);
        foreach (string? candidateFrontierId in candidates.Select(candidate => candidate.FrontierId))
        {
            if (!accounts.TryGetValue(candidateFrontierId, out FrontierAccountCredential? source) || !source.IsLinked)
            {
                continue;
            }

            if (target?.IsLinked != true)
            {
                target = source;
                accounts[commander.FrontierId] = source;
            }

            accounts.Remove(candidateFrontierId);
            migratedAliases.Add(candidateFrontierId);
        }

        if (migratedAliases.Count == 0)
        {
            return migratedAliases;
        }

        document = document with { Version = 3, Accounts = accounts };
        await credentials.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        return migratedAliases;
    }

    private async Task ConsolidateMiskeyedCachesAsync(
        FrontierCommanderIdentity commander,
        IReadOnlyList<(string FrontierId, FrontierAccountSnapshot Snapshot)> candidates,
        HashSet<string> migratedAliases,
        CancellationToken cancellationToken
    )
    {
        FrontierAccountSnapshot? activeSnapshot = null;
        try
        {
            activeSnapshot = await CacheFor(commander).LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Replace the malformed active cache with the verified alias below.
        }

        FrontierAccountSnapshot bestSnapshot = candidates
            .Where(candidate => migratedAliases.Contains(candidate.FrontierId))
            .Select(candidate => candidate.Snapshot)
            .Append(activeSnapshot)
            .Where(snapshot => snapshot is not null)
            .Cast<FrontierAccountSnapshot>()
            .OrderByDescending(snapshot => snapshot.FetchedAt)
            .First();
        foreach (
            (string FrontierId, FrontierAccountSnapshot Snapshot) candidate in candidates.Where(candidate =>
                migratedAliases.Contains(candidate.FrontierId)
            )
        )
        {
            await CacheFor(FrontierCommanderIdentity.Create(candidate.FrontierId, candidate.Snapshot.CommanderName)!)
                .ClearAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await CacheFor(commander).SaveAsync(bestSnapshot, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureCommanderNameIsAvailable(FrontierCommanderIdentity commander)
    {
        if (string.IsNullOrWhiteSpace(commander.CommanderName))
        {
            throw new InvalidOperationException(
                "Wait for the active commander name to load from the journal before connecting to Frontier or refreshing this page."
            );
        }
    }

    private async Task SavePendingAuthorizationAsync(
        FrontierPendingAuthorization pending,
        CancellationToken cancellationToken
    )
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument document =
            await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new FrontierCredentialDocument();
        if (
            AllPending(document)
                .Any(active =>
                    string.Equals(active.FrontierId, pending.FrontierId, StringComparison.OrdinalIgnoreCase)
                    && utcNow() - active.StartedAt < AuthorizationTimeout
                )
        )
        {
            throw new InvalidOperationException(
                "A Frontier connection for this commander is already waiting for browser authorization."
            );
        }

        var pendingAuthorizations = new Dictionary<string, FrontierPendingAuthorization>(
            document.PendingAuthorizations,
            StringComparer.Ordinal
        );
        if (
            document.PendingAuthorization is { } legacyPending
            && utcNow() - legacyPending.StartedAt >= AuthorizationTimeout
        )
        {
            document = document with { PendingAuthorization = null };
        }
        foreach (
            string expiredState in pendingAuthorizations
                .Where(entry => utcNow() - entry.Value.StartedAt >= AuthorizationTimeout)
                .Select(entry => entry.Key)
                .ToArray()
        )
        {
            pendingAuthorizations.Remove(expiredState);
        }
        pendingAuthorizations[pending.State] = pending;
        var results = new Dictionary<string, FrontierAuthorizationResult>(
            document.AuthorizationResults,
            StringComparer.Ordinal
        );
        foreach (
            string expiredState in results
                .Where(entry => utcNow() - entry.Value.CompletedAt >= AuthorizationTimeout)
                .Select(entry => entry.Key)
                .ToArray()
        )
        {
            results.Remove(expiredState);
        }

        await credentials
            .SaveAsync(
                document with
                {
                    Version = 3,
                    PendingAuthorizations = pendingAuthorizations,
                    AuthorizationResults = results,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task SaveCallbackSuccessAsync(
        FrontierPendingAuthorization pending,
        FrontierAccountCredential credential,
        CancellationToken cancellationToken
    )
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument document =
            await credentials.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No Frontier authorization is waiting for this callback.");
        FrontierPendingAuthorization? latest = AllPending(document)
            .FirstOrDefault(candidate => FrontierOAuthCallback.FixedTimeEquals(candidate.State, pending.State));
        if (latest is null || !string.Equals(latest.FrontierId, pending.FrontierId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Frontier authorization was cancelled or replaced before the token exchange completed."
            );
        }

        document = CompleteAuthorization(
            WithAccount(document, pending.FrontierId, credential),
            pending,
            new FrontierAuthorizationResult(pending.State, true, string.Empty, utcNow())
        );
        await credentials.SaveAsync(document, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAccountCredentialAsync(
        FrontierCommanderIdentity commander,
        FrontierAccountCredential credential,
        bool isLegacy,
        CancellationToken cancellationToken
    )
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument document =
            await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new FrontierCredentialDocument();
        document = isLegacy
            ? WithLegacyCredential(document, credential)
            : WithAccount(document, commander.FrontierId, credential);
        await credentials.SaveAsync(document, cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateLegacyCredentialAsync(
        FrontierCommanderIdentity commander,
        FrontierAccountCredential credential,
        FrontierAccountSnapshot? snapshot,
        CancellationToken cancellationToken
    )
    {
        await using (
            IAsyncDisposable lease = await credentials.AcquireLeaseAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            FrontierCredentialDocument document =
                await credentials.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? new FrontierCredentialDocument();
            document = ClearLegacyCredential(WithAccount(document, commander.FrontierId, credential));
            await credentials.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }

        if (snapshot is not null)
        {
            await CacheFor(commander).SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        if (legacyCache is not null)
        {
            await legacyCache.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RejectMismatchedAuthorizationAsync(
        FrontierCommanderIdentity expected,
        FrontierAccountSnapshot actualSnapshot,
        bool isLegacy,
        CancellationToken cancellationToken
    )
    {
        if (!isLegacy)
        {
            await RemoveAccountCredentialAsync(expected, cancellationToken).ConfigureAwait(false);
            await CacheFor(expected).ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await SaveLegacyOwnerAsync(null, actualSnapshot.CommanderName, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveLegacyOwnerAsync(
        string? frontierId,
        string commanderName,
        CancellationToken cancellationToken
    )
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument? document = await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (document?.IsLinked != true)
        {
            return;
        }

        await credentials
            .SaveAsync(
                document with
                {
                    LegacyFrontierId = frontierId ?? string.Empty,
                    LegacyCommanderName = commanderName,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task RemoveAccountCredentialAsync(
        FrontierCommanderIdentity commander,
        CancellationToken cancellationToken
    )
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument? document = await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        Dictionary<string, FrontierAccountCredential> accounts = CopyAccounts(document);
        foreach (
            string? key in accounts
                .Keys.Where(key => string.Equals(key, commander.FrontierId, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        )
        {
            accounts.Remove(key);
        }

        document = document with { Accounts = accounts };
        if (document.IsLinked && LegacyMayBelongTo(document, commander))
        {
            document = ClearLegacyCredential(document);
            if (legacyCache is not null)
            {
                await legacyCache.ClearAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await SaveOrClearDocumentAsync(document, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearLegacyCredentialAsync(CancellationToken cancellationToken)
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument? document = await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        await SaveOrClearDocumentAsync(ClearLegacyCredential(document), cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveOrClearDocumentAsync(
        FrontierCredentialDocument document,
        CancellationToken cancellationToken
    )
    {
        if (
            document.Accounts.Count == 0
            && !document.IsLinked
            && !AllPending(document).Any()
            && document.AuthorizationResult is null
            && document.AuthorizationResults.Count == 0
        )
        {
            await credentials.ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await credentials.SaveAsync(document, cancellationToken).ConfigureAwait(false);
    }

    private static FrontierCredentialDocument WithAccount(
        FrontierCredentialDocument document,
        string frontierId,
        FrontierAccountCredential credential
    )
    {
        Dictionary<string, FrontierAccountCredential> accounts = CopyAccounts(document);
        accounts[frontierId] = credential;
        return document with { Version = 3, Accounts = accounts };
    }

    private static FrontierCredentialDocument WithLegacyCredential(
        FrontierCredentialDocument document,
        FrontierAccountCredential credential
    ) =>
        document with
        {
            AccessToken = credential.AccessToken,
            RefreshToken = credential.RefreshToken,
            TokenType = credential.TokenType,
            ExpiresAt = credential.ExpiresAt,
            AuthorizedAt = credential.AuthorizedAt,
            LastCapiRefreshAt = credential.LastCapiRefreshAt,
            LastCapiAttemptAt = credential.LastCapiAttemptAt,
        };

    private static FrontierCredentialDocument ClearLegacyCredential(FrontierCredentialDocument document) =>
        document with
        {
            AccessToken = string.Empty,
            RefreshToken = string.Empty,
            TokenType = "Bearer",
            ExpiresAt = null,
            AuthorizedAt = null,
            LastCapiRefreshAt = null,
            LastCapiAttemptAt = null,
            LegacyFrontierId = string.Empty,
            LegacyCommanderName = string.Empty,
        };

    private static Dictionary<string, FrontierAccountCredential> CopyAccounts(FrontierCredentialDocument document) =>
        new(document.Accounts, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<FrontierPendingAuthorization> AllPending(FrontierCredentialDocument document)
    {
        if (document.PendingAuthorization is { } legacy)
        {
            yield return legacy;
        }

        foreach (FrontierPendingAuthorization pending in document.PendingAuthorizations.Values)
        {
            yield return pending;
        }
    }

    private static FrontierCredentialDocument CompleteAuthorization(
        FrontierCredentialDocument document,
        FrontierPendingAuthorization pending,
        FrontierAuthorizationResult result
    )
    {
        if (
            document.PendingAuthorization is { } legacy
            && FrontierOAuthCallback.FixedTimeEquals(legacy.State, pending.State)
        )
        {
            return document with { PendingAuthorization = null, AuthorizationResult = result };
        }

        var pendingAuthorizations = new Dictionary<string, FrontierPendingAuthorization>(
            document.PendingAuthorizations,
            StringComparer.Ordinal
        );
        pendingAuthorizations.Remove(pending.State);
        var results = new Dictionary<string, FrontierAuthorizationResult>(
            document.AuthorizationResults,
            StringComparer.Ordinal
        )
        {
            [pending.State] = result,
        };
        return document with { PendingAuthorizations = pendingAuthorizations, AuthorizationResults = results };
    }

    private static FrontierCredentialDocument RemovePending(
        FrontierCredentialDocument document,
        FrontierPendingAuthorization pending
    )
    {
        if (
            document.PendingAuthorization is { } legacy
            && FrontierOAuthCallback.FixedTimeEquals(legacy.State, pending.State)
        )
        {
            return document with { PendingAuthorization = null };
        }

        var pendingAuthorizations = new Dictionary<string, FrontierPendingAuthorization>(
            document.PendingAuthorizations,
            StringComparer.Ordinal
        );
        pendingAuthorizations.Remove(pending.State);
        return document with { PendingAuthorizations = pendingAuthorizations };
    }

    private async Task WaitForAuthorizationAsync(string state, string frontierId, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = utcNow() + AuthorizationTimeout;
        while (utcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FrontierCredentialDocument? document;
            await using (
                IAsyncDisposable lease = await credentials.AcquireLeaseAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                document = await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            FrontierAuthorizationResult? result = document?.AuthorizationResults.GetValueOrDefault(state);
            if (
                result is null
                && document?.AuthorizationResult is { } legacyResult
                && FrontierOAuthCallback.FixedTimeEquals(legacyResult.State, state)
            )
            {
                result = legacyResult;
            }
            if (result is not null)
            {
                AuthorizationCallbackReceived?.Invoke(this, EventArgs.Empty);
                if (
                    result.Succeeded
                    && document?.Accounts.TryGetValue(frontierId, out FrontierAccountCredential? account) == true
                    && account.IsLinked
                )
                {
                    return;
                }

                throw new InvalidOperationException(
                    FirstNonEmpty(result.Error, "Frontier authorization was not completed.")
                );
            }

            if (
                document is null
                || !AllPending(document).Any(pending => FrontierOAuthCallback.FixedTimeEquals(pending.State, state))
            )
            {
                throw new InvalidOperationException("Frontier authorization was cancelled or replaced.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Frontier authorization timed out. Please try again.");
    }

    private async Task ClearPendingAuthorizationAsync(string state, CancellationToken cancellationToken)
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument? document = await credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        FrontierPendingAuthorization? pending = document is null
            ? null
            : AllPending(document)
                .FirstOrDefault(candidate => FrontierOAuthCallback.FixedTimeEquals(candidate.State, state));
        if (pending is null)
        {
            return;
        }

        await credentials.SaveAsync(RemovePending(document!, pending), cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveCallbackFailureAsync(string state, string error, CancellationToken cancellationToken)
    {
        await using IAsyncDisposable lease = await credentials
            .AcquireLeaseAsync(cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument document =
            await credentials.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new FrontierCredentialDocument();
        FrontierPendingAuthorization? pending = AllPending(document)
            .FirstOrDefault(candidate => FrontierOAuthCallback.FixedTimeEquals(candidate.State, state));
        if (pending is null)
        {
            return;
        }

        await credentials
            .SaveAsync(
                CompleteAuthorization(
                    document,
                    pending,
                    new FrontierAuthorizationResult(state, false, error, utcNow())
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static Uri BuildAuthorizationUri(string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["audience"] = "frontier",
            ["scope"] = "auth capi",
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["redirect_uri"] = FrontierOAuthCallback.RedirectUri,
        };
        return new Uri(
            AuthorizationEndpoint
                + "?"
                + string.Join(
                    '&',
                    query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))
                )
        );
    }

    private static async Task OpenBrowserAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        await Task.CompletedTask;
    }

    private static async Task<string> ReadBoundedStringAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException($"Frontier response exceeded the {maximumBytes:N0}-byte safety limit.");
        }

        await using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Frontier response exceeded the {maximumBytes:N0}-byte safety limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string TryReadTokenError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            foreach (string? name in new[] { "error_description", "message", "error" })
            {
                if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            return json.Length <= 300 ? json : json[..300];
        }

        return string.Empty;
    }

    private static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first >= second ? first : second;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        capiGate.Dispose();
        httpClient.Dispose();
    }

    private sealed record CapiResponse(FrontierAccountCredential Credential, string? Content);

    private sealed record OptionalCapiResponse(
        FrontierAccountCredential Credential,
        string? Content,
        string Error,
        bool Queried,
        bool Succeeded
    )
    {
        public static OptionalCapiResponse Skipped(FrontierAccountCredential credential) =>
            new(credential, null, string.Empty, false, false);
    }

    private sealed record LoadedCredential(FrontierAccountCredential Credential, bool IsLegacy);
}
