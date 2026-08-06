using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BiologySurveyViewModel
{
    public BiologySurveyMode Mode { get; init; }

    public int? SelectedBodyId { get; init; }

    public string Heading { get; init; } = string.Empty;

    public string ProgressText { get; init; } = string.Empty;

    public IReadOnlyList<BiologyBodyRowViewModel> Bodies { get; init; } = [];

    public IReadOnlyList<BiologyOrganismRowViewModel> Organisms { get; init; } = [];

    public string RewardSummary { get; init; } = string.Empty;

    public string FirstFootfallRewardSummary { get; init; } = string.Empty;

    public int RadicoidaUnicaCount { get; init; }

    public bool RequiresDss { get; init; }

    public string PredictionStatus { get; init; } = string.Empty;

    public int GeologicalSignalCount { get; init; }

    public IReadOnlyList<string> GeologicalSignals { get; init; } = [];

    public static BiologySurveyViewModel Empty { get; } = new();

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
        BiologySurveyCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        ArgumentNullException.ThrowIfNull(options);
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
            options.DrawBodyBiosOnlyWhenNear,
            options.AllowRetainedCurrentBody,
            options.ForceSystemOverview);
        return body is null
            ? CreateSystem(
                snapshot,
                status,
                biologicalBodies,
                new BiologySurveySystemBuildOptions
                {
                    HighlightRegionalFirsts = options.HighlightRegionalFirsts,
                    DiscoveryContext = options.DiscoveryContext
                        ?? BiologyDiscoveryContext.Unavailable,
                    DisablePredictions = options.DisablePredictions,
                    RadicoidaUnicaCount = exobiology.CountRadicoidaUnica,
                    RewardThresholds = options.RewardThresholds
                        ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator
                        ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = options.ReferenceCatalog
                        ?? DefaultBioReferenceCatalog.Value,
                    CanonnBiologyBodyIds = options.CanonnBiologyBodyIds,
                })
            : CreateBody(
                snapshot,
                body,
                exobiology,
                new BiologySurveyBodyBuildOptions
                {
                    HighlightRegionalFirsts = options.HighlightRegionalFirsts,
                    DimAnalyzedOrganisms = options.DimAnalyzedOrganisms,
                    HideGeoCount = options.HideGeoCount,
                    DisablePredictions = options.DisablePredictions,
                    DiscoveryContext = options.DiscoveryContext
                        ?? BiologyDiscoveryContext.Unavailable,
                    RewardThresholds = options.RewardThresholds
                        ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator
                        ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = options.ReferenceCatalog
                        ?? DefaultBioReferenceCatalog.Value,
                });
    }

    public static BiologySurveyViewModel? CreateSystemOverview(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        BiologySurveySystemOverviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
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
                new BiologySurveySystemBuildOptions
                {
                    HighlightRegionalFirsts = options.HighlightRegionalFirsts,
                    DiscoveryContext = options.DiscoveryContext
                        ?? BiologyDiscoveryContext.Unavailable,
                    DisablePredictions = options.DisablePredictions,
                    RadicoidaUnicaCount = options.RadicoidaUnicaCount,
                    RewardThresholds = options.RewardThresholds
                        ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator
                        ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = options.ReferenceCatalog
                        ?? DefaultBioReferenceCatalog.Value,
                    CanonnBiologyBodyIds = null,
                });
    }

    public static BiologySurveyViewModel? CreateBodyDetail(
        SystemScanSnapshot snapshot,
        int bodyId,
        ExobiologySnapshot exobiology,
        BiologySurveyBodyDetailOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        ArgumentNullException.ThrowIfNull(options);
        var body = snapshot.Bodies.FirstOrDefault(candidate =>
            candidate.BodyId == bodyId
            && candidate.BiologicalSignalCount > 0);
        return snapshot.SystemAddress is null || body is null
            ? null
            : CreateBody(
                snapshot,
                body,
                exobiology,
                new BiologySurveyBodyBuildOptions
                {
                    HighlightRegionalFirsts = options.HighlightRegionalFirsts,
                    DimAnalyzedOrganisms = options.DimAnalyzedOrganisms,
                    HideGeoCount = options.HideGeoCount,
                    DisablePredictions = options.DisablePredictions,
                    DiscoveryContext = options.DiscoveryContext
                        ?? BiologyDiscoveryContext.Unavailable,
                    RewardThresholds = options.RewardThresholds
                        ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator
                        ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = options.ReferenceCatalog
                        ?? DefaultBioReferenceCatalog.Value,
                });
    }

    public static IReadOnlyList<BiologySignalRewardBandViewModel>
        CreateRewardBandsForBody(
            SystemScanSnapshot snapshot,
            SystemScanBodySnapshot body,
            BiologySurveyRewardBandOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(options);
        var thresholds = options.RewardThresholds ?? BiologyRewardThresholds.Default;
        var predictions = CreatePredictions(
            snapshot,
            body,
            options.DisablePredictions,
            options.PredictionEvaluator ?? DefaultPredictionEvaluator.Value,
            options.ReferenceCatalog ?? DefaultBioReferenceCatalog.Value);
        return CreateSystemRewardBands(
            body,
            predictions,
            options.HighlightRegionalFirsts,
            options.DiscoveryContext ?? BiologyDiscoveryContext.Unavailable,
            thresholds);
    }

    private static BiologySurveyViewModel CreateSystem(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies,
        BiologySurveySystemBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
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
                    options.DisablePredictions,
                    options.PredictionEvaluator,
                    options.ReferenceCatalog);
                var estimate = CreateRewardEstimate(body, predictions);
                var rewardBands = CreateSystemRewardBands(
                    body,
                    predictions,
                    options.HighlightRegionalFirsts,
                    options.DiscoveryContext,
                    options.RewardThresholds);
                var row = new BiologyBodyRowViewModel
    {
        BodyId = body.BodyId,
        Name = body.ShortName,
        AnalyzedSignalCount = body.AnalyzedBiologicalSignalCount,
        SignalCount = body.BiologicalSignalCount,
        KnownReward = estimate.KnownReward,
        MinimumReward = estimate.MinimumReward,
        MaximumReward = estimate.MaximumReward,
        HasPredictedReward = estimate.HasPredictedReward,
        HasUnknownReward = estimate.HasUnknownReward,
        IsDestination = body.BodyId == destinationBodyId,
        IsCurrentBody = body.BodyId == currentBodyId,
        HasCanonnSignals = options.CanonnBiologyBodyIds?.Contains(body.BodyId) == true,
        RewardBands = rewardBands,
        RewardBucketOneMillions = options.RewardThresholds.BucketOneMillions,
        RewardBucketTwoMillions = options.RewardThresholds.BucketTwoMillions,
        RewardBucketThreeMillions = options.RewardThresholds.BucketThreeMillions
    };
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

        return new BiologySurveyViewModel
    {
        Mode = BiologySurveyMode.System,
        SelectedBodyId = null,
        Heading = snapshot.SystemName ?? "Current system",
        ProgressText = $"{analyzed:N0} of {total:N0} biological signals analyzed",
        Bodies = rows,
        Organisms = [],
        RewardSummary = hasPredictedReward
                ? FormatEstimatedReward(
                    minimumSystemReward,
                    maximumSystemReward,
                    hasUnknownReward)
                : FormatKnownReward(knownSystemReward, hasUnknownReward),
        FirstFootfallRewardSummary = string.Empty,
        RadicoidaUnicaCount = options.RadicoidaUnicaCount,
        RequiresDss = false,
        PredictionStatus = string.Empty,
        GeologicalSignalCount = 0,
        GeologicalSignals = []
    };
    }

    private static BiologySurveyViewModel CreateBody(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        ExobiologySnapshot exobiology,
        BiologySurveyBodyBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var highlightRegionalFirsts = options.HighlightRegionalFirsts;
        var dimAnalyzedOrganisms = options.DimAnalyzedOrganisms;
        var hideGeoCount = options.HideGeoCount;
        var disablePredictions = options.DisablePredictions;
        var discoveryContext = options.DiscoveryContext;
        var rewardThresholds = options.RewardThresholds;
        var predictionEvaluator = options.PredictionEvaluator;
        var referenceCatalog = options.ReferenceCatalog;
        var predictionSet = CreatePredictions(
            snapshot,
            body,
            disablePredictions,
            predictionEvaluator,
            referenceCatalog);
        var organisms = BuildBodyOrganismRows(
            body,
            exobiology,
            predictionSet,
            highlightRegionalFirsts,
            dimAnalyzedOrganisms,
            discoveryContext,
            rewardThresholds);
        var rewardEstimate = CreateRewardEstimate(body, predictionSet);
        var geoCount = hideGeoCount ? 0 : body.GeologicalSignalCount;
        var geoSignals = hideGeoCount
            ? Array.Empty<string>()
            : body.AnalyzedGeologicalSignals;

        return new BiologySurveyViewModel
        {
            Mode = BiologySurveyMode.Body,
            SelectedBodyId = body.BodyId,
            Heading = $"{body.Name} biology",
            ProgressText = FormatBodyProgressText(body.BiologicalSignalCount),
            Bodies = [],
            Organisms = organisms,
            RewardSummary = FormatBodyRewardSummary(rewardEstimate),
            FirstFootfallRewardSummary = FormatFirstFootfallRewardSummary(
                body,
                rewardEstimate),
            RadicoidaUnicaCount = exobiology.CountRadicoidaUnica,
            RequiresDss = body.Organisms.Count == 0 && !body.IsDssComplete,
            PredictionStatus = predictionSet.Status,
            GeologicalSignalCount = geoCount,
            GeologicalSignals = geoSignals
        };
    }

    private static string FormatBodyProgressText(int biologicalSignalCount)
    {
        return biologicalSignalCount == 1
            ? "1 biological signal"
            : $"{biologicalSignalCount:N0} biological signals";
    }

    private static string FormatBodyRewardSummary(
        BiologyRewardEstimate rewardEstimate)
    {
        return rewardEstimate.HasPredictedReward
            ? FormatEstimatedReward(
                rewardEstimate.MinimumReward,
                rewardEstimate.MaximumReward,
                rewardEstimate.HasUnknownReward)
            : FormatKnownReward(
                rewardEstimate.KnownReward,
                rewardEstimate.HasUnknownReward);
    }

    private static string FormatFirstFootfallRewardSummary(
        SystemScanBodySnapshot body,
        BiologyRewardEstimate rewardEstimate)
    {
        if (!body.IsFirstFootfall || rewardEstimate.MaximumReward <= 0)
        {
            return string.Empty;
        }

        if (rewardEstimate.HasPredictedReward)
        {
            return "First-footfall estimate: " + FormatRewardRange(
                rewardEstimate.MinimumReward * 5,
                rewardEstimate.MaximumReward * 5,
                rewardEstimate.HasUnknownReward);
        }

        return "First-footfall value: "
            + FormatCredits(rewardEstimate.KnownReward * 5);
    }

    private sealed class BodyOrganismRowBuildContext
    {
        public required SystemScanBodySnapshot Body { get; init; }

        public required ExobiologySnapshot Exobiology { get; init; }

        public required BiologyPredictionSet PredictionSet { get; init; }

        public bool HighlightRegionalFirsts { get; init; }

        public bool DimAnalyzedOrganisms { get; init; }

        public required BiologyDiscoveryContext DiscoveryContext { get; init; }

        public required BiologyRewardThresholds RewardThresholds { get; init; }
    }

    private static List<BiologyOrganismRowViewModel> BuildBodyOrganismRows(
        SystemScanBodySnapshot body,
        ExobiologySnapshot exobiology,
        BiologyPredictionSet predictionSet,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        BiologyDiscoveryContext discoveryContext,
        BiologyRewardThresholds rewardThresholds)
    {
        var context = new BodyOrganismRowBuildContext
        {
            Body = body,
            Exobiology = exobiology,
            PredictionSet = predictionSet,
            HighlightRegionalFirsts = highlightRegionalFirsts,
            DimAnalyzedOrganisms = dimAnalyzedOrganisms,
            DiscoveryContext = discoveryContext,
            RewardThresholds = rewardThresholds,
        };
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
            AddKnownOrganismRows(
                organisms,
                consumedPredictions,
                organism,
                predictionsByGenus,
                context);
        }

        AddRemainingPredictionRows(organisms, consumedPredictions, context);

        while (organisms.Count < body.BiologicalSignalCount)
        {
            organisms.Add(BiologyOrganismRowViewModel.Unknown(
                organisms.Count + 1,
                rewardThresholds));
        }

        return organisms;
    }

    private static void AddKnownOrganismRows(
        List<BiologyOrganismRowViewModel> organisms,
        HashSet<string> consumedPredictions,
        SystemOrganismSnapshot organism,
        IReadOnlyDictionary<string, BiologyPredictionPresentation[]> predictionsByGenus,
        BodyOrganismRowBuildContext context)
    {
        var genusName = organism.GenusLocalized
            ?? FormatJournalName(organism.Genus);
        if (organism.Variant is null
            && predictionsByGenus.TryGetValue(genusName, out var predictions))
        {
            foreach (var prediction in predictions)
            {
                organisms.Add(CreatePrediction(prediction, context));
                consumedPredictions.Add(prediction.Prediction.Name);
            }

            return;
        }

        organisms.Add(CreateOrganism(organism, context));
    }

    private static void AddRemainingPredictionRows(
        List<BiologyOrganismRowViewModel> organisms,
        HashSet<string> consumedPredictions,
        BodyOrganismRowBuildContext context)
    {
        foreach (var prediction in context.PredictionSet.Predictions.Where(
                     prediction => !consumedPredictions.Contains(
                         prediction.Prediction.Name)))
        {
            if (context.Body.Organisms.Any(organism => prediction.Reference is not null
                    && (organism.Variant == prediction.Reference.VariantName
                        || organism.Species == prediction.Reference.SpeciesName)))
            {
                continue;
            }

            organisms.Add(CreatePrediction(prediction, context));
        }
    }

    private static BiologyOrganismRowViewModel CreateOrganism(
        SystemOrganismSnapshot organism,
        BodyOrganismRowBuildContext context)
    {
        var body = context.Body;
        var exobiology = context.Exobiology;
        var highlightRegionalFirsts = context.HighlightRegionalFirsts;
        var dimAnalyzedOrganisms = context.DimAnalyzedOrganisms;
        var discoveryContext = context.DiscoveryContext;
        var rewardThresholds = context.RewardThresholds;
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

        return new BiologyOrganismRowViewModel
        {
            DisplayName = displayName,
            GenusName = genusName,
            SampleDistanceMeters = ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                organism.GenusLocalized ?? organism.Genus),
            Reward = organism.Reward ?? 0,
            HasReward = organism.Reward is not null,
            IsAnalyzed = organism.IsAnalyzed,
            IsCommanderFirst = firstDiscovery.IsCommanderFirst,
            IsRegionalFirst = firstDiscovery.IsRegionalFirst,
            IsGlobalRegionalFirst = firstDiscovery.IsGlobalRegionalFirst,
            IsHighlightedFirst = firstDiscovery.IsHighlighted(highlightRegionalFirsts),
            IsCurrentSample = activeSample,
            IsPrediction = false,
            IsGenusIdentified = organism.Variant is null,
            IsUnknown = false,
            ShouldDim = dimAnalyzedOrganisms && organism.IsAnalyzed,
            RewardBucketOneMillions = rewardThresholds.BucketOneMillions,
            RewardBucketTwoMillions = rewardThresholds.BucketTwoMillions,
            RewardBucketThreeMillions = rewardThresholds.BucketThreeMillions,
        };
    }

    private static BiologyOrganismRowViewModel CreatePrediction(
        BiologyPredictionPresentation prediction,
        BodyOrganismRowBuildContext context)
    {
        var body = context.Body;
        var exobiology = context.Exobiology;
        var highlightRegionalFirsts = context.HighlightRegionalFirsts;
        var discoveryContext = context.DiscoveryContext;
        var rewardThresholds = context.RewardThresholds;
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

        return new BiologyOrganismRowViewModel
        {
            DisplayName = prediction.Prediction.Name,
            GenusName = prediction.Prediction.Genus,
            SampleDistanceMeters = ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                prediction.Prediction.Genus),
            Reward = reward,
            HasReward = reward > 0,
            IsAnalyzed = false,
            IsCommanderFirst = firstDiscovery.IsCommanderFirst,
            IsRegionalFirst = firstDiscovery.IsRegionalFirst,
            IsGlobalRegionalFirst = firstDiscovery.IsGlobalRegionalFirst,
            IsHighlightedFirst = firstDiscovery.IsHighlighted(highlightRegionalFirsts),
            IsCurrentSample = activeSample,
            IsPrediction = true,
            IsGenusIdentified = false,
            IsUnknown = false,
            ShouldDim = false,
        RewardBucketOneMillions = rewardThresholds.BucketOneMillions,
        RewardBucketTwoMillions = rewardThresholds.BucketTwoMillions,
        RewardBucketThreeMillions = rewardThresholds.BucketThreeMillions
    };
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

