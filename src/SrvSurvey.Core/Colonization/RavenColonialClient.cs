using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Colonization;

public interface IRavenColonialClient
{
    Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
        string commanderName,
        CancellationToken cancellationToken = default
    );

    Task<string?> GetCommanderByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
        string commanderName,
        IEnumerable<string> hiddenProjectIds,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationProject?> GetProjectAsync(string buildId, CancellationToken cancellationToken = default);

    Task<ColonizationProject?> GetProjectAsync(
        long systemAddress,
        long marketId,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationProject> UpdateProjectAsync(
        ColonizationProjectUpdate update,
        CancellationToken cancellationToken = default
    );

    Task MarkProjectCompleteAsync(string buildId, CancellationToken cancellationToken = default);

    Task ContributeToProjectAsync(
        string buildId,
        string commanderName,
        IReadOnlyDictionary<string, int> contributions,
        CancellationToken cancellationToken = default
    );

    Task SetPrimaryProjectAsync(string commanderName, string? buildId, CancellationToken cancellationToken = default);

    Task LinkCommanderAsync(string buildId, string commanderName, CancellationToken cancellationToken = default);

    Task UnlinkCommanderAsync(string buildId, string commanderName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    );

    Task<string?> GetSystemArchitectAsync(string systemNameOrAddress, CancellationToken cancellationToken = default);

    Task<ColonizationSystemRecord> GetSystemAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
        string systemNameOrAddress,
        ColonizationSystemSiteUpdate update,
        string apiKey,
        CancellationToken cancellationToken = default
    );

    Task PatchSystemSiteAsync(
        string systemNameOrAddress,
        string siteId,
        ColonizationSystemSitePatch patch,
        string apiKey,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationProject?> CreateProjectAsync(
        ColonizationProjectCreate project,
        CancellationToken cancellationToken = default
    );

    Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(long marketId, CancellationToken cancellationToken = default);

    Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
        ColonizationFleetCarrierRegistration carrier,
        string apiKey,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargo,
        string apiKey,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargoChanges,
        string apiKey,
        CancellationToken cancellationToken = default
    );

    Task PublishCurrentShipAsync(
        ColonizationCurrentShip ship,
        string apiKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class RavenColonialClient : IRavenColonialClient
{
    private const string RccKeyHeader = "rcc-key";

    private const int MaximumJsonResponseBytes = 8 * 1024 * 1024;
    private const int MaximumErrorDetailBytes = 2048;

    public static Uri DefaultServiceUri { get; } =
        new("https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/");

    public static Uri WebsiteUri { get; } = new("https://ravencolonial.com/");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly HttpClient httpClient;
    private readonly Uri serviceUri;

    public RavenColonialClient(HttpClient? httpClient = null, Uri? serviceUri = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.serviceUri = EnsureTrailingSlash(serviceUri ?? DefaultServiceUri);
    }

    /// <summary>Loads the commander workspace, accepting an absent primary selection while requiring project and carrier lists.</summary>
    public async Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
        string commanderName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        string commander = Uri.EscapeDataString(commanderName.Trim());
        Task<ColonizationProject[]?> projectsTask = GetAsync<ColonizationProject[]>(
            $"api/cmdr/{commander}/active",
            "load active colonisation projects",
            cancellationToken
        );
        Task<string[]?> hiddenTask = GetAsync<string[]>(
            $"api/cmdr/{commander}/hiddenIDs",
            "load hidden colonisation projects",
            cancellationToken
        );
        Task<string?> primaryTask = GetAsync<string?>(
            $"api/cmdr/{commander}/primary",
            "load the primary colonisation project",
            cancellationToken,
            allowNull: true
        );
        Task<ColonizationFleetCarrier[]?> fleetCarriersTask = GetAsync<ColonizationFleetCarrier[]>(
            $"api/cmdr/{commander}/fc/all",
            "load commander Fleet Carriers",
            cancellationToken
        );
        await Task.WhenAll(projectsTask, hiddenTask, primaryTask, fleetCarriersTask).ConfigureAwait(false);
        return new ColonizationCommanderProjects(
            await projectsTask.ConfigureAwait(false) ?? [],
            await hiddenTask.ConfigureAwait(false) ?? [],
            await primaryTask.ConfigureAwait(false),
            await fleetCarriersTask.ConfigureAwait(false) ?? []
        );
    }

    /// <summary>Resolves the commander owning a Raven API key within the complete request deadline.</summary>
    public async Task<string?> GetCommanderByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri("api/cmdr/"));
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (
            response.StatusCode
            is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound
        )
        {
            return null;
        }

        Dictionary<string, string> data = await ReadRequiredAsync<Dictionary<string, string>>(
                response,
                "validate the Raven API key",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!data.TryGetValue("displayName", out string? commanderName) || string.IsNullOrWhiteSpace(commanderName))
        {
            throw new InvalidDataException("Raven returned no commander display name for the API key.");
        }

        return commanderName.Trim();
    }

    /// <summary>Publishes the commander's hidden projects after validating the supplied identifiers.</summary>
    public async Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
        string commanderName,
        IEnumerable<string> hiddenProjectIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        ArgumentNullException.ThrowIfNull(hiddenProjectIds);
        string[] ids = hiddenProjectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri($"api/cmdr/{Uri.EscapeDataString(commanderName.Trim())}/hiddenIDs")
        )
        {
            Content = JsonContent.Create(ids, options: JsonOptions),
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<string[]>(response, "save hidden colonisation projects", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Loads a project by its persistent identity or construction-site coordinates; a missing project returns null.</summary>
    public async Task<ColonizationProject?> GetProjectAsync(
        string buildId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateUri($"api/project/{Uri.EscapeDataString(buildId.Trim())}")
        );
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationProject>(response, "load a colonisation project", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Loads a project by its persistent identity or construction-site coordinates; a missing project returns null.</summary>
    public async Task<ColonizationProject?> GetProjectAsync(
        long systemAddress,
        long marketId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri($"api/system/{systemAddress}/{marketId}"));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationProject>(
                response,
                "load a colonisation project by construction site",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Patches only the supplied project fields and validates the returned project.</summary>
    public async Task<ColonizationProject> UpdateProjectAsync(
        ColonizationProjectUpdate update,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.BuildId);
        if (update.Commodities is not null)
        {
            update = update with { Commodities = ColonizationCommodityMaps.NormalizeNeedMap(update.Commodities) };
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            CreateUri($"api/project/{Uri.EscapeDataString(update.BuildId.Trim())}")
        )
        {
            Content = JsonContent.Create(update, options: JsonOptions),
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationProject>(response, "patch a colonisation project", cancellationToken)
            .ConfigureAwait(false);
    }

    public Task MarkProjectCompleteAsync(string buildId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        return SendWithoutResponseAsync(
            HttpMethod.Post,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/complete",
            content: null,
            "mark a colonisation project complete",
            cancellationToken
        );
    }

    public Task ContributeToProjectAsync(
        string buildId,
        string commanderName,
        IReadOnlyDictionary<string, int> contributions,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        ArgumentNullException.ThrowIfNull(contributions);
        // Reject empty-normalized keys up front so NormalizeNeedMap cannot drop them
        // and partially submit a mixed map of valid and invalid commodity names.
        if (
            contributions.Any(pair =>
                pair.Value <= 0 || ColonizationConstructionState.NormalizeCommodityName(pair.Key).Length == 0
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(contributions),
                "Project contributions require a commodity name and a positive amount."
            );
        }

        Dictionary<string, int> normalized = ColonizationCommodityMaps.NormalizeNeedMap(contributions);
        if (normalized.Count == 0 || normalized.Any(pair => pair.Value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contributions),
                "Project contributions require a commodity name and a positive amount."
            );
        }

        return SendWithoutResponseAsync(
            HttpMethod.Post,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/contribute/"
                + Uri.EscapeDataString(commanderName.Trim()),
            JsonContent.Create(normalized, options: JsonOptions),
            "publish a colonisation contribution",
            cancellationToken
        );
    }

    public Task SetPrimaryProjectAsync(
        string commanderName,
        string? buildId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        string relativeUri = $"api/cmdr/{Uri.EscapeDataString(commanderName.Trim())}/primary/";
        if (string.IsNullOrWhiteSpace(buildId))
        {
            return SendWithoutResponseAsync(
                HttpMethod.Delete,
                relativeUri,
                content: null,
                "clear the primary colonisation project",
                cancellationToken
            );
        }

        return SendWithoutResponseAsync(
            HttpMethod.Put,
            relativeUri + Uri.EscapeDataString(buildId.Trim()),
            content: null,
            "set the primary colonisation project",
            cancellationToken
        );
    }

    public Task LinkCommanderAsync(string buildId, string commanderName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        return SendWithoutResponseAsync(
            HttpMethod.Put,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/link/" + Uri.EscapeDataString(commanderName.Trim()),
            content: null,
            "link a commander to a colonisation project",
            cancellationToken
        );
    }

    public Task UnlinkCommanderAsync(
        string buildId,
        string commanderName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        return SendWithoutResponseAsync(
            HttpMethod.Delete,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/link/" + Uri.EscapeDataString(commanderName.Trim()),
            content: null,
            "unlink a commander from a colonisation project",
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        return await GetAsync<ColonizationSystemSite[]>(
                    $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/sites",
                    "load planned colonisation sites",
                    cancellationToken
                )
                .ConfigureAwait(false)
            ?? [];
    }

    /// <summary>Loads the system architect, accepting a null response when no architect is assigned.</summary>
    public Task<string?> GetSystemArchitectAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        return GetAsync<string?>(
            $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/architect",
            "load the system architect",
            cancellationToken,
            allowNull: true
        );
    }

    public Task<ColonizationSystemRecord> GetSystemAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        return GetAsync<ColonizationSystemRecord>(
            $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}",
            "load a colonisation system",
            cancellationToken
        )!;
    }

    /// <summary>Imports the selected system's bodies and returns its refreshed workspace.</summary>
    public async Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri($"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/import/bodies")
        );
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationSystemRecord>(
                response,
                "import colonisation system bodies",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Publishes a reconciled site update and validates the returned site list.</summary>
    public async Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
        string systemNameOrAddress,
        ColonizationSystemSiteUpdate update,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateUri($"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/sites")
        )
        {
            Content = JsonContent.Create(update, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationSystemRecord>(
                response,
                "update colonisation system sites",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Applies a targeted site repair while preserving fields omitted from the patch.</summary>
    public async Task PatchSystemSiteAsync(
        string systemNameOrAddress,
        string siteId,
        ColonizationSystemSitePatch patch,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (patch.MarketId is null && patch.Name is null)
        {
            throw new ArgumentException("A system-site patch must contain a market ID or name.", nameof(patch));
        }
        if (patch.MarketId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(patch), "A system-site market ID must be positive.");
        }
        if (patch.Name is not null && string.IsNullOrWhiteSpace(patch.Name))
        {
            throw new ArgumentException("A system-site name cannot be empty.", nameof(patch));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            CreateUri(
                $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/sites/"
                    + Uri.EscapeDataString(siteId.Trim())
            )
        )
        {
            Content = JsonContent.Create(patch, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await ReadBoundedTextAsync(response.Content, MaximumErrorDetailBytes, cancellationToken)
                .ConfigureAwait(false);
            throw new RavenColonialServiceException(response.StatusCode, "repair a colonisation system site", detail);
        }
    }

    /// <summary>Creates a Raven project and requires a valid project in the response.</summary>
    public async Task<ColonizationProject?> CreateProjectAsync(
        ColonizationProjectCreate project,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(project);
        project = project with { Commodities = ColonizationCommodityMaps.NormalizeNeedMap(project.Commodities) };
        using var request = new HttpRequestMessage(HttpMethod.Put, CreateUri("api/project/"))
        {
            Content = JsonContent.Create(project, options: JsonOptions),
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationProject>(
                response,
                "create a colonisation project",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Loads carrier cargo by MarketID, returning null when Raven has no matching carrier.</summary>
    public async Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
        long marketId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri($"api/fc/{marketId}"));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationFleetCarrier>(
                response,
                "load Fleet Carrier cargo",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Registers or links the specified carrier using the initiating profile's Raven key.</summary>
    public async Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
        ColonizationFleetCarrierRegistration carrier,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrier.MarketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(carrier.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Put, CreateUri($"api/fc/{carrier.MarketId}"))
        {
            Content = JsonContent.Create(carrier, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationFleetCarrier>(
                response,
                "publish the Fleet Carrier",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargo,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (cargo.Any(pair => pair.Value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(cargo), "Replacement cargo counts cannot be negative.");
        }

        return SendFleetCarrierCargoAsync(
            HttpMethod.Post,
            marketId,
            cargo,
            apiKey,
            "replace Fleet Carrier cargo",
            cancellationToken
        );
    }

    public Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargoChanges,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(cargoChanges);
        return SendFleetCarrierCargoAsync(
            HttpMethod.Patch,
            marketId,
            cargoChanges,
            apiKey,
            "adjust Fleet Carrier cargo",
            cancellationToken
        );
    }

    /// <summary>Publishes the commander's current ship cargo using validated commodity counts.</summary>
    public async Task PublishCurrentShipAsync(
        ColonizationCurrentShip ship,
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, CreateUri("api/cmdr/currentShip"))
        {
            Content = JsonContent.Create(ship, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await ReadBoundedTextAsync(response.Content, MaximumErrorDetailBytes, cancellationToken)
                .ConfigureAwait(false);
            throw new RavenColonialServiceException(response.StatusCode, "publish current ship cargo", detail);
        }
    }

    /// <summary>Sends a carrier cargo operation and validates its normalized response map.</summary>
    private async Task<IReadOnlyDictionary<string, int>> SendFleetCarrierCargoAsync(
        HttpMethod method,
        long marketId,
        IReadOnlyDictionary<string, int> cargo,
        string apiKey,
        string operation,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var normalizedCargo = cargo
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage(method, CreateUri($"api/fc/{marketId}/cargo"))
        {
            Content = JsonContent.Create(normalizedCargo, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation(RccKeyHeader, apiKey.Trim());
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        Dictionary<string, int> result = await ReadRequiredAsync<Dictionary<string, int>>(
                response,
                operation,
                cancellationToken
            )
            .ConfigureAwait(false);
        var validated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> pair in result)
        {
            string? name = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(name) || pair.Value < 0)
            {
                throw new InvalidDataException("Raven Colonial returned invalid Fleet Carrier cargo.");
            }

            if (!validated.TryAdd(name, pair.Value))
            {
                throw new InvalidDataException("Raven Colonial returned duplicate Fleet Carrier cargo names.");
            }
        }

        return validated;
    }

    /// <summary>Reads a bounded GET response, permitting JSON null only for explicitly optional results.</summary>
    private async Task<T?> GetAsync<T>(
        string relativeUri,
        string operation,
        CancellationToken cancellationToken,
        bool allowNull = false
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri(relativeUri));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<T>(response, operation, cancellationToken, allowNull).ConfigureAwait(false);
    }

    /// <summary>Requires an HTTP success acknowledgement without reading an unused success body.</summary>
    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string relativeUri,
        HttpContent? content,
        string operation,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(method, CreateUri(relativeUri)) { Content = content };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken, readSuccessBody: false)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await ReadBoundedTextAsync(response.Content, MaximumErrorDetailBytes, cancellationToken)
                .ConfigureAwait(false);
            throw new RavenColonialServiceException(response.StatusCode, operation, detail);
        }
    }

    /// <summary>Bounds and buffers response bodies within the HTTP deadline, and normalizes transport I/O failures.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool readSuccessBody = true
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(
            httpClient.Timeout == Timeout.InfiniteTimeSpan ? TimeSpan.FromSeconds(100) : httpClient.Timeout
        );
        HttpResponseMessage? response = null;
        try
        {
            response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode && !readSuccessBody)
            {
                return response;
            }
            byte[] body = response.IsSuccessStatusCode
                ? await ReadBoundedBytesAsync(response.Content, MaximumJsonResponseBytes, deadline.Token)
                    .ConfigureAwait(false)
                : Encoding.UTF8.GetBytes(
                    await ReadBoundedTextAsync(response.Content, MaximumErrorDetailBytes, deadline.Token)
                        .ConfigureAwait(false)
                );
            response.Content.Dispose();
            response.Content = new ByteArrayContent(body);
            return response;
        }
        catch (IOException exception)
        {
            response?.Dispose();
            throw new HttpRequestException("Raven Colonial response could not be read.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            throw new TaskCanceledException("Raven Colonial response timed out.", exception);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    /// <summary>Deserializes a bounded response and distinguishes optional absence from a malformed required object.</summary>
    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken,
        bool allowNull = false
    )
    {
        if (!response.IsSuccessStatusCode)
        {
            string detail = await ReadBoundedTextAsync(response.Content, MaximumErrorDetailBytes, cancellationToken)
                .ConfigureAwait(false);
            throw new RavenColonialServiceException(response.StatusCode, operation, detail);
        }

        try
        {
            byte[] bytes = await ReadBoundedBytesAsync(response.Content, MaximumJsonResponseBytes, cancellationToken)
                .ConfigureAwait(false);
            T? result = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            if (result is null && allowNull)
            {
                return default!;
            }
            return result
                ?? throw new InvalidDataException($"Raven Colonial returned no data while trying to {operation}.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Raven Colonial returned invalid data while trying to {operation}.",
                exception
            );
        }
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken
    )
    {
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[maximumBytes];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await source
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException($"Raven Colonial returned more than {maximumBytes:N0} bytes.");
        }

        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Raven Colonial returned more than {maximumBytes:N0} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return destination.ToArray();
    }

    private Uri CreateUri(string relativeUri)
    {
        return new Uri(serviceUri, relativeUri);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("The Raven Colonial service URI must be absolute.", nameof(uri));
        }

        return UriPath.EnsureTrailingSeparator(uri);
    }
}

