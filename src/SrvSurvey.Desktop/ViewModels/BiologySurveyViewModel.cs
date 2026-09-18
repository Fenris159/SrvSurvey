using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BiologySurveyViewModel
{
    private const string PendingRewardSuffix = " + pending";
    private IReadOnlyList<BiologyOrganismGroupViewModel>? organismGroups;

    public BiologySurveyMode Mode { get; init; }

    public string Title { get; init; } = "SYSTEM BIOLOGY";

    public int? SelectedBodyId { get; init; }

    public string Heading { get; init; } = string.Empty;

    public string ProgressText { get; init; } = string.Empty;

    public IReadOnlyList<BiologyBodyRowViewModel> Bodies { get; init; } = [];

    public IReadOnlyList<BiologyOrganismRowViewModel> Organisms { get; init; } = [];

    public IReadOnlyList<BiologyOrganismGroupViewModel> OrganismGroups =>
        organismGroups ??= BiologyOrganismGroupViewModel.Create(Organisms);

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

    public bool HasOrganismGroups => OrganismGroups.Count > 0;

    public bool HasRewardSummary => !string.IsNullOrWhiteSpace(RewardSummary);

    public bool HasFirstFootfallRewardSummary => !string.IsNullOrWhiteSpace(FirstFootfallRewardSummary);

    public bool HasRadicoidaUnicaCount => RadicoidaUnicaCount > 0;

    public string RadicoidaUnicaCountText => $"Radicoida scans: {RadicoidaUnicaCount:N0}";

    public bool HasGeologicalSignals => GeologicalSignalCount > 0;

    public bool HasPredictionStatus => !string.IsNullOrWhiteSpace(PredictionStatus);

    public int UnidentifiedGeologicalSignalCount => Math.Max(0, GeologicalSignalCount - GeologicalSignals.Count);

    public bool HasUnidentifiedGeologicalSignals => UnidentifiedGeologicalSignalCount > 0;

    public string UnidentifiedGeologicalSignalsText =>
        UnidentifiedGeologicalSignalCount == 1
            ? "1 geological signal unidentified"
            : $"{UnidentifiedGeologicalSignalCount:N0} geological signals unidentified";

    public static BiologySurveyViewModel? Create(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        ExobiologySnapshot exobiology,
        BiologySurveyCreateOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        ArgumentNullException.ThrowIfNull(options);
        ExobiologyReferenceCatalog referenceCatalog = options.ReferenceCatalog ?? DefaultBioReferenceCatalog.Value;
        snapshot = AddConfirmedExternalBiology(snapshot, options.ConfirmedExternalBiologySignals, referenceCatalog);
        SystemScanBodySnapshot[] biologicalBodies = snapshot
            .Bodies.Where(body => body.BiologicalSignalCount > 0)
            .OrderBy(body => body.BodyId)
            .ToArray();
        if (snapshot.SystemAddress is null || biologicalBodies.Length == 0)
        {
            return null;
        }

        SystemScanBodySnapshot? body = ResolveBody(
            snapshot,
            status,
            biologicalBodies,
            options.DrawBodyBiosOnlyWhenNear,
            options.AllowRetainedCurrentBody,
            options.ForceSystemOverview
        );
        return body is null
            ? CreateSystem(
                snapshot,
                status,
                biologicalBodies,
                new BiologySurveySystemBuildOptions
                {
                    HighlightRegionalFirsts = options.HighlightRegionalFirsts,
                    DiscoveryContext = options.DiscoveryContext ?? BiologyDiscoveryContext.Unavailable,
                    DisablePredictions = options.DisablePredictions,
                    RadicoidaUnicaCount = exobiology.CountRadicoidaUnica,
                    RewardThresholds = options.RewardThresholds ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = referenceCatalog,
                    CanonnBiologyBodyIds = options.CanonnBiologyBodyIds,
                }
            )
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
                    DiscoveryContext = options.DiscoveryContext ?? BiologyDiscoveryContext.Unavailable,
                    RewardThresholds = options.RewardThresholds ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = referenceCatalog,
                }
            );
    }

    public static BiologySurveyViewModel? CreateSystemOverview(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        BiologySurveySystemOverviewOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ExobiologyReferenceCatalog referenceCatalog = options.ReferenceCatalog ?? DefaultBioReferenceCatalog.Value;
        snapshot = AddConfirmedExternalBiology(snapshot, options.ConfirmedExternalBiologySignals, referenceCatalog);
        SystemScanBodySnapshot[] biologicalBodies = snapshot
            .Bodies.Where(body => body.BiologicalSignalCount > 0)
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
                    DiscoveryContext = options.DiscoveryContext ?? BiologyDiscoveryContext.Unavailable,
                    DisablePredictions = options.DisablePredictions,
                    RadicoidaUnicaCount = options.RadicoidaUnicaCount,
                    RewardThresholds = options.RewardThresholds ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = referenceCatalog,
                    CanonnBiologyBodyIds = null,
                }
            );
    }

    public static BiologySurveyViewModel? CreateBodyDetail(
        SystemScanSnapshot snapshot,
        int bodyId,
        ExobiologySnapshot exobiology,
        BiologySurveyBodyDetailOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        ArgumentNullException.ThrowIfNull(options);
        ExobiologyReferenceCatalog referenceCatalog = options.ReferenceCatalog ?? DefaultBioReferenceCatalog.Value;
        snapshot = AddConfirmedExternalBiology(snapshot, options.ConfirmedExternalBiologySignals, referenceCatalog);
        SystemScanBodySnapshot? body = snapshot.Bodies.FirstOrDefault(candidate =>
            candidate.BodyId == bodyId && candidate.BiologicalSignalCount > 0
        );
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
                    DiscoveryContext = options.DiscoveryContext ?? BiologyDiscoveryContext.Unavailable,
                    RewardThresholds = options.RewardThresholds ?? BiologyRewardThresholds.Default,
                    PredictionEvaluator = options.PredictionEvaluator ?? DefaultPredictionEvaluator.Value,
                    ReferenceCatalog = referenceCatalog,
                }
            );
    }

    public static IReadOnlyList<BiologySignalRewardBandViewModel> CreateRewardBandsForBody(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        BiologySurveyRewardBandOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(options);
        BiologyRewardThresholds thresholds = options.RewardThresholds ?? BiologyRewardThresholds.Default;
        BiologyPredictionSet predictions = CreatePredictions(
            snapshot,
            body,
            options.DisablePredictions,
            options.PredictionEvaluator ?? DefaultPredictionEvaluator.Value,
            options.ReferenceCatalog ?? DefaultBioReferenceCatalog.Value
        );
        return CreateSystemRewardBands(
            body,
            predictions,
            options.HighlightRegionalFirsts,
            options.DiscoveryContext ?? BiologyDiscoveryContext.Unavailable,
            options.ReferenceCatalog ?? DefaultBioReferenceCatalog.Value,
            thresholds
        );
    }

    private static BiologySurveyViewModel CreateSystem(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies,
        BiologySurveySystemBuildOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        int? destinationBodyId =
            status?.Destination is { } destination && destination.System == snapshot.SystemAddress
                ? destination.Body
                : (int?)null;
        int? currentBodyId = ResolveCurrentBody(snapshot, status)?.BodyId;
        var rowData = biologicalBodies
            .Select(body =>
            {
                BiologyPredictionSet predictions = CreatePredictions(
                    snapshot,
                    body,
                    options.DisablePredictions,
                    options.PredictionEvaluator,
                    options.ReferenceCatalog
                );
                BiologyRewardEstimate estimate = CreateRewardEstimate(body, predictions);
                BiologySignalRewardBandViewModel[] rewardBands = CreateSystemRewardBands(
                    body,
                    predictions,
                    options.HighlightRegionalFirsts,
                    options.DiscoveryContext,
                    options.ReferenceCatalog,
                    options.RewardThresholds
                );
                var row = new BiologyBodyRowViewModel
                {
                    BodyId = body.BodyId,
                    Name = body.ShortName,
                    BodySubtype = ResolveBodySubtype(body),
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
                    RewardBucketThreeMillions = options.RewardThresholds.BucketThreeMillions,
                };
                return new { Row = row, Estimate = estimate };
            })
            .ToArray();
        BiologyBodyRowViewModel[] rows = rowData.Select(item => item.Row).ToArray();
        int analyzed = biologicalBodies.Sum(body => body.AnalyzedBiologicalSignalCount);
        int total = biologicalBodies.Sum(body => body.BiologicalSignalCount);
        long knownSystemReward = rows.Sum(row => row.KnownReward);
        long minimumSystemReward = rowData.Sum(item => item.Estimate.MinimumReward);
        long maximumSystemReward = rowData.Sum(item => item.Estimate.MaximumReward);
        bool hasPredictedReward = rowData.Any(item => item.Estimate.HasPredictedReward);
        bool hasUnknownReward = rowData.Any(item => item.Estimate.HasUnknownReward);

        return new BiologySurveyViewModel
        {
            Mode = BiologySurveyMode.System,
            Title = "SYSTEM BIOLOGY",
            SelectedBodyId = null,
            Heading = snapshot.SystemName ?? "Current system",
            ProgressText = $"{analyzed:N0} of {total:N0} biological signals analyzed",
            Bodies = rows,
            Organisms = [],
            RewardSummary = hasPredictedReward
                ? FormatCompactEstimatedReward(minimumSystemReward, maximumSystemReward, hasUnknownReward)
                : FormatCompactKnownReward(knownSystemReward, hasUnknownReward),
            FirstFootfallRewardSummary = string.Empty,
            RadicoidaUnicaCount = options.RadicoidaUnicaCount,
            RequiresDss = false,
            PredictionStatus = string.Empty,
            GeologicalSignalCount = 0,
            GeologicalSignals = [],
        };
    }

    private static string ResolveBodySubtype(SystemScanBodySnapshot body)
    {
        if (!string.IsNullOrWhiteSpace(body.PlanetClass))
        {
            return body.PlanetClass;
        }

        return body.Kind switch
        {
            SystemBodyKind.Star when !string.IsNullOrWhiteSpace(body.StarClass) => $"{body.StarClass} star",
            SystemBodyKind.Star => "Star",
            SystemBodyKind.GasGiant => "Gas giant",
            SystemBodyKind.Asteroid => "Asteroid cluster",
            SystemBodyKind.Barycentre => "Barycentre",
            _ => string.Empty,
        };
    }

    private static BiologySurveyViewModel CreateBody(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        ExobiologySnapshot exobiology,
        BiologySurveyBodyBuildOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        bool highlightRegionalFirsts = options.HighlightRegionalFirsts;
        bool dimAnalyzedOrganisms = options.DimAnalyzedOrganisms;
        bool hideGeoCount = options.HideGeoCount;
        bool disablePredictions = options.DisablePredictions;
        BiologyDiscoveryContext discoveryContext = options.DiscoveryContext;
        BiologyRewardThresholds rewardThresholds = options.RewardThresholds;
        BiologyPredictionEvaluator predictionEvaluator = options.PredictionEvaluator;
        ExobiologyReferenceCatalog referenceCatalog = options.ReferenceCatalog;
        BiologyPredictionSet predictionSet = CreatePredictions(
            snapshot,
            body,
            disablePredictions,
            predictionEvaluator,
            referenceCatalog
        );
        List<BiologyOrganismRowViewModel> organisms = BuildBodyOrganismRows(
            new BodyOrganismRowBuildContext
            {
                Body = body,
                Exobiology = exobiology,
                PredictionSet = predictionSet,
                HighlightRegionalFirsts = highlightRegionalFirsts,
                DimAnalyzedOrganisms = dimAnalyzedOrganisms,
                DiscoveryContext = discoveryContext,
                RewardThresholds = rewardThresholds,
                ReferenceCatalog = referenceCatalog,
            }
        );
        BiologyRewardEstimate rewardEstimate = CreateRewardEstimate(body, predictionSet);
        int geoCount = hideGeoCount ? 0 : body.GeologicalSignalCount;
        IReadOnlyList<string> geoSignals = hideGeoCount ? Array.Empty<string>() : body.AnalyzedGeologicalSignals;

        bool isIdentified = body.IsDssComplete;
        return new BiologySurveyViewModel
        {
            Mode = BiologySurveyMode.Body,
            Title = isIdentified ? "IDENTIFIED BIO" : "BODY PREDICTIONS",
            SelectedBodyId = body.BodyId,
            Heading = body.Name,
            ProgressText = FormatBodyProgressText(body.BiologicalSignalCount),
            Bodies = [],
            Organisms = organisms,
            RewardSummary = isIdentified
                ? FormatIdentifiedRewardSummary(rewardEstimate)
                : FormatCompactBodyRewardSummary(rewardEstimate),
            FirstFootfallRewardSummary = isIdentified
                ? FormatCompactFirstFootfallRewardSummary(body, rewardEstimate)
                : FormatFirstFootfallRewardSummary(body, rewardEstimate),
            RadicoidaUnicaCount = exobiology.CountRadicoidaUnica,
            RequiresDss = body.Organisms.Count == 0 && !body.IsDssComplete,
            PredictionStatus = isIdentified ? "DSS Scan Complete\nExact Organisms Identified" : predictionSet.Status,
            GeologicalSignalCount = geoCount,
            GeologicalSignals = geoSignals,
        };
    }

    private static string FormatBodyProgressText(int biologicalSignalCount)
    {
        return biologicalSignalCount == 1 ? "1 biological signal" : $"{biologicalSignalCount:N0} biological signals";
    }

    private static string FormatCompactBodyRewardSummary(BiologyRewardEstimate rewardEstimate)
    {
        return rewardEstimate.HasPredictedReward
            ? FormatCompactEstimatedReward(
                rewardEstimate.MinimumReward,
                rewardEstimate.MaximumReward,
                rewardEstimate.HasUnknownReward
            )
            : FormatCompactKnownReward(rewardEstimate.KnownReward, rewardEstimate.HasUnknownReward);
    }

    private static string FormatIdentifiedRewardSummary(BiologyRewardEstimate rewardEstimate)
    {
        if (rewardEstimate.HasUnscannedOrganismReward)
        {
            return "Estimated reward:\n"
                + FormatRewardRange(
                    rewardEstimate.MinimumReward,
                    rewardEstimate.MaximumReward,
                    rewardEstimate.HasUnknownReward
                );
        }

        if (rewardEstimate.KnownReward <= 0)
        {
            return rewardEstimate.HasUnknownReward ? "Reward pending identification" : string.Empty;
        }

        if (rewardEstimate.HasPredictedReward)
        {
            return "Estimated reward:\n"
                + FormatRewardRange(
                    rewardEstimate.MinimumReward,
                    rewardEstimate.MaximumReward,
                    rewardEstimate.HasUnknownReward
                );
        }

        string value = FormatCompactCredits(rewardEstimate.KnownReward);
        return "Known reward:\n" + (rewardEstimate.HasUnknownReward ? value + PendingRewardSuffix : value);
    }

    private static string FormatFirstFootfallRewardSummary(
        SystemScanBodySnapshot body,
        BiologyRewardEstimate rewardEstimate
    )
    {
        if (!body.IsFirstFootfall || rewardEstimate.MaximumReward <= 0)
        {
            return string.Empty;
        }

        if (rewardEstimate.HasPredictedReward)
        {
            return "First-footfall estimate: "
                + FormatRewardRange(
                    rewardEstimate.MinimumReward * 5,
                    rewardEstimate.MaximumReward * 5,
                    rewardEstimate.HasUnknownReward
                );
        }

        return "First-footfall value: " + FormatCredits(rewardEstimate.KnownReward * 5);
    }

    private static string FormatCompactFirstFootfallRewardSummary(
        SystemScanBodySnapshot body,
        BiologyRewardEstimate rewardEstimate
    )
    {
        if (!body.IsFirstFootfall || rewardEstimate.MaximumReward <= 0)
        {
            return string.Empty;
        }

        if (rewardEstimate.HasPredictedReward)
        {
            string minimum = FormatCompactCredits(rewardEstimate.MinimumReward * 5);
            string maximum = FormatCompactCredits(rewardEstimate.MaximumReward * 5);
            string range = minimum == maximum ? minimum : $"{minimum} – {maximum}";
            return "First-footfall estimate:\n"
                + (rewardEstimate.HasUnknownReward ? range + PendingRewardSuffix : range);
        }

        return "First-footfall total:\n" + FormatCompactCredits(rewardEstimate.KnownReward * 5);
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

        public required ExobiologyReferenceCatalog ReferenceCatalog { get; init; }
    }

    private static List<BiologyOrganismRowViewModel> BuildBodyOrganismRows(BodyOrganismRowBuildContext context)
    {
        SystemScanBodySnapshot body = context.Body;
        var predictionsByGenus = context
            .PredictionSet.Predictions.GroupBy(
                prediction => prediction.Prediction.Genus,
                StringComparer.OrdinalIgnoreCase
            )
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var consumedPredictions = new HashSet<string>(StringComparer.Ordinal);
        var organisms = new List<BiologyOrganismRowViewModel>();
        foreach (SystemOrganismSnapshot organism in body.Organisms)
        {
            AddKnownOrganismRows(organisms, consumedPredictions, organism, predictionsByGenus, context);
        }

        // Once the body has identified organisms, its journal rows are the
        // source of truth. Genus-only rows above may still expand to their
        // possible species, but predictions for unrelated genera have been
        // disproved by the scan and must not remain in the identified view.
        if (body.Organisms.Count == 0)
        {
            AddRemainingPredictionRows(organisms, consumedPredictions, context);
        }

        while (organisms.Count < body.BiologicalSignalCount)
        {
            organisms.Add(BiologyOrganismRowViewModel.Unknown(organisms.Count + 1, context.RewardThresholds));
        }

        return organisms;
    }

    private static void AddKnownOrganismRows(
        List<BiologyOrganismRowViewModel> organisms,
        HashSet<string> consumedPredictions,
        SystemOrganismSnapshot organism,
        IReadOnlyDictionary<string, BiologyPredictionPresentation[]> predictionsByGenus,
        BodyOrganismRowBuildContext context
    )
    {
        string genusName = organism.GenusLocalized ?? FormatJournalName(organism.Genus);
        if (
            organism.Variant is null
            && predictionsByGenus.TryGetValue(genusName, out BiologyPredictionPresentation[]? predictions)
        )
        {
            foreach (BiologyPredictionPresentation prediction in predictions)
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
        BodyOrganismRowBuildContext context
    )
    {
        foreach (
            BiologyPredictionPresentation? prediction in context.PredictionSet.Predictions.Where(prediction =>
                !consumedPredictions.Contains(prediction.Prediction.Name)
            )
        )
        {
            if (
                context.Body.Organisms.Any(organism =>
                    prediction.Reference is not null
                    && (
                        organism.Variant == prediction.Reference.VariantName
                        || organism.Species == prediction.Reference.SpeciesName
                    )
                )
            )
            {
                continue;
            }

            organisms.Add(CreatePrediction(prediction, context));
        }
    }

    private static BiologyOrganismRowViewModel CreateOrganism(
        SystemOrganismSnapshot organism,
        BodyOrganismRowBuildContext context
    )
    {
        SystemScanBodySnapshot body = context.Body;
        ExobiologySnapshot exobiology = context.Exobiology;
        bool highlightRegionalFirsts = context.HighlightRegionalFirsts;
        bool dimAnalyzedOrganisms = context.DimAnalyzedOrganisms;
        BiologyDiscoveryContext discoveryContext = context.DiscoveryContext;
        BiologyRewardThresholds rewardThresholds = context.RewardThresholds;
        ExobiologyReference? reference = organism.EntryId is > 0 and { } entryId
            ? context.ReferenceCatalog.FindByEntryId(entryId)
            : null;
        reference ??=
            context.ReferenceCatalog.FindByVariant(organism.Variant)
            ?? context.ReferenceCatalog.FindBySpecies(organism.Species);
        string displayName =
            organism.VariantLocalized
            ?? reference?.DisplayName
            ?? organism.SpeciesLocalized
            ?? organism.GenusLocalized
            ?? FormatJournalName(organism.Variant ?? organism.Species ?? organism.Genus);
        string genusName = organism.GenusLocalized ?? FormatJournalName(organism.Genus);
        string speciesName = FormatSpeciesName(
            genusName,
            organism.SpeciesLocalized ?? FormatReferenceSpecies(reference?.DisplayName),
            organism.Species
        );
        string variantName = FormatVariantName(
            organism.VariantLocalized ?? reference?.DisplayName,
            organism.Variant,
            organism.SpeciesLocalized
        );
        bool activeSample =
            exobiology.ScanOne is { } scan
            && !organism.IsAnalyzed
            && string.Equals(scan.Body, body.Name, StringComparison.OrdinalIgnoreCase)
            && IsActiveOrganism(organism, scan);
        BiologyFirstDiscoveryState firstDiscovery = ClassifyOrganismFirst(
            body,
            organism,
            discoveryContext,
            context.ReferenceCatalog
        );

        return new BiologyOrganismRowViewModel
        {
            DisplayName = displayName,
            GenusName = genusName,
            SpeciesName = speciesName,
            VariantName = variantName,
            SampleDistanceMeters = ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                organism.GenusLocalized ?? organism.Genus
            ),
            Reward = organism.Reward ?? 0,
            HasReward = organism.Reward is not null,
            IsAnalyzed = organism.IsAnalyzed,
            IsCommanderFirst = firstDiscovery.IsCommanderFirst,
            IsRegionalFirst = firstDiscovery.IsRegionalFirst,
            IsGlobalRegionalFirst = firstDiscovery.IsGlobalRegionalFirst,
            IsHighlightedFirst = firstDiscovery.IsHighlighted(highlightRegionalFirsts),
            IsCurrentSample = activeSample,
            IsPrediction = organism.Variant is not null && !organism.IsScanned,
            IsGenusIdentified = organism.Variant is null,
            IsUnknown = false,
            ShouldDim = dimAnalyzedOrganisms && organism.IsAnalyzed,
            RewardBucketOneMillions = rewardThresholds.BucketOneMillions,
            RewardBucketTwoMillions = rewardThresholds.BucketTwoMillions,
            RewardBucketThreeMillions = rewardThresholds.BucketThreeMillions,
        };
    }

    private static bool IsActiveOrganism(SystemOrganismSnapshot organism, BioSampleSnapshot sample)
    {
        if (sample.EntryId > 0 && organism.EntryId is > 0)
        {
            return sample.EntryId == organism.EntryId;
        }

        if (!string.IsNullOrWhiteSpace(sample.Species) && !string.IsNullOrWhiteSpace(organism.Species))
        {
            return string.Equals(sample.Species, organism.Species, StringComparison.Ordinal);
        }

        return string.Equals(sample.Genus, organism.Genus, StringComparison.Ordinal);
    }

    private static BiologyOrganismRowViewModel CreatePrediction(
        BiologyPredictionPresentation prediction,
        BodyOrganismRowBuildContext context
    )
    {
        SystemScanBodySnapshot body = context.Body;
        ExobiologySnapshot exobiology = context.Exobiology;
        bool highlightRegionalFirsts = context.HighlightRegionalFirsts;
        BiologyDiscoveryContext discoveryContext = context.DiscoveryContext;
        BiologyRewardThresholds rewardThresholds = context.RewardThresholds;
        bool activeSample =
            exobiology.ScanOne is { } scan
            && string.Equals(scan.Body, body.Name, StringComparison.OrdinalIgnoreCase)
            && IsActivePrediction(prediction.Reference, scan);
        long reward = prediction.Reference?.Reward ?? 0;
        BiologyFirstDiscoveryState firstDiscovery = ClassifyPredictionFirst(prediction.Reference, discoveryContext);

        return new BiologyOrganismRowViewModel
        {
            DisplayName = prediction.Prediction.Name,
            GenusName = prediction.Prediction.Genus,
            SpeciesName = prediction.Prediction.Species,
            VariantName = prediction.Prediction.Variant,
            SampleDistanceMeters = ExobiologyReferenceCatalog.GetSampleDistanceMeters(prediction.Prediction.Genus),
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
            RewardBucketThreeMillions = rewardThresholds.BucketThreeMillions,
        };
    }

    private static bool IsActivePrediction(ExobiologyReference? reference, BioSampleSnapshot sample)
    {
        if (reference is null)
        {
            return false;
        }

        if (sample.EntryId > 0)
        {
            return sample.EntryId == reference.EntryId;
        }

        return !string.IsNullOrWhiteSpace(sample.Species)
            && string.Equals(sample.Species, reference.SpeciesName, StringComparison.Ordinal);
    }

    private static SystemScanSnapshot AddConfirmedExternalBiology(
        SystemScanSnapshot snapshot,
        IReadOnlyDictionary<int, IReadOnlyList<CanonnSurfaceBiologySignal>>? signalsByBodyId,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        if (signalsByBodyId is null || signalsByBodyId.Count == 0)
        {
            return snapshot;
        }

        SystemScanBodySnapshot[] bodies = snapshot
            .Bodies.Select(body =>
                signalsByBodyId.TryGetValue(body.BodyId, out IReadOnlyList<CanonnSurfaceBiologySignal>? signals)
                    ? AddConfirmedExternalBiology(body, signals, referenceCatalog)
                    : body
            )
            .ToArray();
        return bodies.SequenceEqual(snapshot.Bodies) ? snapshot : snapshot with { Bodies = bodies };
    }

    private static SystemScanBodySnapshot AddConfirmedExternalBiology(
        SystemScanBodySnapshot body,
        IReadOnlyList<CanonnSurfaceBiologySignal> signals,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        var organisms = body.Organisms.ToList();
        foreach (CanonnSurfaceBiologySignal signal in signals)
        {
            SystemOrganismSnapshot? confirmed = CreateConfirmedExternalOrganism(signal, referenceCatalog);
            if (confirmed is not null)
            {
                MergeConfirmedExternalOrganism(organisms, confirmed);
            }
        }

        return organisms.SequenceEqual(body.Organisms) ? body : body with { Organisms = organisms };
    }

    private static void MergeConfirmedExternalOrganism(
        List<SystemOrganismSnapshot> organisms,
        SystemOrganismSnapshot confirmed
    )
    {
        int existingIndex = organisms.FindIndex(organism =>
            organism.EntryId is > 0 && organism.EntryId == confirmed.EntryId
        );
        if (existingIndex >= 0)
        {
            SystemOrganismSnapshot existing = organisms[existingIndex];
            if (confirmed.IsScanned && !existing.IsScanned)
            {
                organisms[existingIndex] = existing with { IsScanned = true };
            }

            return;
        }

        int genusOnlyIndex = organisms.FindIndex(organism => IsSameUnresolvedGenus(organism, confirmed));
        if (genusOnlyIndex < 0)
        {
            organisms.Add(confirmed);
            return;
        }

        SystemOrganismSnapshot genusExisting = organisms[genusOnlyIndex];
        organisms[genusOnlyIndex] = confirmed with
        {
            IsScanned = confirmed.IsScanned || genusExisting.IsScanned,
            IsAnalyzed = confirmed.IsAnalyzed || genusExisting.IsAnalyzed,
            IsRegionalFirst = confirmed.IsRegionalFirst || genusExisting.IsRegionalFirst,
            GenusLocalized = confirmed.GenusLocalized ?? genusExisting.GenusLocalized,
        };
    }

    private static SystemOrganismSnapshot? CreateConfirmedExternalOrganism(
        CanonnSurfaceBiologySignal signal,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        ExobiologyReference? reference = referenceCatalog.FindByEntryId(signal.EntryId);
        reference ??= referenceCatalog.FindByDisplayName(signal.DisplayName);
        if (reference is not { IsBiology: true, Reward: > 0 })
        {
            return null;
        }

        return new SystemOrganismSnapshot(
            ExobiologyReferenceCatalog.GetGenusName(reference),
            ExobiologyReferenceCatalog.GetGenusDisplayName(reference),
            reference.SpeciesName,
            null,
            reference.VariantName,
            signal.DisplayName ?? reference.DisplayName,
            reference.EntryId,
            reference.Reward,
            signal.IsCommanderScan,
            false,
            false
        );
    }

    private static bool IsSameUnresolvedGenus(SystemOrganismSnapshot organism, SystemOrganismSnapshot confirmed) =>
        organism.Species is null
        && (
            string.Equals(organism.Genus, confirmed.Genus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(organism.GenusLocalized, confirmed.GenusLocalized, StringComparison.OrdinalIgnoreCase)
        );

    private static BiologyPredictionSet CreatePredictions(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        bool disablePredictions,
        BiologyPredictionEvaluator predictionEvaluator,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        if (disablePredictions)
        {
            return BiologyPredictionSet.NoPredictions;
        }

        BiologyPredictionInputs? inputs = BiologyPredictionContextBuilder.Build(snapshot, body.BodyId);
        if (inputs is null)
        {
            return new BiologyPredictionSet([], "Predictions need complete body and parent-star scans.", false);
        }

        BiologyPredictionResult result = predictionEvaluator.Evaluate(inputs.Context, inputs.Knowledge);
        if (!result.HasCompleteContext)
        {
            return new BiologyPredictionSet(
                [],
                "Predictions waiting for: " + string.Join(", ", result.MissingProperties),
                false
            );
        }

        return new BiologyPredictionSet(
            result
                .PredictionDetails.Select(prediction => new BiologyPredictionPresentation(
                    prediction,
                    referenceCatalog.FindByDisplayName(prediction.Name)
                ))
                .ToArray(),
            string.Empty,
            true
        );
    }

    private static BiologyRewardEstimate CreateRewardEstimate(
        SystemScanBodySnapshot body,
        BiologyPredictionSet predictionSet
    )
    {
        long knownReward = body.Organisms.Where(organism => organism.IsScanned).Sum(organism => organism.Reward ?? 0);
        long unscannedReward = body
            .Organisms.Where(organism => !organism.IsScanned && organism.Reward.HasValue)
            .Sum(organism => organism.Reward!.Value);
        int remainingSignals = Math.Max(
            0,
            body.BiologicalSignalCount - body.Organisms.Count(organism => organism.Species is not null)
        );
        bool hasUnscannedOrganismReward = unscannedReward > 0;
        if (remainingSignals == 0)
        {
            return new BiologyRewardEstimate(
                knownReward,
                knownReward + unscannedReward,
                knownReward + unscannedReward,
                hasUnscannedOrganismReward,
                false,
                hasUnscannedOrganismReward
            );
        }

        var rewardGroups = predictionSet
            .Predictions.Where(prediction => prediction.Reference?.Reward > 0)
            .GroupBy(prediction => prediction.Prediction.Genus, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Minimum = group.Min(prediction => prediction.Reference!.Reward),
                Maximum = group.Max(prediction => prediction.Reference!.Reward),
            })
            .ToArray();
        long minimumAdd = rewardGroups
            .OrderBy(group => group.Minimum)
            .Take(remainingSignals)
            .Sum(group => group.Minimum);
        long maximumAdd = rewardGroups
            .OrderByDescending(group => group.Maximum)
            .Take(remainingSignals)
            .Sum(group => group.Maximum);
        int predictedCount = Math.Min(remainingSignals, rewardGroups.Length);

        return new BiologyRewardEstimate(
            knownReward,
            knownReward + unscannedReward + minimumAdd,
            knownReward + unscannedReward + maximumAdd,
            hasUnscannedOrganismReward || predictedCount > 0,
            !predictionSet.IsComplete || predictedCount < remainingSignals,
            hasUnscannedOrganismReward
        );
    }

    private static BiologySignalRewardBandViewModel[] CreateSystemRewardBands(
        SystemScanBodySnapshot body,
        BiologyPredictionSet predictionSet,
        bool highlightRegionalFirsts,
        BiologyDiscoveryContext discoveryContext,
        ExobiologyReferenceCatalog referenceCatalog,
        BiologyRewardThresholds rewardThresholds
    )
    {
        var predictionsByGenus = predictionSet
            .Predictions.Where(prediction => prediction.Reference?.Reward > 0)
            .GroupBy(prediction => prediction.Prediction.Genus, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    BiologyFirstDiscoveryState[] discoveryStates = group
                        .Select(prediction => ClassifyPredictionFirst(prediction.Reference, discoveryContext))
                        .ToArray();
                    return new BiologySignalRewardRange(
                        group.Min(prediction => prediction.Reference!.Reward),
                        group.Max(prediction => prediction.Reference!.Reward),
                        discoveryStates.Any(state => state.IsHighlighted(highlightRegionalFirsts)),
                        discoveryStates.Any(state => state.IsGlobalRegionalFirst)
                    );
                },
                StringComparer.OrdinalIgnoreCase
            );
        var consumedPredictionGenera = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bands = new List<BiologySignalRewardBandViewModel>(body.BiologicalSignalCount);

        // Preserve the legacy sequence: known/DSS-resolved genera first,
        // every remaining predicted genus second, and unidentified signal
        // slots last. Legacy deliberately rendered predictions beyond the
        // reported signal count as alternative candidates outside its signal
        // frame, so do not truncate those additional PIPs here.
        foreach (SystemOrganismSnapshot organism in body.Organisms)
        {
            string genus = organism.GenusLocalized ?? FormatJournalName(organism.Genus);
            BiologyFirstDiscoveryState discoveryState = ClassifyOrganismFirst(
                body,
                organism,
                discoveryContext,
                referenceCatalog
            );
            bool isHighlighted = discoveryState.IsHighlighted(highlightRegionalFirsts);
            if (organism.Reward is { } reward && reward > 0)
            {
                bands.Add(
                    organism.IsScanned
                        ? BiologySignalRewardBandViewModel.Known(
                            reward,
                            isHighlighted,
                            organism.IsAnalyzed,
                            rewardThresholds,
                            discoveryState.IsGlobalRegionalFirst
                        )
                        : BiologySignalRewardBandViewModel.Predicted(
                            reward,
                            reward,
                            isHighlighted,
                            rewardThresholds,
                            discoveryState.IsGlobalRegionalFirst
                        )
                );
                consumedPredictionGenera.Add(genus);
                continue;
            }

            if (predictionsByGenus.TryGetValue(genus, out BiologySignalRewardRange? prediction))
            {
                bands.Add(
                    BiologySignalRewardBandViewModel.Predicted(
                        prediction.Minimum,
                        prediction.Maximum,
                        isHighlighted || prediction.IsHighlighted,
                        rewardThresholds,
                        prediction.IsGlobalRegionalFirst
                    )
                );
                consumedPredictionGenera.Add(genus);
                continue;
            }

            bands.Add(BiologySignalRewardBandViewModel.Unknown(rewardThresholds));
        }

        foreach (KeyValuePair<string, BiologySignalRewardRange> prediction in predictionsByGenus)
        {
            if (consumedPredictionGenera.Contains(prediction.Key))
            {
                continue;
            }

            bands.Add(
                BiologySignalRewardBandViewModel.Predicted(
                    prediction.Value.Minimum,
                    prediction.Value.Maximum,
                    prediction.Value.IsHighlighted,
                    rewardThresholds,
                    prediction.Value.IsGlobalRegionalFirst
                )
            );
        }

        while (bands.Count < body.BiologicalSignalCount)
        {
            bands.Add(BiologySignalRewardBandViewModel.Unknown(rewardThresholds));
        }

        return bands.ToArray();
    }

    private static SystemScanBodySnapshot? ResolveBody(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies,
        bool drawBodyBiosOnlyWhenNear,
        bool allowRetainedCurrentBody,
        bool forceSystemOverview
    )
    {
        if (forceSystemOverview || status?.GuiFocus is GuiFocus.ExternalPanel or GuiFocus.SystemMap or GuiFocus.Orrery)
        {
            return null;
        }

        if (status?.GuiFocus == GuiFocus.Fss)
        {
            return snapshot.LastDetailedBodyId is { } lastBodyId
                ? biologicalBodies.FirstOrDefault(body => body.BodyId == lastBodyId)
                : null;
        }

        SystemScanBodySnapshot? current = allowRetainedCurrentBody ? ResolveCurrentBody(snapshot, status) : null;
        if (current?.BiologicalSignalCount is not > 0)
        {
            current = null;
        }

        SystemScanBodySnapshot? destination =
            status?.Destination is { } target && target.System == snapshot.SystemAddress
                ? biologicalBodies.FirstOrDefault(body => body.BodyId == target.Body)
                : null;
        if (!drawBodyBiosOnlyWhenNear)
        {
            return destination ?? current;
        }

        return destination is null || destination.BodyId == current?.BodyId ? current : null;
    }

    private static SystemScanBodySnapshot? ResolveCurrentBody(SystemScanSnapshot snapshot, EliteStatus? status)
    {
        SystemScanBodySnapshot? current = !string.IsNullOrWhiteSpace(status?.BodyName)
            ? snapshot.Bodies.FirstOrDefault(body =>
                string.Equals(body.Name, status.BodyName, StringComparison.OrdinalIgnoreCase)
            )
            : null;
        return current
            ?? (
                snapshot.CurrentBodyId is { } bodyId
                    ? snapshot.Bodies.FirstOrDefault(body => body.BodyId == bodyId)
                    : null
            );
    }

    private static string FormatCompactKnownReward(long reward, bool hasUnknown)
    {
        if (reward <= 0)
        {
            return hasUnknown ? "Reward pending identification" : string.Empty;
        }

        string label = hasUnknown ? "Known reward:" : "Total reward:";
        return $"{label}\n{FormatCompactCredits(reward)}";
    }

    private static string FormatCompactEstimatedReward(long minimum, long maximum, bool hasUnknown)
    {
        string range =
            minimum == maximum
                ? FormatCompactCredits(minimum)
                : $"{FormatCompactCredits(minimum)} – {FormatCompactCredits(maximum)}";
        return "Estimated reward:\n" + (hasUnknown ? range + PendingRewardSuffix : range);
    }

    private static string FormatRewardRange(long minimum, long maximum, bool hasUnknown)
    {
        string range =
            minimum == maximum ? FormatCredits(minimum) : $"{FormatCredits(minimum)} – {FormatCredits(maximum)}";
        return hasUnknown ? range + PendingRewardSuffix : range;
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

    private static string FormatCompactCredits(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:N2} M",
            >= 1_000 => $"{value / 1_000d:N1} K",
            _ => $"{value:N0}",
        };
    }

    private static string FormatJournalName(string value)
    {
        string normalized = value
            .Replace("$Codex_Ent_", string.Empty, StringComparison.Ordinal)
            .Replace("_Genus_Name;", string.Empty, StringComparison.Ordinal)
            .Replace("_Name;", string.Empty, StringComparison.Ordinal)
            .Replace('_', ' ')
            .Trim('$', ';', ' ');
        return string.IsNullOrWhiteSpace(normalized) ? "Unidentified organism" : normalized;
    }

    private static string FormatSpeciesName(string genusName, string? speciesLocalized, string? species)
    {
        string? display = speciesLocalized ?? (species is null ? null : FormatJournalName(species));
        if (string.IsNullOrWhiteSpace(display))
        {
            return string.Empty;
        }

        string prefix = genusName + " ";
        return display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? display[prefix.Length..].Trim()
            : display.Trim();
    }

    private static string FormatVariantName(string? variantLocalized, string? variant, string? speciesLocalized)
    {
        if (!string.IsNullOrWhiteSpace(variantLocalized))
        {
            int separator = variantLocalized.LastIndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0 && separator + 3 < variantLocalized.Length)
            {
                return variantLocalized[(separator + 3)..].Trim();
            }

            if (
                !string.IsNullOrWhiteSpace(speciesLocalized)
                && variantLocalized.StartsWith(speciesLocalized, StringComparison.OrdinalIgnoreCase)
            )
            {
                return variantLocalized[speciesLocalized.Length..].Trim(' ', '-', ':');
            }
        }

        return variant is null ? string.Empty : FormatJournalName(variant);
    }

    private static string? FormatReferenceSpecies(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        int separator = displayName.LastIndexOf(" - ", StringComparison.Ordinal);
        return separator < 0 ? displayName : displayName[..separator];
    }

    private static BiologyFirstDiscoveryState ClassifyOrganismFirst(
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot organism,
        BiologyDiscoveryContext discoveryContext,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        long? resolvedEntryId = organism.EntryId is > 0
            ? organism.EntryId
            : referenceCatalog.FindByVariant(organism.Variant)?.EntryId
                ?? referenceCatalog.FindBySpecies(organism.Species)?.EntryId;
        bool commanderFirst =
            resolvedEntryId is > 0 and { } entryId && discoveryContext.IsPersonalFirst(entryId, body.BodyId);
        bool regionalFirst =
            !commanderFirst
            && (
                organism.IsRegionalFirst
                || resolvedEntryId is > 0 and { } regionalEntryId
                    && !organism.IsAnalyzed
                    && discoveryContext.IsRegionalNew(regionalEntryId)
            );

        // Legacy SrvSurvey only applies the externally maintained
        // codexNotFound catalog to predictions. Once the organism is known,
        // the journal's IsNewEntry value and the commander's Codex ledgers are
        // authoritative; a stale external candidate must not remain displayed
        // as a Galactic-region first.
        return new BiologyFirstDiscoveryState(commanderFirst, regionalFirst, IsGlobalRegionalFirst: false);
    }

    private static BiologyFirstDiscoveryState ClassifyPredictionFirst(
        ExobiologyReference? reference,
        BiologyDiscoveryContext discoveryContext
    )
    {
        bool globalRegionalFirst = reference is not null && discoveryContext.IsGlobalRegionalNew(reference.EntryId);
        bool commanderFirst =
            !globalRegionalFirst && reference is not null && discoveryContext.IsCommanderNew(reference.EntryId);
        bool regionalFirst =
            !globalRegionalFirst
            && reference is not null
            && !commanderFirst
            && discoveryContext.IsRegionalNew(reference.EntryId);
        return new BiologyFirstDiscoveryState(commanderFirst, regionalFirst, globalRegionalFirst);
    }

    private static readonly Lazy<BiologyPredictionEvaluator> DefaultPredictionEvaluator = new(() =>
        new BiologyPredictionEvaluator(BiologyCriteriaCatalog.LoadEmbedded())
    );

    private static readonly Lazy<ExobiologyReferenceCatalog> DefaultBioReferenceCatalog = new(
        ExobiologyReferenceCatalog.LoadEmbedded
    );

    private sealed record BiologyPredictionPresentation(BiologyPrediction Prediction, ExobiologyReference? Reference);

    private sealed record BiologyPredictionSet(
        IReadOnlyList<BiologyPredictionPresentation> Predictions,
        string Status,
        bool IsComplete
    )
    {
        public static BiologyPredictionSet NoPredictions { get; } = new([], string.Empty, false);
    }

    private sealed record BiologyRewardEstimate(
        long KnownReward,
        long MinimumReward,
        long MaximumReward,
        bool HasPredictedReward,
        bool HasUnknownReward,
        bool HasUnscannedOrganismReward
    );

    private sealed record BiologySignalRewardRange(
        long Minimum,
        long Maximum,
        bool IsHighlighted,
        bool IsGlobalRegionalFirst
    );

    private readonly record struct BiologyFirstDiscoveryState(
        bool IsCommanderFirst,
        bool IsRegionalFirst,
        bool IsGlobalRegionalFirst
    )
    {
        public bool IsHighlighted(bool highlightRegionalFirsts) =>
            IsGlobalRegionalFirst || IsCommanderFirst || highlightRegionalFirsts && IsRegionalFirst;
    }
}

