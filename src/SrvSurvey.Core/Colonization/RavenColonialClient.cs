using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

public interface IRavenColonialClient
{
    Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
        string commanderName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
        string commanderName,
        IEnumerable<string> hiddenProjectIds,
        CancellationToken cancellationToken = default);

    Task<ColonizationProject?> GetProjectAsync(
        string buildId,
        CancellationToken cancellationToken = default);

    Task<ColonizationProject?> GetProjectAsync(
        long systemAddress,
        long marketId,
        CancellationToken cancellationToken = default);

    Task<ColonizationProject> UpdateProjectAsync(
        ColonizationProjectUpdate update,
        CancellationToken cancellationToken = default);

    Task MarkProjectCompleteAsync(
        string buildId,
        CancellationToken cancellationToken = default);

    Task ContributeToProjectAsync(
        string buildId,
        string commanderName,
        IReadOnlyDictionary<string, int> contributions,
        CancellationToken cancellationToken = default);

    Task SetPrimaryProjectAsync(
        string commanderName,
        string? buildId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default);

    Task<string?> GetSystemArchitectAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default);

    Task<ColonizationSystemRecord> GetSystemAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default);

    Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default);

    Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
        string systemNameOrAddress,
        ColonizationSystemSiteUpdate update,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<ColonizationProject?> CreateProjectAsync(
        ColonizationProjectCreate project,
        CancellationToken cancellationToken = default);

    Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
        long marketId,
        CancellationToken cancellationToken = default);

    Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
        ColonizationFleetCarrierRegistration carrier,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargo,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargoChanges,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task PublishCurrentShipAsync(
        ColonizationCurrentShip ship,
        string apiKey,
        CancellationToken cancellationToken = default);
}

public sealed class RavenColonialClient : IRavenColonialClient
{
    private const int MaximumJsonResponseBytes = 8 * 1024 * 1024;
    private const int MaximumErrorDetailBytes = 2048;

    public static Uri DefaultServiceUri { get; } = new(
        "https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/");

    public static Uri WebsiteUri { get; } = new("https://ravencolonial.com/");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    private readonly HttpClient httpClient;
    private readonly Uri serviceUri;

    public RavenColonialClient(
        HttpClient? httpClient = null,
        Uri? serviceUri = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.serviceUri = EnsureTrailingSlash(
            serviceUri ?? DefaultServiceUri);
    }

