using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record BiologySurveyViewModel(
    BiologySurveyMode Mode,
    int? SelectedBodyId,
    string Heading,
    string ProgressText,
    IReadOnlyList<BiologyBodyRowViewModel> Bodies,
    IReadOnlyList<BiologyOrganismRowViewModel> Organisms,
    string RewardSummary,
    string FirstFootfallRewardSummary,
    int RadicoidaUnicaCount,
    bool RequiresDss,
    string PredictionStatus,
    int GeologicalSignalCount,
    IReadOnlyList<string> GeologicalSignals)
{
    public static BiologySurveyViewModel Empty { get; } = new(
        BiologySurveyMode.System,
        null,
        string.Empty,
        string.Empty,
        [],
        [],
        string.Empty,
        string.Empty,
        0,
        false,
        string.Empty,
        0,
        []);

    public bool IsBodyDetail => Mode == BiologySurveyMode.Body;

    public bool IsSystemOverview => Mode == BiologySurveyMode.System;

    public bool HasBodies => Bodies.Count > 0;

    public bool HasOrganisms => Organisms.Count > 0;

    public bool HasRewardSummary => !string.IsNullOrWhiteSpace(RewardSummary);

    public bool HasFirstFootfallRewardSummary => !string.IsNullOrWhiteSpace(
        FirstFootfallRewardSummary);

    public bool HasRadicoidaUnicaCount => RadicoidaUnicaCount > 0;

    public string RadicoidaUnicaCountText =>
        $"Radicoida scans: {RadicoidaUnicaCount:N0}";

    public bool HasGeologicalSignals => GeologicalSignalCount > 0;

    public bool HasPredictionStatus => !string.IsNullOrWhiteSpace(
        PredictionStatus);

    public int UnidentifiedGeologicalSignalCount => Math.Max(
        0,
        GeologicalSignalCount - GeologicalSignals.Count);

    public bool HasUnidentifiedGeologicalSignals =>
        UnidentifiedGeologicalSignalCount > 0;

    public string UnidentifiedGeologicalSignalsText =>
        UnidentifiedGeologicalSignalCount == 1
            ? "1 geological signal unidentified"
            : $"{UnidentifiedGeologicalSignalCount:N0} geological signals unidentified";

    public static BiologySurveyViewModel? Create(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        ExobiologySnapshot exobiology,
        bool drawBodyBiosOnlyWhenNear,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions,
        BiologyDiscoveryContext? discoveryContext = null,
        BiologyRewardThresholds? rewardThresholds = null,
        BiologyPredictionEvaluator? predictionEvaluator = null,
        ExobiologyReferenceCatalog? referenceCatalog = null,
        IReadOnlySet<int>? canonnBiologyBodyIds = null,
        bool allowRetainedCurrentBody = true,
        bool forceSystemOverview = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        var biologicalBodies = snapshot.Bodies
            .Where(body => body.BiologicalSignalCount > 0)
            .OrderBy(body => body.BodyId)
            .ToArray();
        if (snapshot.SystemAddress is null || biologicalBodies.Length == 0)
        {
            return null;
        }

        var body = ResolveBody(
            snapshot,
            status,
            biologicalBodies,
            drawBodyBiosOnlyWhenNear,
            allowRetainedCurrentBody,
            forceSystemOverview);
        return body is null
            ? CreateSystem(
                snapshot,
                status,
                biologicalBodies,
                highlightRegionalFirsts,
                discoveryContext ?? BiologyDiscoveryContext.Unavailable,
                disablePredictions,
                exobiology.CountRadicoidaUnica,
                rewardThresholds ?? BiologyRewardThresholds.Default,
                predictionEvaluator ?? DefaultPredictionEvaluator.Value,
                referenceCatalog ?? DefaultBioReferenceCatalog.Value,
                canonnBiologyBodyIds)
            : CreateBody(
                snapshot,
                body,
                exobiology,
                highlightRegionalFirsts,
                dimAnalyzedOrganisms,
                hideGeoCount,
                disablePredictions,
                discoveryContext ?? BiologyDiscoveryContext.Unavailable,
                rewardThresholds ?? BiologyRewardThresholds.Default,
                predictionEvaluator ?? DefaultPredictionEvaluator.Value,
                referenceCatalog ?? DefaultBioReferenceCatalog.Value);
    }

    public static BiologySurveyViewModel? CreateSystemOverview(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        bool disablePredictions,
        BiologyRewardThresholds? rewardThresholds = null,
        BiologyPredictionEvaluator? predictionEvaluator = null,
        ExobiologyReferenceCatalog? referenceCatalog = null,
        int radicoidaUnicaCount = 0,
        bool highlightRegionalFirsts = false,
        BiologyDiscoveryContext? discoveryContext = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var biologicalBodies = snapshot.Bodies
            .Where(body => body.BiologicalSignalCount > 0)
            .OrderBy(body => body.BodyId)
            .ToArray();
        return snapshot.SystemAddress is null || biologicalBodies.Length == 0
            ? null
            : CreateSystem(
                snapshot,
                status,
                biologicalBodies,
                highlightRegionalFirsts,
                discoveryContext ?? BiologyDiscoveryContext.Unavailable,
                disablePredictions,
                radicoidaUnicaCount,
                rewardThresholds ?? BiologyRewardThresholds.Default,
                predictionEvaluator ?? DefaultPredictionEvaluator.Value,
                referenceCatalog ?? DefaultBioReferenceCatalog.Value,
                canonnBiologyBodyIds: null);
    }

    public static BiologySurveyViewModel? CreateBodyDetail(
        SystemScanSnapshot snapshot,
        int bodyId,
        ExobiologySnapshot exobiology,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions,
        BiologyDiscoveryContext? discoveryContext = null,
        BiologyRewardThresholds? rewardThresholds = null,
        BiologyPredictionEvaluator? predictionEvaluator = null,
        ExobiologyReferenceCatalog? referenceCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        var body = snapshot.Bodies.FirstOrDefault(candidate =>
            candidate.BodyId == bodyId
            && candidate.BiologicalSignalCount > 0);
        return snapshot.SystemAddress is null || body is null
            ? null
            : CreateBody(
                snapshot,
                body,
                exobiology,
                highlightRegionalFirsts,
                dimAnalyzedOrganisms,
                hideGeoCount,
                disablePredictions,
                discoveryContext ?? BiologyDiscoveryContext.Unavailable,
                rewardThresholds ?? BiologyRewardThresholds.Default,
                predictionEvaluator ?? DefaultPredictionEvaluator.Value,
                referenceCatalog ?? DefaultBioReferenceCatalog.Value);
    }

    public static IReadOnlyList<BiologySignalRewardBandViewModel>
        CreateRewardBandsForBody(
            SystemScanSnapshot snapshot,
            SystemScanBodySnapshot body,
            bool disablePredictions,
            BiologyRewardThresholds? rewardThresholds = null,
            BiologyPredictionEvaluator? predictionEvaluator = null,
            ExobiologyReferenceCatalog? referenceCatalog = null,
            bool highlightRegionalFirsts = false,
            BiologyDiscoveryContext? discoveryContext = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(body);
        var thresholds = rewardThresholds ?? BiologyRewardThresholds.Default;
        var predictions = CreatePredictions(
            snapshot,
            body,
            disablePredictions,
            predictionEvaluator ?? DefaultPredictionEvaluator.Value,
            referenceCatalog ?? DefaultBioReferenceCatalog.Value);
        return CreateSystemRewardBands(
            body,
            predictions,
            highlightRegionalFirsts,
            discoveryContext ?? BiologyDiscoveryContext.Unavailable,
            thresholds);
    }

    private static BiologySurveyViewModel CreateSystem(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies,
        bool highlightRegionalFirsts,
        BiologyDiscoveryContext discoveryContext,
        bool disablePredictions,
        int radicoidaUnicaCount,
        BiologyRewardThresholds rewardThresholds,
        BiologyPredictionEvaluator predictionEvaluator,
        ExobiologyReferenceCatalog referenceCatalog,
        IReadOnlySet<int>? canonnBiologyBodyIds)
    {
        var destinationBodyId = status?.Destination is { } destination
            && destination.System == snapshot.SystemAddress
            ? destination.Body
            : (int?)null;
        var currentBodyId = ResolveCurrentBody(snapshot, status)?.BodyId;
        var rowData = biologicalBodies
            .Select(body =>
            {
                var predictions = CreatePredictions(
                    snapshot,
                    body,
                    disablePredictions,
                    predictionEvaluator,
                    referenceCatalog);
                var estimate = CreateRewardEstimate(body, predictions);
                var rewardBands = CreateSystemRewardBands(
                    body,
                    predictions,
                    highlightRegionalFirsts,
                    discoveryContext,
                    rewardThresholds);
                var row = new BiologyBodyRowViewModel(
                    body.BodyId,
                    body.ShortName,
                    body.AnalyzedBiologicalSignalCount,
                    body.BiologicalSignalCount,
                    estimate.KnownReward,
                    estimate.MinimumReward,
                    estimate.MaximumReward,
                    estimate.HasPredictedReward,
                    estimate.HasUnknownReward,
                    body.BodyId == destinationBodyId,
                    body.BodyId == currentBodyId,
                    canonnBiologyBodyIds?.Contains(body.BodyId) == true,
                    rewardBands,
                    rewardThresholds.BucketOneMillions,
                    rewardThresholds.BucketTwoMillions,
                    rewardThresholds.BucketThreeMillions);
                return new { Row = row, Estimate = estimate };
            })
            .ToArray();
        var rows = rowData.Select(item => item.Row).ToArray();
        var analyzed = biologicalBodies.Sum(
            body => body.AnalyzedBiologicalSignalCount);
        var total = biologicalBodies.Sum(body => body.BiologicalSignalCount);
        var knownSystemReward = rows.Sum(row => row.KnownReward);
        var minimumSystemReward = rowData.Sum(
            item => item.Estimate.MinimumReward);
        var maximumSystemReward = rowData.Sum(
            item => item.Estimate.MaximumReward);
        var hasPredictedReward = rowData.Any(
            item => item.Estimate.HasPredictedReward);
        var hasUnknownReward = rowData.Any(
            item => item.Estimate.HasUnknownReward);

        return new BiologySurveyViewModel(
            BiologySurveyMode.System,
            null,
            snapshot.SystemName ?? "Current system",
            $"{analyzed:N0} of {total:N0} biological signals analyzed",
            rows,
            [],
            hasPredictedReward
                ? FormatEstimatedReward(
                    minimumSystemReward,
                    maximumSystemReward,
                    hasUnknownReward)
                : FormatKnownReward(knownSystemReward, hasUnknownReward),
            string.Empty,
            radicoidaUnicaCount,
            false,
            string.Empty,
            0,
            []);
    }

    private static BiologySurveyViewModel CreateBody(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        ExobiologySnapshot exobiology,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions,
        BiologyDiscoveryContext discoveryContext,
        BiologyRewardThresholds rewardThresholds,
        BiologyPredictionEvaluator predictionEvaluator,
        ExobiologyReferenceCatalog referenceCatalog)
    {
        var predictionSet = CreatePredictions(
            snapshot,
            body,
            disablePredictions,
            predictionEvaluator,
            referenceCatalog);
        var predictionsByGenus = predictionSet.Predictions
            .GroupBy(
                prediction => prediction.Prediction.Genus,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var consumedPredictions = new HashSet<string>(StringComparer.Ordinal);
        var organisms = new List<BiologyOrganismRowViewModel>();
        foreach (var organism in body.Organisms)
        {
            var genusName = organism.GenusLocalized
                ?? FormatJournalName(organism.Genus);
            if (organism.Variant is null
                && predictionsByGenus.TryGetValue(genusName, out var predictions))
            {
                foreach (var prediction in predictions)
                {
                    organisms.Add(CreatePrediction(
                        body,
                        prediction,
                        exobiology,
                        highlightRegionalFirsts,
                        discoveryContext,
                        rewardThresholds));
                    consumedPredictions.Add(prediction.Prediction.Name);
                }

                continue;
            }

            organisms.Add(CreateOrganism(
                body,
                organism,
                exobiology,
                highlightRegionalFirsts,
                dimAnalyzedOrganisms,
                discoveryContext,
                rewardThresholds));
        }

        foreach (var prediction in predictionSet.Predictions.Where(
                     prediction => !consumedPredictions.Contains(
                         prediction.Prediction.Name)))
        {
            if (body.Organisms.Any(organism => prediction.Reference is not null
                    && (organism.Variant == prediction.Reference.VariantName
                        || organism.Species == prediction.Reference.SpeciesName)))
            {
                continue;
            }

            organisms.Add(CreatePrediction(
                body,
                prediction,
                exobiology,
                highlightRegionalFirsts,
                discoveryContext,
                rewardThresholds));
        }

        while (organisms.Count < body.BiologicalSignalCount)
        {
            organisms.Add(BiologyOrganismRowViewModel.Unknown(
                organisms.Count + 1,
                rewardThresholds));
        }

        var rewardEstimate = CreateRewardEstimate(body, predictionSet);
        var geoCount = hideGeoCount ? 0 : body.GeologicalSignalCount;
        var geoSignals = hideGeoCount
            ? []
            : body.AnalyzedGeologicalSignals;

        return new BiologySurveyViewModel(
            BiologySurveyMode.Body,
            body.BodyId,
            $"{body.Name} biology",
            body.BiologicalSignalCount == 1
                ? "1 biological signal"
                : $"{body.BiologicalSignalCount:N0} biological signals",
            [],
            organisms,
            rewardEstimate.HasPredictedReward
                ? FormatEstimatedReward(
                    rewardEstimate.MinimumReward,
                    rewardEstimate.MaximumReward,
                    rewardEstimate.HasUnknownReward)
                : FormatKnownReward(
                    rewardEstimate.KnownReward,
                    rewardEstimate.HasUnknownReward),
            body.IsFirstFootfall && rewardEstimate.MaximumReward > 0
                ? rewardEstimate.HasPredictedReward
                    ? "First-footfall estimate: " + FormatRewardRange(
                        rewardEstimate.MinimumReward * 5,
                        rewardEstimate.MaximumReward * 5,
                        rewardEstimate.HasUnknownReward)
                    : "First-footfall value: "
                        + FormatCredits(rewardEstimate.KnownReward * 5)
                : string.Empty,
            exobiology.CountRadicoidaUnica,
            body.Organisms.Count == 0 && !body.IsDssComplete,
            predictionSet.Status,
            geoCount,
            geoSignals);
    }

    private static BiologyOrganismRowViewModel CreateOrganism(
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot organism,
        ExobiologySnapshot exobiology,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        BiologyDiscoveryContext discoveryContext,
        BiologyRewardThresholds rewardThresholds)
    {
        var displayName = organism.VariantLocalized
            ?? organism.SpeciesLocalized
            ?? organism.GenusLocalized
            ?? FormatJournalName(organism.Variant
                ?? organism.Species
                ?? organism.Genus);
        var genusName = organism.GenusLocalized
            ?? FormatJournalName(organism.Genus);
        var activeSample = exobiology.ScanOne is { } scan
            && !organism.IsAnalyzed
            && string.Equals(scan.Body, body.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(scan.Genus, organism.Genus, StringComparison.Ordinal);
        var firstDiscovery = ClassifyOrganismFirst(
            body,
            organism,
            discoveryContext);

        return new BiologyOrganismRowViewModel(
            displayName,
            genusName,
            ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                organism.GenusLocalized ?? organism.Genus),
            organism.Reward ?? 0,
            organism.Reward is not null,
            organism.IsAnalyzed,
            firstDiscovery.IsCommanderFirst,
            firstDiscovery.IsRegionalFirst,
            firstDiscovery.IsGlobalRegionalFirst,
            firstDiscovery.IsHighlighted(highlightRegionalFirsts),
            activeSample,
            false,
            organism.Variant is null,
            false,
            dimAnalyzedOrganisms && organism.IsAnalyzed,
            rewardThresholds.BucketOneMillions,
            rewardThresholds.BucketTwoMillions,
            rewardThresholds.BucketThreeMillions);
    }

    private static BiologyOrganismRowViewModel CreatePrediction(
        SystemScanBodySnapshot body,
        BiologyPredictionPresentation prediction,
        ExobiologySnapshot exobiology,
        bool highlightRegionalFirsts,
        BiologyDiscoveryContext discoveryContext,
        BiologyRewardThresholds rewardThresholds)
    {
        var activeSample = exobiology.ScanOne is { } scan
            && string.Equals(scan.Body, body.Name, StringComparison.OrdinalIgnoreCase)
            && body.Organisms.Any(organism => string.Equals(
                    organism.GenusLocalized,
                    prediction.Prediction.Genus,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    organism.Genus,
                    scan.Genus,
                    StringComparison.Ordinal));
        var reward = prediction.Reference?.Reward ?? 0;
        var firstDiscovery = ClassifyPredictionFirst(
            prediction.Reference,
            discoveryContext);

        return new BiologyOrganismRowViewModel(
            prediction.Prediction.Name,
            prediction.Prediction.Genus,
            ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                prediction.Prediction.Genus),
            reward,
            reward > 0,
            false,
            firstDiscovery.IsCommanderFirst,
            firstDiscovery.IsRegionalFirst,
            firstDiscovery.IsGlobalRegionalFirst,
            firstDiscovery.IsHighlighted(highlightRegionalFirsts),
            activeSample,
            true,
            false,
            false,
            false,
            rewardThresholds.BucketOneMillions,
            rewardThresholds.BucketTwoMillions,
            rewardThresholds.BucketThreeMillions);
    }

    private static BiologyPredictionSet CreatePredictions(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        bool disablePredictions,
        BiologyPredictionEvaluator predictionEvaluator,
        ExobiologyReferenceCatalog referenceCatalog)
    {
        if (disablePredictions)
        {
            return BiologyPredictionSet.NoPredictions;
        }

        var inputs = BiologyPredictionContextBuilder.Build(
            snapshot,
            body.BodyId);
        if (inputs is null)
        {
            return new BiologyPredictionSet(
                [],
                "Predictions need complete body and parent-star scans.",
                false);
        }

        var result = predictionEvaluator.Evaluate(
            inputs.Context,
            inputs.Knowledge);
        if (!result.HasCompleteContext)
        {
            return new BiologyPredictionSet(
                [],
                "Predictions waiting for: "
                    + string.Join(", ", result.MissingProperties),
                false);
        }

        return new BiologyPredictionSet(
            result.PredictionDetails
                .Select(prediction => new BiologyPredictionPresentation(
                    prediction,
                    referenceCatalog.FindByDisplayName(
                        prediction.Name)))
                .ToArray(),
            string.Empty,
            true);
    }

    private static BiologyRewardEstimate CreateRewardEstimate(
        SystemScanBodySnapshot body,
        BiologyPredictionSet predictionSet)
    {
        var knownReward = body.Organisms.Sum(
            organism => organism.Reward ?? 0);
        var remainingSignals = Math.Max(
            0,
            body.BiologicalSignalCount
                - body.Organisms.Count(organism => organism.Species is not null));
        if (remainingSignals == 0)
        {
            return new BiologyRewardEstimate(
                knownReward,
                knownReward,
                knownReward,
                false,
                false);
        }

        var rewardGroups = predictionSet.Predictions
            .Where(prediction => prediction.Reference?.Reward > 0)
            .GroupBy(
                prediction => prediction.Prediction.Genus,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Minimum = group.Min(prediction => prediction.Reference!.Reward),
                Maximum = group.Max(prediction => prediction.Reference!.Reward),
            })
            .ToArray();
        var minimumAdd = rewardGroups
            .OrderBy(group => group.Minimum)
            .Take(remainingSignals)
            .Sum(group => group.Minimum);
        var maximumAdd = rewardGroups
            .OrderByDescending(group => group.Maximum)
            .Take(remainingSignals)
            .Sum(group => group.Maximum);
        var predictedCount = Math.Min(remainingSignals, rewardGroups.Length);

        return new BiologyRewardEstimate(
            knownReward,
            knownReward + minimumAdd,
            knownReward + maximumAdd,
            predictedCount > 0,
            !predictionSet.IsComplete || predictedCount < remainingSignals);
    }

    private static BiologySignalRewardBandViewModel[]
        CreateSystemRewardBands(
            SystemScanBodySnapshot body,
            BiologyPredictionSet predictionSet,
            bool highlightRegionalFirsts,
            BiologyDiscoveryContext discoveryContext,
            BiologyRewardThresholds rewardThresholds)
    {
        var predictionsByGenus = predictionSet.Predictions
            .Where(prediction => prediction.Reference?.Reward > 0)
            .GroupBy(
                prediction => prediction.Prediction.Genus,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new BiologySignalRewardRange(
                    group.Min(prediction => prediction.Reference!.Reward),
                    group.Max(prediction => prediction.Reference!.Reward),
                    group.Any(prediction => ClassifyPredictionFirst(
                            prediction.Reference,
                            discoveryContext)
                        .IsHighlighted(highlightRegionalFirsts))),
                StringComparer.OrdinalIgnoreCase);
        var consumedPredictionGenera = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var bands = new List<BiologySignalRewardBandViewModel>(
            body.BiologicalSignalCount);

        // Preserve the legacy sequence: known/DSS-resolved genera first,
        // remaining predictions second, and unidentified signals last.
        foreach (var organism in body.Organisms)
        {
            var genus = organism.GenusLocalized
                ?? FormatJournalName(organism.Genus);
            var isHighlighted = ClassifyOrganismFirst(
                    body,
                    organism,
                    discoveryContext)
                .IsHighlighted(highlightRegionalFirsts);
            if (organism.Reward is { } reward && reward > 0)
            {
                bands.Add(BiologySignalRewardBandViewModel.Known(
                    reward,
                    isHighlighted,
                    organism.IsAnalyzed,
                    rewardThresholds));
                consumedPredictionGenera.Add(genus);
                continue;
            }

            if (predictionsByGenus.TryGetValue(genus, out var prediction))
            {
                bands.Add(BiologySignalRewardBandViewModel.Predicted(
                    prediction.Minimum,
                    prediction.Maximum,
                    isHighlighted || prediction.IsHighlighted,
                    rewardThresholds));
                consumedPredictionGenera.Add(genus);
                continue;
            }

            bands.Add(BiologySignalRewardBandViewModel.Unknown(
                rewardThresholds));
        }

        foreach (var prediction in predictionsByGenus)
        {
            if (bands.Count >= body.BiologicalSignalCount
                || consumedPredictionGenera.Contains(prediction.Key))
            {
                continue;
            }

            bands.Add(BiologySignalRewardBandViewModel.Predicted(
                prediction.Value.Minimum,
                prediction.Value.Maximum,
                prediction.Value.IsHighlighted,
                rewardThresholds));
        }

        while (bands.Count < body.BiologicalSignalCount)
        {
            bands.Add(BiologySignalRewardBandViewModel.Unknown(
                rewardThresholds));
        }

        return bands.Take(body.BiologicalSignalCount).ToArray();
    }

    private static SystemScanBodySnapshot? ResolveBody(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies,
        bool drawBodyBiosOnlyWhenNear,
        bool allowRetainedCurrentBody,
        bool forceSystemOverview)
    {
        if (forceSystemOverview
            || status?.GuiFocus is GuiFocus.ExternalPanel
            or GuiFocus.SystemMap
            or GuiFocus.Orrery)
        {
            return null;
        }

        if (status?.GuiFocus == GuiFocus.Fss)
        {
            return snapshot.LastDetailedBodyId is { } lastBodyId
                ? biologicalBodies.FirstOrDefault(body =>
                    body.BodyId == lastBodyId)
                : null;
        }

        var current = allowRetainedCurrentBody
            ? ResolveCurrentBody(snapshot, status)
            : null;
        var destination = status?.Destination is { } target
            && target.System == snapshot.SystemAddress
                ? biologicalBodies.FirstOrDefault(body =>
                    body.BodyId == target.Body)
                : null;
        if (!drawBodyBiosOnlyWhenNear)
        {
            return destination ?? current;
        }

        return destination is null || destination.BodyId == current?.BodyId
            ? current
            : null;
    }

    private static SystemScanBodySnapshot? ResolveCurrentBody(
        SystemScanSnapshot snapshot,
        EliteStatus? status)
    {
        var current = !string.IsNullOrWhiteSpace(status?.BodyName)
            ? snapshot.Bodies.FirstOrDefault(body => string.Equals(
                body.Name,
                status.BodyName,
                StringComparison.OrdinalIgnoreCase))
            : null;
        return current ?? (snapshot.CurrentBodyId is { } bodyId
            ? snapshot.Bodies.FirstOrDefault(body => body.BodyId == bodyId)
            : null);
    }

    private static string FormatKnownReward(long reward, bool hasUnknown)
    {
        if (reward <= 0)
        {
            return hasUnknown ? "Reward pending identification" : string.Empty;
        }

        return hasUnknown
            ? $"Known reward: {FormatCredits(reward)}"
            : $"Total reward: {FormatCredits(reward)}";
    }

    private static string FormatEstimatedReward(
        long minimum,
        long maximum,
        bool hasUnknown)
    {
        return "Estimated reward: "
            + FormatRewardRange(minimum, maximum, hasUnknown);
    }

    private static string FormatRewardRange(
        long minimum,
        long maximum,
        bool hasUnknown)
    {
        var range = minimum == maximum
            ? FormatCredits(minimum)
            : $"{FormatCredits(minimum)} – {FormatCredits(maximum)}";
        return hasUnknown ? range + " + pending" : range;
    }

    private static string FormatCredits(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:N2} M CR",
            >= 1_000 => $"{value / 1_000d:N1} K CR",
            _ => $"{value:N0} CR",
        };
    }

    private static string FormatJournalName(string value)
    {
        var normalized = value
            .Replace("$Codex_Ent_", string.Empty, StringComparison.Ordinal)
            .Replace("_Genus_Name;", string.Empty, StringComparison.Ordinal)
            .Replace("_Name;", string.Empty, StringComparison.Ordinal)
            .Replace('_', ' ')
            .Trim('$', ';', ' ');
        return string.IsNullOrWhiteSpace(normalized)
            ? "Unidentified organism"
            : normalized;
    }

    private static BiologyFirstDiscoveryState ClassifyOrganismFirst(
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot organism,
        BiologyDiscoveryContext discoveryContext)
    {
        var globalRegionalFirst = organism.IsRegionalFirst
            || organism.EntryId is { } globalRegionalEntryId
                && discoveryContext.IsGlobalRegionalNew(globalRegionalEntryId);
        var commanderFirst = !globalRegionalFirst
            && organism.EntryId is { } entryId
            && discoveryContext.IsPersonalFirst(
                entryId,
                body.BodyId);
        var regionalFirst = !globalRegionalFirst
            && organism.EntryId is { } regionalEntryId
            && !organism.IsAnalyzed
            && !commanderFirst
            && discoveryContext.IsRegionalNew(regionalEntryId);
        return new BiologyFirstDiscoveryState(
            commanderFirst,
            regionalFirst,
            globalRegionalFirst);
    }

    private static BiologyFirstDiscoveryState ClassifyPredictionFirst(
        ExobiologyReference? reference,
        BiologyDiscoveryContext discoveryContext)
    {
        var globalRegionalFirst = reference is not null
            && discoveryContext.IsGlobalRegionalNew(reference.EntryId);
        var commanderFirst = !globalRegionalFirst
            && reference is not null
            && discoveryContext.IsCommanderNew(reference.EntryId);
        var regionalFirst = !globalRegionalFirst
            && reference is not null
            && !commanderFirst
            && discoveryContext.IsRegionalNew(reference.EntryId);
        return new BiologyFirstDiscoveryState(
            commanderFirst,
            regionalFirst,
            globalRegionalFirst);
    }

    private static readonly Lazy<BiologyPredictionEvaluator>
        DefaultPredictionEvaluator =
        new(() => new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.LoadEmbedded()));

    private static readonly Lazy<ExobiologyReferenceCatalog>
        DefaultBioReferenceCatalog =
        new(ExobiologyReferenceCatalog.LoadEmbedded);

    private sealed record BiologyPredictionPresentation(
        BiologyPrediction Prediction,
        ExobiologyReference? Reference);

    private sealed record BiologyPredictionSet(
        IReadOnlyList<BiologyPredictionPresentation> Predictions,
        string Status,
        bool IsComplete)
    {
        public static BiologyPredictionSet NoPredictions { get; } = new(
            [],
            string.Empty,
            false);
    }

    private sealed record BiologyRewardEstimate(
        long KnownReward,
        long MinimumReward,
        long MaximumReward,
        bool HasPredictedReward,
        bool HasUnknownReward);

    private sealed record BiologySignalRewardRange(
        long Minimum,
        long Maximum,
        bool IsHighlighted);

    private readonly record struct BiologyFirstDiscoveryState(
        bool IsCommanderFirst,
        bool IsRegionalFirst,
        bool IsGlobalRegionalFirst)
    {
        public bool IsHighlighted(bool highlightRegionalFirsts) =>
            IsGlobalRegionalFirst
            || IsCommanderFirst
            || highlightRegionalFirsts && IsRegionalFirst;
    }
}