public enum BiologySurveyMode
{
    System,
    Body,
}

public sealed class BiologyBodyRowViewModel
{
    private RouteBodyVisual? bodyVisual;

    public int BodyId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string BodySubtype { get; init; } = string.Empty;

    public string BodyIconAssetPath => BodyVisual.AssetPath;

    public string BodyIconAccessibleName => BodyVisual.AccessibleName;

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

    public IReadOnlyList<BiologySignalRewardBandViewModel> RewardBands { get; init; } = [];

    public IEnumerable<BiologySignalRewardBandViewModel> SignalRewardBands =>
        RewardBands.Take(Math.Max(0, SignalCount));

    public IEnumerable<BiologySignalRewardBandViewModel> AlternativeRewardBands =>
        RewardBands.Skip(Math.Max(0, SignalCount));

    public bool HasAlternativeRewardBands => RewardBands.Count > SignalCount;

    public bool IsRewardBandGroupHighlighted =>
        IsDestination || (AnalyzedSignalCount > 0 && AnalyzedSignalCount < SignalCount);

    public double RewardBucketOneMillions { get; init; } = 3;

    public double RewardBucketTwoMillions { get; init; } = 7;

    public double RewardBucketThreeMillions { get; init; } = 12;

    public string ProgressText => $"{AnalyzedSignalCount:N0}/{SignalCount:N0}";