public sealed class BiologyBodyRowViewModel
{
    public int BodyId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int AnalyzedSignalCount { get; init; }

    public int SignalCount { get; init; }

    public long KnownReward { get; init; }

    public long MinimumReward { get; init; }

    public long MaximumReward { get; init; }

    public bool HasPredictedReward { get; init; }

    public bool HasUnknownReward { get; init; }

    public bool IsDestination { get; init; }

    public bool IsCurrentBody { get; init; }

    public bool HasCanonnSignals { get; init; }

    public IReadOnlyList<BiologySignalRewardBandViewModel> RewardBands { get; init; } =
        [];

    public double RewardBucketOneMillions { get; init; } = 3;

    public double RewardBucketTwoMillions { get; init; } = 7;

    public double RewardBucketThreeMillions { get; init; } = 12;

    public string ProgressText => $"{AnalyzedSignalCount:N0}/{SignalCount:N0}";

    public bool IsComplete => SignalCount > 0
        && AnalyzedSignalCount >= SignalCount;

    public string RewardText
    {
        get
        {
            if (HasPredictedReward)
            {
                if (MinimumReward == MaximumReward)
                {
                    return $"~{MinimumReward / 1_000_000d:N2} M CR";
                }

                return $"{MinimumReward / 1_000_000d:N2}–{MaximumReward / 1_000_000d:N2} M CR";
            }

            if (KnownReward <= 0)
            {
                return string.Empty;
            }

            if (HasUnknownReward)
            {
                return $"{KnownReward / 1_000_000d:N2} M+ CR";
            }

            return $"{KnownReward / 1_000_000d:N2} M CR";
        }
    }