public sealed record ColonizationCurrentShip
{
    [JsonPropertyName("cmdr")]
    public required string CommanderName { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("maxCargo")]
    public required int MaximumCargo { get; init; }

    [JsonPropertyName("cargo")]
    public required IReadOnlyDictionary<string, int> Cargo { get; init; }
}

public sealed class RavenColonialServiceException : HttpRequestException
{
    public RavenColonialServiceException(HttpStatusCode statusCode, string operation, string? responseDetail)
        : base(CreateMessage(statusCode, operation, responseDetail), inner: null, statusCode)
    {
        Operation = operation;
    }

    public string Operation { get; }

    private static string CreateMessage(HttpStatusCode statusCode, string operation, string? detail)
    {
        string message = $"Raven Colonial could not {operation} " + $"(HTTP {(int)statusCode} {statusCode}).";
        if (string.IsNullOrWhiteSpace(detail))
        {
            return message;
        }

        string normalized = detail.Trim();
        if (normalized.Length > 512)
        {
            normalized = normalized[..512] + "...";
        }

        return message + " " + normalized;
    }
}

public sealed record ColonizationCommanderProjects(
    IReadOnlyList<ColonizationProject> Projects,
    IReadOnlyList<string> HiddenProjectIds,
    string? PrimaryProjectId,
    IReadOnlyList<ColonizationFleetCarrier> FleetCarriers
);