    public bool IsComplete => SignalCount > 0 && AnalyzedSignalCount >= SignalCount;

    public string RewardText
    {
        get
        {
            if (HasPredictedReward)
            {
                if (MinimumReward == MaximumReward)
                {
                    return $"~{MinimumReward / 1_000_000d:N2} M";
                }

                return $"{MinimumReward / 1_000_000d:N2}–\n{MaximumReward / 1_000_000d:N2} M";
            }

            if (KnownReward <= 0)
            {
                return string.Empty;
            }

            if (HasUnknownReward)
            {
                return $"{KnownReward / 1_000_000d:N2} M+";
            }

            return $"{KnownReward / 1_000_000d:N2} M";
        }
    }

    public bool HasReward => KnownReward > 0 || HasPredictedReward;

    public long RewardBandMinimum => HasPredictedReward ? MinimumReward : KnownReward;

    public long RewardBandMaximum => HasPredictedReward ? MaximumReward : KnownReward;

    private RouteBodyVisual BodyVisual => bodyVisual ??= RouteBodyAssetResolver.Resolve(BodySubtype);
}

public sealed class BiologySignalRewardBandViewModel
{
    public long MinimumReward { get; init; }

    public long MaximumReward { get; init; }

    public bool IsPrediction { get; init; }

    public bool IsHighlighted { get; init; }