    public bool HasReward => KnownReward > 0 || HasPredictedReward;

    public long RewardBandMinimum => HasPredictedReward
        ? MinimumReward
        : KnownReward;

    public long RewardBandMaximum => HasPredictedReward
        ? MaximumReward
        : KnownReward;
}

public sealed class BiologySignalRewardBandViewModel
{
    public long MinimumReward { get; init; }

    public long MaximumReward { get; init; }

    public bool IsPrediction { get; init; }

    public bool IsHighlighted { get; init; }

    public bool ShouldDim { get; init; }

    public double RewardBucketOneMillions { get; init; }

    public double RewardBucketTwoMillions { get; init; }

    public double RewardBucketThreeMillions { get; init; }

    public double Opacity => ShouldDim ? 0.48 : 1;

    public static BiologySignalRewardBandViewModel Known(
        long reward,
        bool isHighlighted,
        bool shouldDim,
        BiologyRewardThresholds thresholds) => new()
    {
        MinimumReward = reward,
        MaximumReward = reward,
        IsPrediction = false,
        IsHighlighted = isHighlighted,
        ShouldDim = shouldDim,
        RewardBucketOneMillions = thresholds.BucketOneMillions,
        RewardBucketTwoMillions = thresholds.BucketTwoMillions,
        RewardBucketThreeMillions = thresholds.BucketThreeMillions,
    };