    public async Task<ColonizationCommanderProjects>
        GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        var commander = Uri.EscapeDataString(commanderName.Trim());
        var projectsTask = GetAsync<ColonizationProject[]>(
            $"api/cmdr/{commander}/active",
            "load active colonisation projects",
            cancellationToken);
        var hiddenTask = GetAsync<string[]>(
            $"api/cmdr/{commander}/hiddenIDs",
            "load hidden colonisation projects",
            cancellationToken);
        var primaryTask = GetAsync<string?>(
            $"api/cmdr/{commander}/primary",
            "load the primary colonisation project",
            cancellationToken);
        var fleetCarriersTask = GetAsync<ColonizationFleetCarrier[]>(
            $"api/cmdr/{commander}/fc/all",
            "load commander Fleet Carriers",
            cancellationToken);
        await Task.WhenAll(
                projectsTask,
                hiddenTask,
                primaryTask,
                fleetCarriersTask)
            .ConfigureAwait(false);
        return new ColonizationCommanderProjects(
            await projectsTask.ConfigureAwait(false) ?? [],
            await hiddenTask.ConfigureAwait(false) ?? [],
            await primaryTask.ConfigureAwait(false),
            await fleetCarriersTask.ConfigureAwait(false) ?? []);
    }

    public async Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
        string commanderName,
        IEnumerable<string> hiddenProjectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        ArgumentNullException.ThrowIfNull(hiddenProjectIds);
        var ids = hiddenProjectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(
                $"api/cmdr/{Uri.EscapeDataString(commanderName.Trim())}/hiddenIDs"))
        {
            Content = JsonContent.Create(ids, options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<string[]>(
                response,
                "save hidden colonisation projects",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationProject?> GetProjectAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        using var response = await httpClient.GetAsync(
            CreateUri($"api/project/{Uri.EscapeDataString(buildId.Trim())}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationProject>(
                response,
                "load a colonisation project",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationProject?> GetProjectAsync(
        long systemAddress,
        long marketId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        using var response = await httpClient.GetAsync(
            CreateUri($"api/system/{systemAddress}/{marketId}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationProject>(
                response,
                "load a colonisation project by construction site",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationProject> UpdateProjectAsync(
        ColonizationProjectUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.BuildId);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(
                $"api/project/{Uri.EscapeDataString(update.BuildId.Trim())}"))
        {
            Content = JsonContent.Create(update, options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationProject>(
                response,
                "update a colonisation project",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task MarkProjectCompleteAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        return SendWithoutResponseAsync(
            HttpMethod.Post,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/complete",
            content: null,
            "mark a colonisation project complete",
            cancellationToken);
    }

    public Task ContributeToProjectAsync(
        string buildId,
        string commanderName,
        IReadOnlyDictionary<string, int> contributions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        ArgumentNullException.ThrowIfNull(contributions);
        if (contributions.Any(pair =>
            string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contributions),
                "Project contributions require a commodity name and a positive amount.");
        }

        var normalized = contributions.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        return SendWithoutResponseAsync(
            HttpMethod.Post,
            $"api/project/{Uri.EscapeDataString(buildId.Trim())}/contribute/"
                + Uri.EscapeDataString(commanderName.Trim()),
            JsonContent.Create(normalized, options: JsonOptions),
            "publish a colonisation contribution",
            cancellationToken);
    }

    public Task SetPrimaryProjectAsync(
        string commanderName,
        string? buildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commanderName);
        var relativeUri =
            $"api/cmdr/{Uri.EscapeDataString(commanderName.Trim())}/primary/";
        if (string.IsNullOrWhiteSpace(buildId))
        {
            return SendWithoutResponseAsync(
                HttpMethod.Delete,
                relativeUri,
                content: null,
                "clear the primary colonisation project",
                cancellationToken);
        }

        return SendWithoutResponseAsync(
            HttpMethod.Put,
            relativeUri + Uri.EscapeDataString(buildId.Trim()),
            content: null,
            "set the primary colonisation project",
            cancellationToken);
    }

    public async Task<IReadOnlyList<ColonizationSystemSite>>
        GetSystemSitesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        return await GetAsync<ColonizationSystemSite[]>(
                $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/sites",
                "load planned colonisation sites",
                cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    public Task<string?> GetSystemArchitectAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        return GetAsync<string?>(
            $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/architect",
            "load the system architect",
            cancellationToken);
    }

    public Task<ColonizationSystemRecord> GetSystemAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        return GetAsync<ColonizationSystemRecord>(
            $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}",
            "load a colonisation system",
            cancellationToken)!;
    }

    public async Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(
                $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/import/bodies"));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationSystemRecord>(
                response,
                "import colonisation system bodies",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
        string systemNameOrAddress,
        ColonizationSystemSiteUpdate update,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNameOrAddress);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateUri(
                $"api/v2/system/{Uri.EscapeDataString(systemNameOrAddress.Trim())}/sites"))
        {
            Content = JsonContent.Create(update, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation("rcc-key", apiKey.Trim());
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationSystemRecord>(
                response,
                "update colonisation system sites",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationProject?> CreateProjectAsync(
        ColonizationProjectCreate project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateUri("api/project/"))
        {
            Content = JsonContent.Create(project, options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationProject>(
                response,
                "create a colonisation project",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
        long marketId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        using var response = await httpClient.GetAsync(
            CreateUri($"api/fc/{marketId}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ColonizationFleetCarrier>(
                response,
                "load Fleet Carrier cargo",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
        ColonizationFleetCarrierRegistration carrier,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrier.MarketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(carrier.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateUri($"api/fc/{carrier.MarketId}"))
        {
            Content = JsonContent.Create(carrier, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation("rcc-key", apiKey.Trim());
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<ColonizationFleetCarrier>(
                response,
                "publish the Fleet Carrier",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyDictionary<string, int>>
        ReplaceFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargo,
            string apiKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        if (cargo.Any(pair => pair.Value < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cargo),
                "Replacement cargo counts cannot be negative.");
        }

        return SendFleetCarrierCargoAsync(
            HttpMethod.Post,
            marketId,
            cargo,
            apiKey,
            "replace Fleet Carrier cargo",
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
        long marketId,
        IReadOnlyDictionary<string, int> cargoChanges,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cargoChanges);
        return SendFleetCarrierCargoAsync(
            HttpMethod.Patch,
            marketId,
            cargoChanges,
            apiKey,
            "adjust Fleet Carrier cargo",
            cancellationToken);
    }

    public async Task PublishCurrentShipAsync(
        ColonizationCurrentShip ship,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri("api/cmdr/currentShip"))
        {
            Content = JsonContent.Create(ship, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation("rcc-key", apiKey.Trim());
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadBoundedTextAsync(
                response.Content,
                MaximumErrorDetailBytes,
                cancellationToken).ConfigureAwait(false);
            throw new RavenColonialServiceException(
                response.StatusCode,
                "publish current ship cargo",
                detail);
        }
    }

    private async Task<IReadOnlyDictionary<string, int>>
        SendFleetCarrierCargoAsync(
            HttpMethod method,
            long marketId,
            IReadOnlyDictionary<string, int> cargo,
            string apiKey,
            string operation,
            CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var normalizedCargo = cargo
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage(
            method,
            CreateUri($"api/fc/{marketId}/cargo"))
        {
            Content = JsonContent.Create(normalizedCargo, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation("rcc-key", apiKey.Trim());
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadRequiredAsync<Dictionary<string, int>>(
                response,
                operation,
                cancellationToken)
            .ConfigureAwait(false);
        return new Dictionary<string, int>(
            result,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<T?> GetAsync<T>(
        string relativeUri,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            CreateUri(relativeUri),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<T>(
                response,
                operation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string relativeUri,
        HttpContent? content,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            CreateUri(relativeUri))
        {
            Content = content,
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadBoundedTextAsync(
                response.Content,
                MaximumErrorDetailBytes,
                cancellationToken).ConfigureAwait(false);
            throw new RavenColonialServiceException(
                response.StatusCode,
                operation,
                detail);
        }
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadBoundedTextAsync(
                response.Content,
                MaximumErrorDetailBytes,
                cancellationToken).ConfigureAwait(false);
            throw new RavenColonialServiceException(
                response.StatusCode,
                operation,
                detail);
        }

        try
        {
            var bytes = await ReadBoundedBytesAsync(
                response.Content,
                MaximumJsonResponseBytes,
                cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            return result
                ?? throw new InvalidDataException(
                    $"Raven Colonial returned no data while trying to {operation}.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Raven Colonial returned invalid data while trying to {operation}.",
                exception);
        }
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        var buffer = new byte[maximumBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"Raven Colonial returned more than {maximumBytes:N0} bytes.");
        }

        await using var source = await content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(
                buffer,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"Raven Colonial returned more than {maximumBytes:N0} bytes.");
            }

            destination.Write(buffer, 0, read);
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
            throw new ArgumentException(
                "The Raven Colonial service URI must be absolute.",
                nameof(uri));
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
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
    public RavenColonialServiceException(
        HttpStatusCode statusCode,
        string operation,
        string? responseDetail)
        : base(CreateMessage(statusCode, operation, responseDetail),
            inner: null,
            statusCode)
    {
        Operation = operation;
    }

    public string Operation { get; }

    private static string CreateMessage(
        HttpStatusCode statusCode,
        string operation,
        string? detail)
    {
        var message = $"Raven Colonial could not {operation} "
            + $"(HTTP {(int)statusCode} {statusCode}).";
        if (string.IsNullOrWhiteSpace(detail))
        {
            return message;
        }

        var normalized = detail.Trim();
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
    IReadOnlyList<ColonizationFleetCarrier> FleetCarriers);

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
    public Dictionary<string, HashSet<string>> Commanders { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("commodities")]
    public Dictionary<string, int> Commodities { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("maxNeed")]
    public int MaximumRequired { get; init; }

    [JsonPropertyName("systemSiteId")]
    public string? SystemSiteId { get; init; }

    [JsonPropertyName("colonisationConstructionDepot")]
    public ColonizationConstructionDepotPayload? ConstructionDepot
    {
        get;
        init;
    }
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
    public ColonizationConstructionDepotPayload? ConstructionDepot
    {
        get;
        init;
    }
}

public sealed record ColonizationConstructionDepotPayload
{
    [JsonPropertyName("MarketID")]
    public long MarketId { get; init; }

    [JsonPropertyName("ConstructionProgress")]
    public double ConstructionProgress { get; init; }

    [JsonPropertyName("ConstructionComplete")]
    public bool IsComplete { get; init; }

    [JsonPropertyName("ConstructionFailed")]
    public bool IsFailed { get; init; }

    [JsonPropertyName("ResourcesRequired")]
    public List<ColonizationResourceRequirementPayload> ResourcesRequired
    {
        get;
        init;
    } = [];

    public static ColonizationConstructionDepotPayload FromSnapshot(
        ColonizationConstructionDepotSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ColonizationConstructionDepotPayload
        {
            MarketId = snapshot.MarketId,
            ConstructionProgress = snapshot.ReportedProgress,
            IsComplete = snapshot.IsComplete,
            IsFailed = snapshot.IsFailed,
            ResourcesRequired = snapshot.Resources.Select(resource =>
                new ColonizationResourceRequirementPayload
                {
                    Name = $"${resource.Name}_name;",
                    LocalizedName = resource.LocalizedName,
                    RequiredAmount = resource.RequiredAmount,
                    ProvidedAmount = resource.ProvidedAmount,
                    Payment = resource.Payment,
                }).ToList(),
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
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("bodyNum")]
    public int BodyNumber { get; init; }

    [JsonPropertyName("buildType")]
    public string? BuildType { get; init; }

    [JsonPropertyName("buildId")]
    public string? BuildId { get; init; }

    [JsonPropertyName("marketId")]
    public long? MarketId { get; init; }

    [JsonPropertyName("status")]
    public ColonizationSystemSiteStatus Status { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public enum ColonizationSystemSiteStatus
{
    Plan,
    Build,
    Complete,
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
