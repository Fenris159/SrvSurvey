using System.Text.Json;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public interface ISpanshPlannerClient
{
    Task<SpanshRouteReference> SubmitAsync(SpanshPlannerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Submits the same form fields as Spansh's route planners; results use the existing route client.</summary>
public sealed class SpanshPlannerClient(
    HttpClient? client = null,
    Uri? origin = null,
    IStarSystemResolver? resolver = null
) : ISpanshPlannerClient
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient client = client ?? SharedClient;
    private readonly Uri origin = origin ?? SpanshApi.Origin;
    private readonly IStarSystemResolver resolver = resolver ?? new SpanshStarSystemResolver(client, origin);

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }

    public async Task<SpanshRouteReference> SubmitAsync(
        SpanshPlannerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<KeyValuePair<string, string>> parameters = await PrepareParametersAsync(
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, request.Planner.Endpoint))
        {
            Content = new FormUrlEncodedContent(parameters),
        };
        using HttpResponseMessage response = await client
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await BoundedHttpContent
            .ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "Spansh planner response", cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Spansh returned an invalid planner response.");
        }
        if (root.TryGetProperty("error", out JsonElement error))
        {
            throw new InvalidDataException("Spansh: " + error.ToString());
        }
        response.EnsureSuccessStatusCode();
        if (!root.TryGetProperty("job", out JsonElement job) || !Guid.TryParse(job.ToString(), out Guid id))
        {
            throw new InvalidDataException("Spansh did not return a valid route job ID.");
        }
        return new SpanshRouteReference(id, request.Planner.Kind);
    }

    private async Task<IReadOnlyList<KeyValuePair<string, string>>> PrepareParametersAsync(
        SpanshPlannerRequest request,
        CancellationToken cancellationToken
    )
    {
        var parameters = request.Parameters.ToList();
        if (request.Planner.Kind == SpanshRouteKind.Galaxy)
        {
            string slef = parameters.Single(pair => pair.Key == "ship_build").Value;
            parameters.AddRange(SpanshShipBuild.GetJumpParameters(slef));
            parameters.Add(new("max_time", "60"));
        }
        if (request.Planner.Kind is SpanshRouteKind.Galaxy or SpanshRouteKind.FleetCarrier)
        {
            var addresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < parameters.Count; index++)
            {
                KeyValuePair<string, string> pair = parameters[index];
                SpanshPlannerField? field = request.Planner.Fields.FirstOrDefault(field => field.Key == pair.Key);
                if (field?.Type is not (SpanshPlannerFieldType.System or SpanshPlannerFieldType.Systems))
                {
                    continue;
                }
                if (!addresses.TryGetValue(pair.Value, out string? address))
                {
                    IReadOnlyList<StarSystemReference> matches = await resolver
                        .SearchAsync(pair.Value, cancellationToken)
                        .ConfigureAwait(false);
                    StarSystemReference system =
                        matches.FirstOrDefault(system =>
                            system.Name.Equals(pair.Value, StringComparison.OrdinalIgnoreCase)
                        ) ?? throw new ArgumentException($"Spansh could not resolve system '{pair.Value}'.");
                    address = system.SystemAddress.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    addresses.Add(pair.Value, address);
                }
                parameters[index] = new KeyValuePair<string, string>(pair.Key, address);
            }
        }
        return parameters;
    }
}
