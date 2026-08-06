using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public sealed class RouteNameImporter(IStarSystemResolver resolver)
{
    public async Task<RouteNameImportResult> ImportAsync(
        IEnumerable<string> names,
        IProgress<RouteNameImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var normalizedNames = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();
        var hops = new List<FollowRouteHop>(normalizedNames.Length);
        var resolvedCount = 0;
        for (var index = 0; index < normalizedNames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedName = normalizedNames[index];
            var matches = await resolver.SearchAsync(
                    requestedName,
                    cancellationToken)
                .ConfigureAwait(false);
            var match = matches.Count > 0 ? matches[0] : null;
            if (match is not null)
            {
                resolvedCount++;
            }

            hops.Add(new FollowRouteHop(
                match?.Name ?? requestedName,
                match?.SystemAddress,
                match?.Position,
                null,
                false,
                false));
            progress?.Report(new RouteNameImportProgress(
                index + 1,
                normalizedNames.Length,
                requestedName,
                match is not null));
        }

        return new RouteNameImportResult(
            hops,
            resolvedCount,
            hops.Count - resolvedCount);
    }

    public static IReadOnlyList<string> ParseNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();
    }
}

public sealed record RouteNameImportResult(
    IReadOnlyList<FollowRouteHop> Hops,
    int ResolvedCount,
    int UnresolvedCount);

public sealed record RouteNameImportProgress(
    int CompletedCount,
    int TotalCount,
    string SystemName,
    bool Resolved);