public enum BiologySurveyMode
{
    System,
    Body,
}

public sealed record BiologyBodyRowViewModel(
    int BodyId,
    string Name,
    int AnalyzedSignalCount,
    int SignalCount,
    long KnownReward,
    long MinimumReward,
    long MaximumReward,
    bool HasPredictedReward,
    bool HasUnknownReward,
    bool IsDestination,
    bool IsCurrentBody,
    bool HasCanonnSignals,
    IReadOnlyList<BiologySignalRewardBandViewModel> RewardBands,
    double RewardBucketOneMillions = 3,
    double RewardBucketTwoMillions = 7,
    double RewardBucketThreeMillions = 12)
{
    public string ProgressText => $"{AnalyzedSignalCount:N0}/{SignalCount:N0}";

    public bool IsComplete => SignalCount > 0
        && AnalyzedSignalCount >= SignalCount;

    public string RewardText => HasPredictedReward
        ? MinimumReward == MaximumReward
            ? $"~{MinimumReward / 1_000_000d:N2} M CR"
            : $"{MinimumReward / 1_000_000d:N2}–{MaximumReward / 1_000_000d:N2} M CR"
        : KnownReward <= 0
        ? ""
        : HasUnknownReward
            ? $"{KnownReward / 1_000_000d:N2} M+ CR"
            : $"{KnownReward / 1_000_000d:N2} M CR";

    public bool HasReward => KnownReward > 0 || HasPredictedReward;

    public long RewardBandMinimum => HasPredictedReward
        ? MinimumReward
        : KnownReward;

    public long RewardBandMaximum => HasPredictedReward
        ? MaximumReward
        : KnownReward;
}