public sealed record ColonizationProjectCreate
{
    [JsonPropertyName("buildType")]
    public string BuildType { get; init; } = string.Empty;

    [JsonPropertyName("buildName")]
    public string BuildName { get; init; } = string.Empty;

    [JsonPropertyName("architectName")]
    public string? ArchitectName { get; init; }

    [JsonPropertyName("factionName")]
    public string? FactionName { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("isPrimaryPort")]
    public bool IsPrimaryPort { get; init; }

    [JsonPropertyName("marketId")]
    public long MarketId { get; init; }

    [JsonPropertyName("systemAddress")]
    public long SystemAddress { get; init; }

    [JsonPropertyName("systemName")]
    public string SystemName { get; init; } = string.Empty;

    [JsonPropertyName("starPos")]
    public double[] StarPosition { get; init; } = [];

    [JsonPropertyName("bodyNum")]
    public int? BodyNumber { get; init; }

    [JsonPropertyName("bodyName")]
    public string? BodyName { get; init; }

    [JsonPropertyName("commanders")]
    public Dictionary<string, HashSet<string>> Commanders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("commodities")]
    public Dictionary<string, int> Commodities { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("maxNeed")]
    public int MaximumRequired { get; init; }

    [JsonPropertyName("systemSiteId")]
    public string? SystemSiteId { get; init; }

    [JsonPropertyName("colonisationConstructionDepot")]
    public ColonizationConstructionDepotPayload? ConstructionDepot { get; init; }
}