    public static BiologySignalRewardBandViewModel Predicted(
        long minimumReward,
        long maximumReward,
        bool isHighlighted,
        BiologyRewardThresholds thresholds) => new()
    {
        MinimumReward = minimumReward,
        MaximumReward = maximumReward,
        IsPrediction = true,
        IsHighlighted = isHighlighted,
        ShouldDim = false,
        RewardBucketOneMillions = thresholds.BucketOneMillions,
        RewardBucketTwoMillions = thresholds.BucketTwoMillions,
        RewardBucketThreeMillions = thresholds.BucketThreeMillions,
    };

    public static BiologySignalRewardBandViewModel Unknown(
        BiologyRewardThresholds thresholds) => new()
    {
        MinimumReward = 0,
        MaximumReward = 0,
        IsPrediction = false,
        IsHighlighted = false,
        ShouldDim = false,
        RewardBucketOneMillions = thresholds.BucketOneMillions,
        RewardBucketTwoMillions = thresholds.BucketTwoMillions,
        RewardBucketThreeMillions = thresholds.BucketThreeMillions,
    };
}

public sealed class BiologyOrganismRowViewModel
{
    public string DisplayName { get; init; } = string.Empty;

    public string GenusName { get; init; } = string.Empty;

    public int SampleDistanceMeters { get; init; }