public sealed record BiologySignalRewardBandViewModel(
    long MinimumReward,
    long MaximumReward,
    bool IsPrediction,
    bool IsHighlighted,
    bool ShouldDim,
    double RewardBucketOneMillions,
    double RewardBucketTwoMillions,
    double RewardBucketThreeMillions)
{
    public double Opacity => ShouldDim ? 0.48 : 1;

    public static BiologySignalRewardBandViewModel Known(
        long reward,
        bool isHighlighted,
        bool shouldDim,
        BiologyRewardThresholds thresholds) => new(
            reward,
            reward,
            false,
            isHighlighted,
            shouldDim,
            thresholds.BucketOneMillions,
            thresholds.BucketTwoMillions,
            thresholds.BucketThreeMillions);

    public static BiologySignalRewardBandViewModel Predicted(
        long minimumReward,
        long maximumReward,
        bool isHighlighted,
        BiologyRewardThresholds thresholds) => new(
            minimumReward,
            maximumReward,
            true,
            isHighlighted,
            false,
            thresholds.BucketOneMillions,
            thresholds.BucketTwoMillions,
            thresholds.BucketThreeMillions);

    public static BiologySignalRewardBandViewModel Unknown(
        BiologyRewardThresholds thresholds) => new(
            0,
            0,
            false,
            false,
            false,
            thresholds.BucketOneMillions,
            thresholds.BucketTwoMillions,
            thresholds.BucketThreeMillions);
}