public sealed record ColonizationProjectUpdate
{
    [JsonPropertyName("buildId")]
    public required string BuildId { get; init; }

    [JsonPropertyName("buildType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuildType { get; init; }

    [JsonPropertyName("buildName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuildName { get; init; }

    [JsonPropertyName("bodyNum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BodyNumber { get; init; }

    [JsonPropertyName("bodyName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BodyName { get; init; }

    [JsonPropertyName("factionName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FactionName { get; init; }

    [JsonPropertyName("architectName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArchitectName { get; init; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; init; }

    [JsonPropertyName("maxNeed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaximumRequired { get; init; }

    [JsonPropertyName("commodities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, int>? Commodities { get; init; }

    [JsonPropertyName("colonisationConstructionDepot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ColonizationConstructionDepotPayload? ConstructionDepot { get; init; }
}

public sealed record ColonizationConstructionDepotPayload
{
    public const string JournalEventName = "ColonisationConstructionDepot";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("event")]
    public string Event { get; init; } = JournalEventName;

    [JsonPropertyName("MarketID")]
    public long MarketId { get; init; }

    [JsonPropertyName("ConstructionProgress")]
    public double ConstructionProgress { get; init; }

    [JsonPropertyName("ConstructionComplete")]
    public bool IsComplete { get; init; }

    [JsonPropertyName("ConstructionFailed")]
    public bool IsFailed { get; init; }

    [JsonPropertyName("ResourcesRequired")]
    public List<ColonizationResourceRequirementPayload> ResourcesRequired { get; init; } = [];

    public static ColonizationConstructionDepotPayload FromSnapshot(ColonizationConstructionDepotSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ColonizationConstructionDepotPayload
        {
            Timestamp = snapshot.Timestamp ?? DateTimeOffset.UtcNow,
            Event = JournalEventName,
            MarketId = snapshot.MarketId,
            ConstructionProgress = snapshot.ReportedProgress,
            IsComplete = snapshot.IsComplete,
            IsFailed = snapshot.IsFailed,
            ResourcesRequired = snapshot
                .Resources.Select(resource => new ColonizationResourceRequirementPayload
                {
                    Name = $"${resource.Name}_name;",
                    LocalizedName = resource.LocalizedName,
                    RequiredAmount = resource.RequiredAmount,
                    ProvidedAmount = resource.ProvidedAmount,
                    Payment = resource.Payment,
                })
                .ToList(),
        };
    }
}

public sealed record ColonizationResourceRequirementPayload
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("Name_Localised")]
    public string LocalizedName { get; init; } = string.Empty;

    [JsonPropertyName("RequiredAmount")]
    public int RequiredAmount { get; init; }

    [JsonPropertyName("ProvidedAmount")]
    public int ProvidedAmount { get; init; }

    [JsonPropertyName("Payment")]
    public int Payment { get; init; }
}

public sealed record ColonizationSystemSite
{
    private ColonizationSystemSiteStatus status;
    private bool hasExplicitStatus;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("bodyNum")]
    public int BodyNumber { get; init; }

    [JsonPropertyName("bodyName")]
    public string? BodyName { get; init; }

    [JsonPropertyName("buildType")]
    public string? BuildType { get; init; }

    [JsonPropertyName("buildId")]
    public string? BuildId { get; init; }

    /// <summary>Identifies the dockable market, accepting legacy numeric and string representations.</summary>
    [JsonPropertyName("marketId")]
    [JsonConverter(typeof(ColonizationLegacyMarketIdConverter))]
    public long? MarketId { get; init; }

    /// <summary>Exposes the effective status while recording whether Raven explicitly provided one.</summary>
    [JsonIgnore]
    public ColonizationSystemSiteStatus Status
    {
        get => status;
        init
        {
            status = value;
            hasExplicitStatus = true;
        }
    }

    /// <summary>Maps optional legacy status values onto the effective status without treating absence as an explicit plan.</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(ColonizationLegacySiteStatusConverter))]
    public ColonizationSystemSiteStatus? SerializedStatus
    {
        get => hasExplicitStatus ? status : null;
        init
        {
            if (value is { } supplied)
            {
                Status = supplied;
            }
        }
    }

    [JsonIgnore]
    public bool HasExplicitStatus => hasExplicitStatus;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public enum ColonizationSystemSiteStatus
{
    Plan,
    Build,
    Complete,
    Demolish,
}

public sealed record ColonizationSystemRecord
{
    [JsonPropertyName("v")]
    public int Version { get; init; }

    [JsonPropertyName("id64")]
    public long SystemAddress { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("architect")]
    public string? Architect { get; init; }

    [JsonPropertyName("open")]
    public bool IsOpen { get; init; }

    [JsonPropertyName("rev")]
    public int Revision { get; init; }

    [JsonPropertyName("reserveLevel")]
    public string? ReserveLevel { get; init; }

    [JsonPropertyName("sites")]
    public List<ColonizationSystemSite> Sites { get; init; } = [];

    [JsonPropertyName("bodies")]
    public List<ColonizationSystemBody>? Bodies { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record ColonizationSystemBody
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("num")]
    public int Number { get; init; }

    [JsonPropertyName("distLS")]
    public double DistanceLightSeconds { get; init; }

    [JsonPropertyName("parents")]
    public List<int> Parents { get; init; } = [];

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("subType")]
    public string? Subtype { get; init; }

    [JsonPropertyName("features")]
    public HashSet<string> Features { get; init; } = [];

    [JsonPropertyName("radius")]
    public double Radius { get; init; } = -1;

    [JsonPropertyName("temp")]
    public double Temperature { get; init; } = -1;

    [JsonPropertyName("gravity")]
    public double Gravity { get; init; } = -1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record ColonizationSystemSiteUpdate
{
    [JsonPropertyName("update")]
    public List<ColonizationSystemSite> UpdatedSites { get; init; } = [];

    [JsonPropertyName("delete")]
    public List<string> DeletedSiteIds { get; init; } = [];

    [JsonPropertyName("orderIDs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? OrderedSiteIds { get; init; }

    [JsonPropertyName("architect")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Architect { get; init; }

    [JsonPropertyName("open")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsOpen { get; init; }

    [JsonPropertyName("reserveLevel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReserveLevel { get; init; }
}

public sealed record ColonizationSystemSitePatch
{
    [JsonPropertyName("marketId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MarketId { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}
