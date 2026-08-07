using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Quests;

public interface IRavenQuestClient
{
    Task<IReadOnlyList<RavenQuestDefinition>> GetPublishedQuestsAsync(
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    Task<RavenQuestDefinition?> GetQuestAsync(
        RavenQuestReference reference,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    Task<string> PublishQuestAsync(
        RavenQuestDefinition quest,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task SaveCommanderQuestAsync(
        RavenCommanderQuest quest,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RavenCommanderQuest>> LoadCommanderQuestsAsync(
        RavenQuestState state,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RavenCommanderQuestStatus>>
        GetCommanderQuestStatusesAsync(
            string apiKey,
            CancellationToken cancellationToken = default);

    Task<RavenQuestDefinition> ActivateQuestAsync(
        string publisher,
        string id,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteQuestAsync(
        string publisher,
        string id,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<bool> SetQuestStateAsync(
        string publisher,
        string id,
        RavenQuestState state,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<string?> GetQuestChapterAsync(
        RavenQuestReference reference,
        string chapterId,
        string? apiKey = null,
        CancellationToken cancellationToken = default);
}

public sealed class RavenQuestClient : IRavenQuestClient
{
    private const int MaximumJsonResponseBytes = 8 * 1024 * 1024;
    private const int MaximumChapterBytes = 8 * 1024 * 1024;
    private const int MaximumErrorDetailBytes = 2 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly HttpClient httpClient;
    private readonly Uri serviceUri;

    public RavenQuestClient(
        HttpClient? httpClient = null,
        Uri? serviceUri = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.serviceUri = EnsureTrailingSlash(
            serviceUri ?? RavenColonialClient.DefaultServiceUri);
    }

    public async Task<IReadOnlyList<RavenQuestDefinition>>
        GetPublishedQuestsAsync(
            string? apiKey = null,
            CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "api/quest/published",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (IsUnavailableQuestResponse(response.StatusCode))
        {
            return [];
        }

        return await ReadRequiredAsync<RavenQuestDefinition[]>(
                response,
                "load published quests",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RavenQuestDefinition?> GetQuestAsync(
        RavenQuestReference reference,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        using var request = CreateRequest(
            HttpMethod.Get,
            DefinitionPath(reference, trailingSlash: true),
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<RavenQuestDefinition>(
                response,
                "load a quest definition",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> PublishQuestAsync(
        RavenQuestDefinition quest,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quest);
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Post,
            "api/quest/publish",
            apiKey);
        request.Content = JsonContent.Create(quest, options: JsonOptions);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "publish a quest",
                cancellationToken)
            .ConfigureAwait(false);
        return response.ReasonPhrase ?? "OK";
    }

    public async Task SaveCommanderQuestAsync(
        RavenCommanderQuest quest,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ValidateIdentity(quest.Publisher, quest.Id);
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Post,
            $"api/quest/cmdr/save/{Escape(quest.Publisher)}/{Escape(quest.Id)}",
            apiKey);
        var payload = JsonSerializer.SerializeToNode(quest, JsonOptions)
            as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException(
                "Commander quest progress could not be serialized.");
        // The legacy load response includes its hydrated definition, while the
        // save contract accepts progress only.
        payload.Remove("quest");
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "save commander quest progress",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RavenCommanderQuest>>
        LoadCommanderQuestsAsync(
            RavenQuestState state,
            string apiKey,
            CancellationToken cancellationToken = default)
    {
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Post,
            $"api/quest/cmdr/load/{state}",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (IsUnavailableQuestResponse(response.StatusCode))
        {
            return [];
        }

        return await ReadRequiredAsync<RavenCommanderQuest[]>(
                response,
                "load commander quests",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RavenCommanderQuestStatus>>
        GetCommanderQuestStatusesAsync(
            string apiKey,
            CancellationToken cancellationToken = default)
    {
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Get,
            "api/quest/cmdr",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (IsUnavailableQuestResponse(response.StatusCode))
        {
            return [];
        }

        return await ReadRequiredAsync<RavenCommanderQuestStatus[]>(
                response,
                "load commander quest statuses",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RavenQuestDefinition> ActivateQuestAsync(
        string publisher,
        string id,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(publisher, id);
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Put,
            $"api/quest/cmdr/{Escape(publisher)}/{Escape(id)}",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await ReadRequiredAsync<RavenQuestDefinition>(
                response,
                "activate a quest",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteQuestAsync(
        string publisher,
        string id,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(publisher, id);
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Delete,
            $"api/quest/cmdr/{Escape(publisher)}/{Escape(id)}",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(
                response,
                "delete a commander quest",
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SetQuestStateAsync(
        string publisher,
        string id,
        RavenQuestState state,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(publisher, id);
        using var request = CreateRequiredKeyRequest(
            HttpMethod.Post,
            $"api/quest/cmdr/{Escape(publisher)}/{Escape(id)}/state/{state}",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(
                response,
                "change commander quest state",
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<string?> GetQuestChapterAsync(
        RavenQuestReference reference,
        string chapterId,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"{DefinitionPath(reference, trailingSlash: false)}/chapter/{Escape(chapterId)}",
            apiKey);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (IsUnavailableQuestResponse(response.StatusCode))
        {
            return null;
        }

        await EnsureSuccessAsync(
                response,
                "load a quest chapter",
                cancellationToken)
            .ConfigureAwait(false);
        return await BoundedHttpContent.ReadStringAsync(
                response.Content,
                MaximumChapterBytes,
                "The Raven Colonial quest chapter response",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequiredKeyRequest(
        HttpMethod method,
        string relativeUri,
        string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return CreateRequest(method, relativeUri, apiKey);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativeUri,
        string? apiKey)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(serviceUri, relativeUri));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("rcc-key", apiKey.Trim());
        }

        return request;
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, operation, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await BoundedHttpContent.ReadFromJsonAsync<T>(
                    response.Content,
                    MaximumJsonResponseBytes,
                    "The Raven Colonial quest response",
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
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

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await BoundedHttpContent.ReadStringPrefixAsync(
                response.Content,
                MaximumErrorDetailBytes,
                cancellationToken)
            .ConfigureAwait(false);
        throw new RavenColonialServiceException(
            response.StatusCode,
            operation,
            detail);
    }

    private static bool IsUnavailableQuestResponse(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.NotFound
            or HttpStatusCode.Unauthorized;
    }

    private static string DefinitionPath(
        RavenQuestReference reference,
        bool trailingSlash)
    {
        ValidateIdentity(reference.Publisher, reference.Id);
        if (!double.IsFinite(reference.Version))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reference),
                "The quest version must be finite.");
        }

        var path = "api/quest/"
            + $"{Escape(reference.Publisher)}/{Escape(reference.Id)}/"
            + reference.Version.ToString(CultureInfo.InvariantCulture);
        return trailingSlash ? path + "/" : path;
    }

    private static void ValidateIdentity(string publisher, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
    }

    private static string Escape(string value)
    {
        return Uri.EscapeDataString(value.Trim());
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

        return UriPath.EnsureTrailingSeparator(uri);
    }
}

public sealed record RavenQuestReference(
    string Publisher,
    string Id,
    double Version)
{
    public override string ToString()
    {
        return $"{Publisher}|{Id}|{Version.ToString(CultureInfo.InvariantCulture)}";
    }
}

public sealed record RavenQuestDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("ver")]
    public double Version { get; init; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("subTitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("desc")]
    public string? Description { get; init; }

    [JsonPropertyName("tags")]
    public HashSet<string> Tags { get; init; } = [];

    [JsonPropertyName("duration")]
    public RavenQuestDuration Duration { get; init; }

    [JsonPropertyName("onlySquadrons")]
    public HashSet<string> OnlySquadrons { get; init; } = [];

    [JsonPropertyName("onlyCmdrs")]
    public HashSet<string> OnlyCommanders { get; init; } = [];

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    [JsonPropertyName("firstChapter")]
    public string FirstChapter { get; init; } = string.Empty;

    [JsonPropertyName("objectives")]
    public Dictionary<string, string> Objectives { get; init; } = [];

    [JsonPropertyName("strings")]
    public Dictionary<string, string> Strings { get; init; } = [];

    [JsonPropertyName("msgs")]
    public List<RavenQuestMessageDefinition> Messages { get; init; } = [];

    [JsonPropertyName("chapters")]
    public Dictionary<string, string> Chapters { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];

    [JsonIgnore]
    public RavenQuestReference Reference => new(Publisher, Id, Version);
}

public sealed record RavenQuestMessageDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("actions")]
    public Dictionary<string, string>? Actions { get; init; }

    [JsonPropertyName("tags")]
    public HashSet<string>? Tags { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<RavenQuestDuration>))]
public enum RavenQuestDuration
{
    Unknown,
    Short,
    Medium,
    Long,
    Extended,
}

public sealed record RavenCommanderQuest
{
    [JsonPropertyName("publisher")]
    public string Publisher { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("ver")]
    public double Version { get; init; }

    [JsonPropertyName("quest")]
    public RavenQuestDefinition? Quest { get; init; }

    [JsonPropertyName("objectives")]
    public Dictionary<string, string> Objectives { get; init; } = [];

    [JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; init; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset? EndTime { get; init; }

    [JsonPropertyName("paused")]
    public bool Paused { get; init; }

    [JsonPropertyName("tags")]
    public HashSet<string> Tags { get; init; } = [];

    [JsonPropertyName("bodyLocations")]
    public Dictionary<string, string> BodyLocations { get; init; } = [];

    [JsonPropertyName("chapters")]
    public List<RavenQuestChapterState> Chapters { get; init; } = [];

    [JsonPropertyName("msgs")]
    public List<RavenQuestMessage> Messages { get; init; } = [];

    [JsonPropertyName("vars")]
    public Dictionary<string, JsonElement> Variables { get; init; } = [];

    [JsonPropertyName("keptLasts")]
    public Dictionary<string, JsonElement> KeptJournalEvents { get; init; } = [];

    [JsonPropertyName("routes")]
    public List<RavenQuestRoute> Routes { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];

    [JsonIgnore]
    public RavenQuestReference Reference => new(Publisher, Id, Version);
}

public sealed record RavenQuestChapterState
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; init; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset? EndTime { get; init; }

    [JsonPropertyName("vars")]
    public Dictionary<string, JsonElement> Variables { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RavenQuestMessage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("received")]
    public DateTimeOffset Received { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("chapter")]
    public string? Chapter { get; init; }

    [JsonPropertyName("actions")]
    public string[]? Actions { get; init; }

    [JsonPropertyName("read")]
    public bool Read { get; init; }

    [JsonPropertyName("replied")]
    public string? Replied { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RavenQuestRoute
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("w")]
    public double Width { get; init; }

    [JsonPropertyName("wp")]
    public List<double[]> Waypoints { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RavenCommanderQuestStatus
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("ver")]
    public double Version { get; init; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public RavenQuestState State { get; init; }

    [JsonPropertyName("stateChangedOn")]
    public DateTimeOffset StateChangedOn { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<RavenQuestState>))]
public enum RavenQuestState
{
    unknown,
    active,
    paused,
    complete,
    failed,
}