    public long Reward { get; init; }

    public bool HasReward { get; init; }

    public bool IsAnalyzed { get; init; }

    public bool IsCommanderFirst { get; init; }

    public bool IsRegionalFirst { get; init; }

    public bool IsGlobalRegionalFirst { get; init; }

    public bool IsHighlightedFirst { get; init; }

    public bool IsCurrentSample { get; init; }

    public bool IsPrediction { get; init; }

    public bool IsGenusIdentified { get; init; }

    public bool IsUnknown { get; init; }

    public bool ShouldDim { get; init; }

    public double RewardBucketOneMillions { get; init; } = 3;

    public double RewardBucketTwoMillions { get; init; } = 7;

    public double RewardBucketThreeMillions { get; init; } = 12;

    public double RowOpacity => ShouldDim ? 0.48 : 1;

    public bool HasSampleDistance => SampleDistanceMeters > 0;

    public string SampleDistanceText => HasSampleDistance
        ? $"{SampleDistanceMeters:N0} m sample separation"
        : string.Empty;

    public string RewardText
    {
        get
        {
            if (HasReward)
            {
                if (Reward >= 1_000_000)
                {
                    return $"{Reward / 1_000_000d:N2} M CR";
                }

                return $"{Reward:N0} CR";
            }

            if (IsPrediction)
            {
                return "Prediction pending";
            }

            return "Unidentified";
        }
    }