    public bool IsGlobalRegionalFirst { get; init; }

    public bool ShouldDim { get; init; }

    public double RewardBucketOneMillions { get; init; }

    public double RewardBucketTwoMillions { get; init; }

    public double RewardBucketThreeMillions { get; init; }

    public double Opacity => ShouldDim ? 0.48 : 1;

    public static BiologySignalRewardBandViewModel Known(
        long reward,
        bool isHighlighted,
        bool shouldDim,
        BiologyRewardThresholds thresholds,
        bool isGlobalRegionalFirst = false
    ) =>
        new()
        {
            MinimumReward = reward,
            MaximumReward = reward,
            IsPrediction = false,
            IsHighlighted = isHighlighted,
            IsGlobalRegionalFirst = isGlobalRegionalFirst,
            ShouldDim = shouldDim,
            RewardBucketOneMillions = thresholds.BucketOneMillions,
            RewardBucketTwoMillions = thresholds.BucketTwoMillions,
            RewardBucketThreeMillions = thresholds.BucketThreeMillions,
        };

    public static BiologySignalRewardBandViewModel Predicted(
        long minimumReward,
        long maximumReward,
        bool isHighlighted,
        BiologyRewardThresholds thresholds,
        bool isGlobalRegionalFirst = false
    ) =>
        new()
        {
            MinimumReward = minimumReward,
            MaximumReward = maximumReward,
            IsPrediction = true,
            IsHighlighted = isHighlighted,
            IsGlobalRegionalFirst = isGlobalRegionalFirst,
            ShouldDim = false,
            RewardBucketOneMillions = thresholds.BucketOneMillions,
            RewardBucketTwoMillions = thresholds.BucketTwoMillions,
            RewardBucketThreeMillions = thresholds.BucketThreeMillions,
        };

