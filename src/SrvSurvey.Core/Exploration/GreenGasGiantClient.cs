using System.Net.Http.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exploration;

public interface IGreenGasGiantClient
{
    Task PublishAsync(
        GreenGasGiantCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class GreenGasGiantClient : IGreenGasGiantClient
{
    public static Uri DefaultServiceUri { get; } = new(
        "https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/");

    private readonly HttpClient httpClient;
    private readonly Uri serviceUri;

    public GreenGasGiantClient(
        HttpClient? httpClient = null,
        Uri? serviceUri = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.serviceUri = new Uri(
            EnsureTrailingSlash(serviceUri ?? DefaultServiceUri),
            "api/ggg/create");
    }

    public async Task PublishAsync(
        GreenGasGiantCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var request = new HttpRequestMessage(HttpMethod.Put, serviceUri)
        {
            Content = JsonContent.Create(new
            {
                cmdr = candidate.CommanderName,
                tag = candidate.Tag,
                starPos = new[]
                {
                    candidate.StarPosition.X,
                    candidate.StarPosition.Y,
                    candidate.StarPosition.Z,
                },
                json = candidate.RawJournalJson,
            }),
        };
        using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorAsync(response, cancellationToken)
            .ConfigureAwait(false);
        throw new HttpRequestException(
            $"Raven Colonial rejected the Green Gas Giant candidate "
                + $"({(int)response.StatusCode} {response.ReasonPhrase}){detail}.",
            null,
            response.StatusCode);
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        const int maximumLength = 512;
        var compact = content.Trim().ReplaceLineEndings(" ");
        if (compact.Length > maximumLength)
        {
            compact = compact[..maximumLength] + "...";
        }

        return ": " + compact;
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
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }
}

public sealed record GreenGasGiantCandidate(
    string CommanderName,
    string Tag,
    GalacticCoordinate StarPosition,
    string RawJournalJson);