    public static BiologyOrganismRowViewModel Unknown(
        int index,
        BiologyRewardThresholds? rewardThresholds = null)
    {
        var thresholds = rewardThresholds ?? BiologyRewardThresholds.Default;
        return new BiologyOrganismRowViewModel
        {
            DisplayName = $"Unidentified biological signal {index:N0}",
            GenusName = "Genus unknown",
            SampleDistanceMeters = 0,
            Reward = 0,
            HasReward = false,
            IsAnalyzed = false,
            IsCommanderFirst = false,
            IsRegionalFirst = false,
            IsGlobalRegionalFirst = false,
            IsHighlightedFirst = false,
            IsCurrentSample = false,
            IsPrediction = false,
            IsGenusIdentified = false,
            IsUnknown = true,
            ShouldDim = false,
            RewardBucketOneMillions = thresholds.BucketOneMillions,
            RewardBucketTwoMillions = thresholds.BucketTwoMillions,
            RewardBucketThreeMillions = thresholds.BucketThreeMillions,
        };
    }
}

public sealed class BiologySurveyCreateOptions
{
    public bool DrawBodyBiosOnlyWhenNear { get; init; }
    public bool HighlightRegionalFirsts { get; init; }
    public bool DimAnalyzedOrganisms { get; init; }
    public bool HideGeoCount { get; init; }
    public bool DisablePredictions { get; init; }
    public BiologyDiscoveryContext? DiscoveryContext { get; init; }
    public BiologyRewardThresholds? RewardThresholds { get; init; }
    public BiologyPredictionEvaluator? PredictionEvaluator { get; init; }
    public ExobiologyReferenceCatalog? ReferenceCatalog { get; init; }
    public IReadOnlySet<int>? CanonnBiologyBodyIds { get; init; }
    public bool AllowRetainedCurrentBody { get; init; } = true;
    public bool ForceSystemOverview { get; init; }

    public BiologySurveyCreateOptions()
    {
    }

