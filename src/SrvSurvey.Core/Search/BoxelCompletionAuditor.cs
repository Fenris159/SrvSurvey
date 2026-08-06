using System.Text.Json;

namespace SrvSurvey.Core.Search;

public sealed class BoxelCompletionAuditor
{
    private readonly LegacySystemDataReader localSystemReader;
    private readonly IBoxelSystemResolver systemResolver;

    public BoxelCompletionAuditor(
        LegacySystemDataReader localSystemReader,
        IBoxelSystemResolver systemResolver)
    {
        this.localSystemReader = localSystemReader
            ?? throw new ArgumentNullException(nameof(localSystemReader));
        this.systemResolver = systemResolver
            ?? throw new ArgumentNullException(nameof(systemResolver));
    }

    public async Task<BoxelCompletionAuditResult> AuditAsync(
        BoxelCompletionAuditRequest request,
        IProgress<BoxelCompletionAuditProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FrontierId);
        ArgumentNullException.ThrowIfNull(request.Boxels);
        ArgumentNullException.ThrowIfNull(request.EmptyPrefixes);
        ArgumentNullException.ThrowIfNull(request.RouteSystems);
        var entries = new List<BoxelCompletionAuditEntry>();
        var errors = new List<string>();
        var processed = 0;

        try
        {
            var local = await localSystemReader.ReadAllAsync(
                    request.FrontierId,
                    cancellationToken)
                .ConfigureAwait(false);
            errors.AddRange(local.Errors);
            var localByPrefix = GroupByPrefix(local.Systems);
            var routeByPrefix = GroupByPrefix(request.RouteSystems);

            foreach (var boxel in request.Boxels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BoxelCompletionAuditEntry? entry = null;
                if (!string.Equals(
                        boxel.Prefix,
                        request.ExcludedPrefix,
                        StringComparison.Ordinal))
                {
                    entry = request.EmptyPrefixes.Contains(boxel.Prefix)
                        ? new BoxelCompletionAuditEntry(boxel, -1, false, true)
                        : await AuditBoxelAsync(
                                boxel,
                                localByPrefix.GetValueOrDefault(boxel.Prefix) ?? [],
                                routeByPrefix.GetValueOrDefault(boxel.Prefix) ?? [],
                                request,
                                errors,
                                cancellationToken)
                            .ConfigureAwait(false);
                    entries.Add(entry);
                }

                processed++;
                progress?.Report(new BoxelCompletionAuditProgress(
                    processed,
                    request.Boxels.Count,
                    boxel.Prefix,
                    entry));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BoxelCompletionAuditResult(
                entries,
                errors,
                true,
                processed,
                request.Boxels.Count);
        }

        return new BoxelCompletionAuditResult(
            entries,
            errors,
            false,
            processed,
            request.Boxels.Count);
    }

    private async Task<BoxelCompletionAuditEntry> AuditBoxelAsync(
        BoxelAddress boxel,
        IReadOnlyList<BoxelSystemObservation> localSystems,
        IReadOnlyList<BoxelSystemObservation> routeSystems,
        BoxelCompletionAuditRequest request,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var systems = new Dictionary<string, AuditedSystem>(StringComparer.Ordinal);
        Merge(systems, localSystems, AuditObservationSource.LocalProfile, request);
        Merge(systems, routeSystems, AuditObservationSource.NavRoute, request);
        try
        {
            var spanshSystems = await systemResolver.SearchAsync(boxel, cancellationToken)
                .ConfigureAwait(false);
            Merge(systems, spanshSystems, AuditObservationSource.Spansh, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or JsonException)
        {
            errors.Add($"Spansh audit failed for {boxel.Prefix}: {exception.Message}");
        }

        var systemCount = systems.Count == 0
            ? 0
            : systems.Values.Max(system => system.Boxel.N2) + 1;
        return new BoxelCompletionAuditEntry(
            boxel,
            systemCount,
            systems.Count > 0 && systems.Values.All(system => system.IsComplete),
            false);
    }

    private static Dictionary<string, IReadOnlyList<BoxelSystemObservation>> GroupByPrefix(
        IEnumerable<BoxelSystemObservation> systems)
    {
        return systems
            .GroupBy(system => system.Boxel.Prefix, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BoxelSystemObservation>)group.ToArray(),
                StringComparer.Ordinal);
    }

    private static void Merge(
        IDictionary<string, AuditedSystem> systems,
        IEnumerable<BoxelSystemObservation> observations,
        AuditObservationSource source,
        BoxelCompletionAuditRequest request)
    {
        foreach (var observation in observations)
        {
            systems.TryGetValue(observation.Boxel.GeneratedName, out var existing);
            var isComplete = existing?.IsComplete ?? false;
            if (source == AuditObservationSource.LocalProfile)
            {
                isComplete |= request.CompletionMode == BoxelCompletionMode.FssAllBodies
                    ? observation.FssAllBodies
                        && observation.VisitedAt > request.StartedOn
                    : observation.VisitedAt > request.StartedOn
                        || request.SkipAlreadyVisited;
            }
            else if (source == AuditObservationSource.Spansh
                && request.CompletionMode == BoxelCompletionMode.EnterSystem
                && observation.HasKnownBodies
                && request.SkipKnownToSpansh)
            {
                isComplete |= observation.SpanshUpdatedAt < request.StartedOn;
            }

            systems[observation.Boxel.GeneratedName] = new AuditedSystem(
                observation.Boxel,
                isComplete);
        }
    }

    private sealed record AuditedSystem(BoxelAddress Boxel, bool IsComplete);

    private enum AuditObservationSource
    {
        LocalProfile,
        NavRoute,
        Spansh,
    }
}

public sealed record BoxelCompletionAuditRequest(
    string FrontierId,
    IReadOnlyList<BoxelAddress> Boxels,
    IReadOnlySet<string> EmptyPrefixes,
    string? ExcludedPrefix,
    DateTimeOffset StartedOn,
    bool SkipAlreadyVisited,
    bool SkipKnownToSpansh,
    BoxelCompletionMode CompletionMode,
    IReadOnlyList<BoxelSystemObservation> RouteSystems);

public sealed record BoxelCompletionAuditEntry(
    BoxelAddress Boxel,
    int SystemCount,
    bool IsComplete,
    bool IsEmpty);

public sealed record BoxelCompletionAuditProgress(
    int Processed,
    int Total,
    string Prefix,
    BoxelCompletionAuditEntry? Entry);

public sealed record BoxelCompletionAuditResult(
    IReadOnlyList<BoxelCompletionAuditEntry> Entries,
    IReadOnlyList<string> Errors,
    bool WasCancelled,
    int Processed,
    int Total);
