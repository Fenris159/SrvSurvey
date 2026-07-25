using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Exobiology;

public sealed class PriorScanPlanner(ExobiologyReferenceCatalog catalog)
{
    private readonly ExobiologyReferenceCatalog catalog = catalog
        ?? throw new ArgumentNullException(nameof(catalog));

    public PriorScanPlan CreatePlan(PriorScanPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BodyName);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MinimumReward);
        if (!double.IsFinite(request.BodyRadiusMeters)
            || request.BodyRadiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The body radius must be positive.");
        }

        if (!double.IsFinite(request.HeadingDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The heading must be finite.");
        }

        var analyzedEntryIds = request.AnalyzedEntryIds.ToHashSet();
        var candidates = request.Signals
            .Where(signal => BodyNamesMatch(
                signal.BodyName,
                request.BodyName))
            .Select(signal => new Candidate(
                signal,
                catalog.FindByEntryId(signal.EntryId)))
            .Where(candidate => candidate.Reference is not null)
            .Where(candidate => !request.SkipLowValue
                || candidate.Reference!.Reward >= request.MinimumReward)
            .Where(candidate => !request.HideOwnSignals
                || !candidate.Signal.IsCommanderScan
                    && !analyzedEntryIds.Contains(candidate.Signal.EntryId))
            .Where(candidate => !IsNearPersonalSample(
                candidate,
                request.PersonalSamples,
                request.BodyRadiusMeters,
                request.HighlightDistanceMeters))
            .GroupBy(candidate => candidate.Signal.EntryId)
            .Select(group => CreateSpecies(
                group,
                request,
                analyzedEntryIds.Contains(group.Key)))
            .Where(species => species.Targets.Count > 0)
            .OrderByDescending(species => species.Reward)
            .ThenBy(species => species.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new PriorScanPlan(candidates);
    }

    private static PriorScanSpecies CreateSpecies(
        IGrouping<long, Candidate> group,
        PriorScanPlanRequest request,
        bool analyzed)
    {
        var first = group.First();
        var reference = first.Reference!;
        var targets = group
            .Select(candidate => CreateTarget(
                candidate.Signal.Location,
                request,
                analyzed))
            .OrderBy(target => target.DistanceMeters)
            .ToList();
        RemoveNearbyDuplicates(
            targets,
            request.BodyRadiusMeters,
            request.HighlightDistanceMeters);
        var displayName = reference.DisplayName
            ?? first.Signal.DisplayName
            ?? reference.SpeciesName;
        var active = string.IsNullOrWhiteSpace(request.ActiveSpeciesName)
            || string.Equals(
                request.ActiveSpeciesName,
                reference.SpeciesName,
                StringComparison.Ordinal);
        return new PriorScanSpecies(
            group.Key,
            reference.SpeciesName,
            displayName,
            reference.Reward,
            analyzed,
            active,
            targets);
    }

    private static PriorScanTarget CreateTarget(
        SurfaceCoordinate location,
        PriorScanPlanRequest request,
        bool analyzed)
    {
        var distance = SurfaceNavigation.GetDistance(
            request.CurrentLocation,
            location,
            request.BodyRadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(
            request.CurrentLocation,
            location);
        var state = analyzed
            ? PriorScanTargetState.Analyzed
            : distance < request.HighlightDistanceMeters
                ? PriorScanTargetState.Close
                : distance > request.FarDistanceMeters
                    ? PriorScanTargetState.Far
                    : PriorScanTargetState.Standard;
        return new PriorScanTarget(
            location,
            distance,
            bearing,
            SurfaceNavigation.NormalizeDegrees(
                bearing - request.HeadingDegrees),
            state);
    }

    private static bool IsNearPersonalSample(
        Candidate candidate,
        IReadOnlyList<PriorScanPersonalSample> personalSamples,
        double bodyRadiusMeters,
        double highlightDistanceMeters)
    {
        return personalSamples.Any(sample => string.Equals(
                sample.SpeciesName,
                candidate.Reference!.SpeciesName,
                StringComparison.Ordinal)
            && SurfaceNavigation.GetDistance(
                sample.Location,
                candidate.Signal.Location,
                bodyRadiusMeters) < highlightDistanceMeters);
    }

    private static bool BodyNamesMatch(string first, string second)
    {
        return string.Equals(
            first.Replace(" ", string.Empty, StringComparison.Ordinal),
            second.Replace(" ", string.Empty, StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveNearbyDuplicates(
        List<PriorScanTarget> targets,
        double bodyRadiusMeters,
        double highlightDistanceMeters)
    {
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            for (var candidateIndex = targets.Count - 1;
                 candidateIndex > index;
                 candidateIndex--)
            {
                if (SurfaceNavigation.GetDistance(
                        target.Location,
                        targets[candidateIndex].Location,
                        bodyRadiusMeters) < highlightDistanceMeters)
                {
                    targets.RemoveAt(candidateIndex);
                }
            }
        }
    }

    private sealed record Candidate(
        CanonnSurfaceBiologySignal Signal,
        ExobiologyReference? Reference);
}

public sealed record PriorScanPlanRequest(
    string BodyName,
    double BodyRadiusMeters,
    SurfaceCoordinate CurrentLocation,
    double HeadingDegrees,
    IReadOnlyList<CanonnSurfaceBiologySignal> Signals,
    IReadOnlyCollection<long> AnalyzedEntryIds,
    IReadOnlyList<PriorScanPersonalSample> PersonalSamples,
    string? ActiveSpeciesName = null,
    bool SkipLowValue = false,
    long MinimumReward = 1_000_000,
    bool HideOwnSignals = false,
    double HighlightDistanceMeters = 150,
    double FarDistanceMeters = 1_000_000);

public sealed record PriorScanPersonalSample(
    string SpeciesName,
    SurfaceCoordinate Location);

public sealed record PriorScanPlan(
    IReadOnlyList<PriorScanSpecies> Species);

public sealed record PriorScanSpecies(
    long EntryId,
    string SpeciesName,
    string DisplayName,
    long Reward,
    bool IsAnalyzed,
    bool IsActive,
    IReadOnlyList<PriorScanTarget> Targets);

public sealed record PriorScanTarget(
    SurfaceCoordinate Location,
    double DistanceMeters,
    double BearingDegrees,
    double RelativeBearingDegrees,
    PriorScanTargetState State);

public enum PriorScanTargetState
{
    Standard,
    Close,
    Far,
    Analyzed,
}