public sealed record BiologyOrganismRowViewModel(
    string DisplayName,
    string GenusName,
    int SampleDistanceMeters,
    long Reward,
    bool HasReward,
    bool IsAnalyzed,
    bool IsCommanderFirst,
    bool IsRegionalFirst,
    bool IsGlobalRegionalFirst,
    bool IsHighlightedFirst,
    bool IsCurrentSample,
    bool IsPrediction,
    bool IsGenusIdentified,
    bool IsUnknown,
    bool ShouldDim,
    double RewardBucketOneMillions = 3,
    double RewardBucketTwoMillions = 7,
    double RewardBucketThreeMillions = 12)
{
    public double RowOpacity => ShouldDim ? 0.48 : 1;

    public bool HasSampleDistance => SampleDistanceMeters > 0;

    public string SampleDistanceText => HasSampleDistance
        ? $"{SampleDistanceMeters:N0} m sample separation"
        : string.Empty;

    public string RewardText => HasReward
        ? Reward >= 1_000_000
            ? $"{Reward / 1_000_000d:N2} M CR"
            : $"{Reward:N0} CR"
        : IsPrediction
            ? "Prediction pending"
            : "Unidentified";

    public static BiologyOrganismRowViewModel Unknown(
        int index,
        BiologyRewardThresholds? rewardThresholds = null)
    {
        var thresholds = rewardThresholds ?? BiologyRewardThresholds.Default;
        return new BiologyOrganismRowViewModel(
            $"Unidentified biological signal {index:N0}",
            "Genus unknown",
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            false,
            thresholds.BucketOneMillions,
            thresholds.BucketTwoMillions,
            thresholds.BucketThreeMillions);
    }
}

public sealed record BiologyDiscoveryContext(
    long SystemAddress,
    CommanderCodexData? Global,
    CommanderCodexData? Regional,
    int? RegionId,
    RegionalCodexCandidateCatalog GlobalRegionalCandidates)
{
    public static BiologyDiscoveryContext Unavailable { get; } = new(
        0,
        null,
        null,
        null,
        RegionalCodexCandidateCatalog.Empty);

    public bool IsCommanderNew(long entryId) => Global is not null
        && !Global.IsDiscovered(entryId);

    public bool IsPersonalFirst(long entryId, int bodyId) => Global is not null
        && Global.IsPersonalFirst(entryId, SystemAddress, bodyId);

    public bool IsRegionalNew(long entryId) => Regional is not null
        && !Regional.IsDiscovered(entryId);

    public bool IsGlobalRegionalNew(long entryId) =>
        GlobalRegionalCandidates.IsCandidate(RegionId, entryId);
}
