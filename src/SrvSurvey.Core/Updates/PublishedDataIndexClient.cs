using System.Net.Http.Headers;
using System.Text.Json;

namespace SrvSurvey.Core.Updates;

public interface IPublishedDataIndexClient
{
    Task<PublishedDataIndex> GetAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PublishedDataIndex(
    Version GitHubVersion,
    Version MicrosoftStoreVersion,
    int BiologyCriteriaVersion,
    int BiologyEngineVersion,
    int CodexReferenceVersion,
    int SettlementTemplateVersion,
    int GuardianVersion,
    int SettlementsVersion,
    int NicknamesVersion,
    int GreenGasGiantsVersion);

public sealed class PublishedDataIndexClient : IPublishedDataIndexClient
{
    public static readonly Uri DefaultIndexUri = new(
        "https://njthomson.github.io/SrvSurvey/data.json");

    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri indexUri;

    public PublishedDataIndexClient(
        HttpClient? client = null,
        Uri? indexUri = null)
    {
        this.client = client ?? SharedClient;
        this.indexUri = indexUri ?? DefaultIndexUri;
    }

    public async Task<PublishedDataIndex> GetAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, indexUri);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        };
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The published-data index must contain a JSON object.");
        }

        return new PublishedDataIndex(
            ReadVersion(root, "ghVer"),
            ReadVersion(root, "msVer"),
            ReadNonNegativeInt(root, "bioCriteria"),
            ReadNonNegativeInt(root, "bioEngine"),
            ReadNonNegativeInt(root, "codexRef"),
            ReadNonNegativeInt(root, "settlementTemplate"),
            ReadNonNegativeInt(root, "guardian"),
            ReadNonNegativeInt(root, "settlements"),
            ReadNonNegativeInt(root, "nicknames"),
            ReadNonNegativeInt(root, "ggg"));
    }

    private static Version ReadVersion(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || !Version.TryParse(property.GetString(), out var version)
            || version.Major < 0
            || version.Minor < 0)
        {
            throw new InvalidDataException(
                $"The published-data index has an invalid '{propertyName}' value.");
        }

        return version;
    }

    private static int ReadNonNegativeInt(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt32(out var value)
            || value < 0)
        {
            throw new InvalidDataException(
                $"The published-data index has an invalid '{propertyName}' value.");
        }

        return value;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }
}
