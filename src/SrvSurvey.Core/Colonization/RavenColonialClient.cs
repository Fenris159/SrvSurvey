using System.Net;
using System.Net.Http.Json;
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

    Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default);

    Task<string?> GetSystemArchitectAsync(
        string systemNameOrAddress,
        CancellationToken cancellationToken = default);

    Task<ColonizationProject?> CreateProjectAsync(
        ColonizationProjectCreate project,
        CancellationToken cancellationToken = default);
}

public sealed class RavenColonialClient : IRavenColonialClient
{
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

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(
                cancellationToken).ConfigureAwait(false);
            throw new RavenColonialServiceException(
                response.StatusCode,
                operation,
                detail);
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
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
}

public enum ColonizationSystemSiteStatus
{
    Plan,
    Build,
    Complete,
}
