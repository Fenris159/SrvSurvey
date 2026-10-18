using System.Text.Json;

namespace SrvSurvey.Core.Search;

public sealed partial class NearestSystemsClient
{
    /// <summary>Searches explicit descendants through their verified provider mappings, then merges nearest systems.</summary>
    public async Task<NearestSystemsSearchResult> SearchCodexAsync(
        GalacticCoordinate reference,
        CodexSearchQuery query,
        string commanderName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        CodexSearchPlan plan = CodexCatalog.Plan(query);
        using var concurrency = new SemaphoreSlim(3);
        IEnumerable<Func<Task<NearestSystemsSearchResult>>> searches = plan
            .Landmarks.Select(filter =>
                (Func<Task<NearestSystemsSearchResult>>)(
                    () => SearchLandmarksAsync(reference, filter, cancellationToken)
                )
            )
            .Concat(
                plan.CanonnNames.Select(name =>
                    (Func<Task<NearestSystemsSearchResult>>)(
                        () => SearchCanonnAsync(reference, name, commanderName, ResultLimit, cancellationToken)
                    )
                )
            );
        SearchOutcome[] outcomes = await Task.WhenAll(
                searches.Select(async search =>
                {
                    await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await TrySearchAsync(search, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                })
            )
            .ConfigureAwait(false);
        NearestSystemsSearchResult[] results = outcomes
            .Where(outcome => outcome.Result is not null)
            .Select(outcome => outcome.Result!)
            .ToArray();
        if (outcomes.Length > 0 && results.Length == 0)
        {
            throw new HttpRequestException(
                "No Codex location source completed the search. " + outcomes[0].Error?.Message,
                outcomes[0].Error
            );
        }
        NearestSystemSearchRow[] rows = results
            .SelectMany(result => result.Rows)
            .OrderBy(row => row.Distance)
            .GroupBy(row => row.SystemName, StringComparer.OrdinalIgnoreCase)
            .Take(ResultLimit)
            .Select(MergeRows)
            .ToArray();
        var coverage = new List<string>();
        int failures = outcomes.Count(outcome => outcome.Error is not null);
        if (failures > 0)
        {
            coverage.Add($"{failures:N0} location search(es) failed; these results are incomplete.");
        }
        if (plan.CanonnNames.Count > 0)
        {
            coverage.Add("Some entries use Canonn's approximate candidate ordering.");
        }

        if (plan.UnsupportedNames.Count > 0)
        {
            coverage.Add(
                $"No supported location index is available for {plan.UnsupportedNames.Count:N0} selected entry(s)."
            );
        }

        return new(
            rows,
            results
                .Select(result => result.SpanshSearchReference)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        )
        {
            CoverageNote = string.Join(" ", coverage),
        };
    }

    private static async Task<SearchOutcome> TrySearchAsync(
        Func<Task<NearestSystemsSearchResult>> search,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return new(await search().ConfigureAwait(false), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
        {
            return new(null, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, exception);
        }
    }

    private sealed record SearchOutcome(NearestSystemsSearchResult? Result, Exception? Error);

    private static NearestSystemSearchRow MergeRows(IEnumerable<NearestSystemSearchRow> rows)
    {
        NearestSystemSearchRow first = rows.First();
        return first with
        {
            Notes = string.Join("; ", rows.Select(row => row.Notes).Distinct(StringComparer.OrdinalIgnoreCase)),
            Source =
                rows.Select(row => row.Source).Distinct().Count() > 1 ? NearestSystemSource.Combined : first.Source,
            SpanshSearchReference = rows.Select(row => row.SpanshSearchReference)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
        };
    }
}