    public static BiologySignalRewardBandViewModel KnownRange(
        long minimumReward,
        long maximumReward,
        bool isHighlighted,
        BiologyRewardThresholds thresholds,
        bool isGlobalRegionalFirst = false
    ) =>
        new()
        {
            MinimumReward = minimumReward,
            MaximumReward = maximumReward,
            IsPrediction = false,
            IsHighlighted = isHighlighted,
            IsGlobalRegionalFirst = isGlobalRegionalFirst,
            ShouldDim = false,
            RewardBucketOneMillions = thresholds.BucketOneMillions,
            RewardBucketTwoMillions = thresholds.BucketTwoMillions,
            RewardBucketThreeMillions = thresholds.BucketThreeMillions,
        };

    public static BiologySignalRewardBandViewModel Unknown(BiologyRewardThresholds thresholds) =>
        new()
        {
            MinimumReward = 0,
            MaximumReward = 0,
            IsPrediction = false,
            IsHighlighted = false,
            IsGlobalRegionalFirst = false,
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

    public string SpeciesName { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

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

    public string SampleDistanceText =>
        HasSampleDistance ? $"{SampleDistanceMeters:N0} m sample separation" : string.Empty;

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

    public static BiologyOrganismRowViewModel Unknown(int index, BiologyRewardThresholds? rewardThresholds = null)
    {
        BiologyRewardThresholds thresholds = rewardThresholds ?? BiologyRewardThresholds.Default;
        return new BiologyOrganismRowViewModel
        {
            DisplayName = $"Unidentified biological signal {index:N0}",
            GenusName = "Genus unknown",
            SpeciesName = $"Signal {index:N0}",
            VariantName = string.Empty,
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

public interface IBiologyDiscoveryMarkerState
{
    bool IsGlobalRegionalFirst { get; }

    bool IsCommanderFirst { get; }

    bool IsHighlightedRegionalFirst { get; }

    bool IsStandardRegionalFirst { get; }
}

public sealed class BiologyOrganismGroupViewModel : IBiologyDiscoveryMarkerState
{
    public string GenusName { get; init; } = string.Empty;

    public string GenusLabel => GenusName + ":";

    public IReadOnlyList<BiologyOrganismVariantRowViewModel> Species { get; init; } = [];

    public long MinimumReward { get; init; }

    public long MaximumReward { get; init; }

    public bool HasReward { get; init; }

    public bool IsPrediction { get; init; }

    public bool IsUnknown { get; init; }

    public bool IsCommanderFirst { get; init; }

    public bool IsRegionalFirst { get; init; }

    public bool IsGlobalRegionalFirst { get; init; }

    public bool IsHighlightedFirst { get; init; }

    public bool IsAnalyzed { get; init; }

    public bool ShouldDim { get; init; }

    public bool ShowDivider { get; init; }

    public double RewardBucketOneMillions { get; init; } = 3;

    public double RewardBucketTwoMillions { get; init; } = 7;

    public double RewardBucketThreeMillions { get; init; } = 12;

    public bool IsHighlightedRegionalFirst => IsRegionalFirst && IsHighlightedFirst;

    public bool IsStandardRegionalFirst => IsRegionalFirst && !IsHighlightedFirst;

    public string RewardText
    {
        get
        {
            if (!HasReward)
            {
                return IsUnknown ? "pending" : "reward unknown";
            }

            return MinimumReward == MaximumReward
                ? FormatCompactReward(MinimumReward)
                : $"{FormatCompactReward(MinimumReward)} – {FormatCompactReward(MaximumReward)}";
        }
    }

    public static IReadOnlyList<BiologyOrganismGroupViewModel> Create(
        IReadOnlyList<BiologyOrganismRowViewModel> organisms
    )
    {
        BiologyOrganismRowViewModel[][] groupedRows = organisms
            .Select((organism, index) => new { organism, index })
            .GroupBy(
                item => item.organism.IsUnknown ? $"unknown-{item.index:N0}" : item.organism.GenusName,
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.Select(item => item.organism).ToArray())
            .ToArray();

        return groupedRows.Select((rows, index) => Create(rows, index < groupedRows.Length - 1)).ToArray();
    }

    private static BiologyOrganismGroupViewModel Create(BiologyOrganismRowViewModel[] rows, bool showDivider)
    {
        BiologyOrganismRowViewModel first = rows[0];
        long[] rewards = rows.Where(row => row.HasReward).Select(row => row.Reward).ToArray();
        bool isGlobalRegionalFirst = rows.Any(row => row.IsGlobalRegionalFirst);
        bool isCommanderFirst = !isGlobalRegionalFirst && rows.Any(row => row.IsCommanderFirst);
        bool isRegionalFirst = !isGlobalRegionalFirst && !isCommanderFirst && rows.Any(row => row.IsRegionalFirst);

        return new BiologyOrganismGroupViewModel
        {
            GenusName = first.GenusName,
            Species = rows.Select(BiologyOrganismVariantRowViewModel.Create).ToArray(),
            MinimumReward = rewards.Length == 0 ? 0 : rewards.Min(),
            MaximumReward = rewards.Length == 0 ? 0 : rewards.Max(),
            HasReward = rewards.Length > 0,
            IsPrediction = rows.Any(row => row.IsPrediction),
            IsUnknown = rows.All(row => row.IsUnknown),
            IsCommanderFirst = isCommanderFirst,
            IsRegionalFirst = isRegionalFirst,
            IsGlobalRegionalFirst = isGlobalRegionalFirst,
            IsHighlightedFirst =
                isGlobalRegionalFirst
                || isCommanderFirst
                || isRegionalFirst && rows.Any(row => row.IsRegionalFirst && row.IsHighlightedFirst),
            IsAnalyzed = rows.All(row => !row.IsPrediction && !row.IsUnknown && row.IsAnalyzed),
            ShouldDim = rows.All(row => row.ShouldDim),
            ShowDivider = showDivider,
            RewardBucketOneMillions = first.RewardBucketOneMillions,
            RewardBucketTwoMillions = first.RewardBucketTwoMillions,
            RewardBucketThreeMillions = first.RewardBucketThreeMillions,
        };
    }

    private static string FormatCompactReward(long reward)
    {
        return reward switch
        {
            >= 1_000_000 => $"{reward / 1_000_000d:0.##} M",
            >= 1_000 => $"{reward / 1_000d:0.#} K",
            _ => $"{reward:N0}",
        };
    }
}

public sealed class BiologyOrganismVariantRowViewModel : IBiologyDiscoveryMarkerState
{
    public string SpeciesName { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    public bool IsPrediction { get; init; }

    public bool IsUnknown { get; init; }

    public bool IsCommanderFirst { get; init; }

    public bool IsRegionalFirst { get; init; }

    public bool IsGlobalRegionalFirst { get; init; }

    public bool IsHighlightedFirst { get; init; }

    public bool HasVariantColor => BiologyVariantColorConverter.Supports(VariantName);

    public bool HasPredictionMarkers => IsPrediction || IsUnknown;

    public string PredictionMarkerToolTip =>
        IsPrediction
            ? "Predicted from current body data; not yet confirmed."
            : "Biological signal cannot be identified from current data.";

    public bool IsHighlightedRegionalFirst => IsRegionalFirst && IsHighlightedFirst;

    public bool IsStandardRegionalFirst => IsRegionalFirst && !IsHighlightedFirst;

    public static BiologyOrganismVariantRowViewModel Create(BiologyOrganismRowViewModel organism)
    {
        string speciesName = organism.SpeciesName;
        if (string.IsNullOrWhiteSpace(speciesName))
        {
            speciesName = RemoveGenusPrefix(organism.DisplayName, organism.GenusName);
        }

        return new BiologyOrganismVariantRowViewModel
        {
            SpeciesName = speciesName,
            VariantName = organism.VariantName,
            IsPrediction = organism.IsPrediction,
            IsUnknown = organism.IsUnknown,
            IsCommanderFirst = organism.IsCommanderFirst,
            IsRegionalFirst = organism.IsRegionalFirst,
            IsGlobalRegionalFirst = organism.IsGlobalRegionalFirst,
            IsHighlightedFirst = organism.IsHighlightedFirst,
        };
    }

    private static string RemoveGenusPrefix(string displayName, string genusName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Unidentified";
        }

        string prefix = genusName + " ";
        string value = displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? displayName[prefix.Length..]
            : displayName;
        int variantSeparator = value.LastIndexOf(" - ", StringComparison.Ordinal);
        return (variantSeparator >= 0 ? value[..variantSeparator] : value).Trim();
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
    public IReadOnlyDictionary<
        int,
        IReadOnlyList<CanonnSurfaceBiologySignal>
    >? ConfirmedExternalBiologySignals { get; init; }
    public bool AllowRetainedCurrentBody { get; init; } = true;
    public bool ForceSystemOverview { get; init; }

    public BiologySurveyCreateOptions() { }

    public BiologySurveyCreateOptions(
        bool drawBodyBiosOnlyWhenNear,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions,
        BiologyDiscoveryContext? discoveryContext,
        BiologyRewardThresholds? rewardThresholds
    )
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
    public IReadOnlyDictionary<
        int,
        IReadOnlyList<CanonnSurfaceBiologySignal>
    >? ConfirmedExternalBiologySignals { get; init; }

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
    public IReadOnlyDictionary<
        int,
        IReadOnlyList<CanonnSurfaceBiologySignal>
    >? ConfirmedExternalBiologySignals { get; init; }

    public BiologySurveyBodyDetailOptions(
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount,
        bool disablePredictions
    )
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
    public BiologyDiscoveryContext DiscoveryContext { get; init; } = BiologyDiscoveryContext.Unavailable;
    public bool DisablePredictions { get; init; }
    public int RadicoidaUnicaCount { get; init; }
    public BiologyRewardThresholds RewardThresholds { get; init; } = BiologyRewardThresholds.Default;
    public BiologyPredictionEvaluator PredictionEvaluator { get; init; } = null!;
    public ExobiologyReferenceCatalog ReferenceCatalog { get; init; } = null!;
    public IReadOnlySet<int>? CanonnBiologyBodyIds { get; init; }
}

public sealed class BiologySurveyBodyBuildOptions
{
    public bool HighlightRegionalFirsts { get; init; }
    public bool DimAnalyzedOrganisms { get; init; }
    public bool HideGeoCount { get; init; }
    public bool DisablePredictions { get; init; }
    public BiologyDiscoveryContext DiscoveryContext { get; init; } = BiologyDiscoveryContext.Unavailable;
    public BiologyRewardThresholds RewardThresholds { get; init; } = BiologyRewardThresholds.Default;
    public BiologyPredictionEvaluator PredictionEvaluator { get; init; } = null!;
    public ExobiologyReferenceCatalog ReferenceCatalog { get; init; } = null!;
}

public sealed record BiologyDiscoveryContext(
    long SystemAddress,
    CommanderCodexData? Global,
    CommanderCodexData? Regional,
    int? RegionId,
    RegionalCodexCandidateCatalog GlobalRegionalCandidates
)
{
    public static BiologyDiscoveryContext Unavailable { get; } =
        new(0, null, null, null, RegionalCodexCandidateCatalog.Empty);

    public bool IsCommanderNew(long entryId) => Global is not null && !Global.IsDiscovered(entryId);

    public bool IsPersonalFirst(long entryId, int bodyId) =>
        Global is not null && Global.IsPersonalFirst(entryId, SystemAddress, bodyId);

    public bool IsRegionalNew(long entryId) => Regional is not null && !Regional.IsDiscovered(entryId);

    public bool IsGlobalRegionalNew(long entryId) => GlobalRegionalCandidates.IsCandidate(RegionId, entryId);
}