    public BiologySurveyCreateOptions(
        bool drawBodyBiosOnlyWhenNear,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions,
        BiologyDiscoveryContext? discoveryContext,
        BiologyRewardThresholds? rewardThresholds)
    {
        DrawBodyBiosOnlyWhenNear = drawBodyBiosOnlyWhenNear;
        HighlightRegionalFirsts = highlightRegionalFirsts;
        DimAnalyzedOrganisms = dimAnalyzedOrganisms;
        HideGeoCount = hideGeoCount;
        DisablePredictions = disablePredictions;
        DiscoveryContext = discoveryContext;
        RewardThresholds = rewardThresholds;
    }
}

public sealed class BiologySurveySystemOverviewOptions
{
    public bool DisablePredictions { get; init; }
    public BiologyRewardThresholds? RewardThresholds { get; init; }
    public BiologyPredictionEvaluator? PredictionEvaluator { get; init; }
    public ExobiologyReferenceCatalog? ReferenceCatalog { get; init; }
    public int RadicoidaUnicaCount { get; init; }
    public bool HighlightRegionalFirsts { get; init; }
    public BiologyDiscoveryContext? DiscoveryContext { get; init; }

    public BiologySurveySystemOverviewOptions(bool disablePredictions)
    {
        DisablePredictions = disablePredictions;
    }
}

public sealed class BiologySurveyBodyDetailOptions
{
    public bool HighlightRegionalFirsts { get; init; }
    public bool DimAnalyzedOrganisms { get; init; }
    public bool HideGeoCount { get; init; }
    public bool DisablePredictions { get; init; }
    public BiologyDiscoveryContext? DiscoveryContext { get; init; }
    public BiologyRewardThresholds? RewardThresholds { get; init; }
    public BiologyPredictionEvaluator? PredictionEvaluator { get; init; }
    public ExobiologyReferenceCatalog? ReferenceCatalog { get; init; }

    public BiologySurveyBodyDetailOptions(
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions)
    {
        HighlightRegionalFirsts = highlightRegionalFirsts;
        DimAnalyzedOrganisms = dimAnalyzedOrganisms;
        HideGeoCount = hideGeoCount;
        DisablePredictions = disablePredictions;
    }
}

public sealed class BiologySurveyRewardBandOptions
{
    public bool DisablePredictions { get; init; }
    public BiologyRewardThresholds? RewardThresholds { get; init; }
    public BiologyPredictionEvaluator? PredictionEvaluator { get; init; }
    public ExobiologyReferenceCatalog? ReferenceCatalog { get; init; }
    public bool HighlightRegionalFirsts { get; init; }
    public BiologyDiscoveryContext? DiscoveryContext { get; init; }

    public BiologySurveyRewardBandOptions(bool disablePredictions)
    {
        DisablePredictions = disablePredictions;
    }
}

public sealed class BiologySurveySystemBuildOptions
{
    public bool HighlightRegionalFirsts { get; init; }
    public BiologyDiscoveryContext DiscoveryContext { get; init; } =
        BiologyDiscoveryContext.Unavailable;
    public bool DisablePredictions { get; init; }
    public int RadicoidaUnicaCount { get; init; }
    public BiologyRewardThresholds RewardThresholds { get; init; } =
        BiologyRewardThresholds.Default;
    public BiologyPredictionEvaluator PredictionEvaluator { get; init; } =
        null!;
    public ExobiologyReferenceCatalog ReferenceCatalog { get; init; } =
        null!;
    public IReadOnlySet<int>? CanonnBiologyBodyIds { get; init; }
}

public sealed class BiologySurveyBodyBuildOptions
{
    public bool HighlightRegionalFirsts { get; init; }
    public bool DimAnalyzedOrganisms { get; init; }
    public bool HideGeoCount { get; init; }
    public bool DisablePredictions { get; init; }
    public BiologyDiscoveryContext DiscoveryContext { get; init; } =
        BiologyDiscoveryContext.Unavailable;
    public BiologyRewardThresholds RewardThresholds { get; init; } =
        BiologyRewardThresholds.Default;
    public BiologyPredictionEvaluator PredictionEvaluator { get; init; } =
        null!;
    public ExobiologyReferenceCatalog ReferenceCatalog { get; init; } =
        null!;
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

