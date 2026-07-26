using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record BiologyStatusViewModel(
    int BodyId,
    string BodyName,
    int AnalyzedSignalCount,
    int SignalCount,
    IReadOnlyList<BiologyStatusSignalViewModel> Signals,
    BiologyActiveSampleViewModel? ActiveSample,
    BiologyCodexNotificationViewModel? CodexNotification,
    bool RequiresDss,
    string Warning,
    string Footer,
    BiologyTemperatureRangeViewModel? TemperatureRange)
{
    private static readonly Lazy<BiologyPredictionEvaluator>
        DefaultPredictionEvaluator = new(() => new BiologyPredictionEvaluator(
            BiologyCriteriaCatalog.LoadEmbedded()));

    public bool HasSignals => Signals.Count > 0;

    public bool HasActiveSample => ActiveSample is not null;

    public bool HasCodexNotification => CodexNotification is not null;

    public bool ShowSignalSummary => HasSignals && !HasActiveSample;

    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);

    public bool HasFooter => !string.IsNullOrWhiteSpace(Footer);

    public bool HasTemperatureRange => TemperatureRange is not null;

    public string ProgressText =>
        $"{AnalyzedSignalCount:N0} of {SignalCount:N0} analyzed";

    public double CompletionPercent => SignalCount <= 0
        ? 0
        : Math.Clamp(
            AnalyzedSignalCount * 100d / SignalCount,
            0,
            100);

    public double TrackedCompletionPercent => SignalCount <= 0
        ? 0
        : Math.Clamp(
            (AnalyzedSignalCount + (HasActiveSample ? 1 : 0))
                * 100d / SignalCount,
            0,
            100);

    public static BiologyStatusViewModel? Create(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        ExobiologySnapshot exobiology,
        bool hideGeologicalSignals,
        BiologyCodexNotificationViewModel? codexNotification = null,
        bool showTemperatureRangeDebug = false,
        BiologyPredictionEvaluator? predictionEvaluator = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exobiology);
        var body = ResolveCurrentBody(snapshot, status);
        if (body is null || body.BiologicalSignalCount <= 0)
        {
            return null;
        }

        var activeScan = exobiology.ScanOne;
        var activeScanIsLocal = activeScan is not null
            && (string.IsNullOrWhiteSpace(activeScan.Body)
                || string.Equals(
                    activeScan.Body,
                    body.Name,
                    StringComparison.OrdinalIgnoreCase));
        var activeOrganism = activeScanIsLocal
            ? body.Organisms.FirstOrDefault(organism =>
                string.Equals(
                    organism.Species,
                    activeScan!.Species,
                    StringComparison.Ordinal)
                || string.Equals(
                    organism.Genus,
                    activeScan.Genus,
                    StringComparison.Ordinal))
            : null;
        var signals = CreateSignals(
            body,
            activeOrganism,
            hideGeologicalSignals);
        var activeSample = activeScanIsLocal && activeScan is not null
            ? CreateActiveSample(
                body,
                activeOrganism,
                exobiology,
                status)
            : null;
        var warning = !activeScanIsLocal && activeScan is not null
            ? "Incomplete "
                + FormatJournalName(activeScan.Genus)
                + " samples remain on "
                + (activeScan.Body ?? "another body")
                + "."
            : string.Empty;
        var allAnalyzed = body.AnalyzedBiologicalSignalCount
            >= body.BiologicalSignalCount;
        var currentNotification = codexNotification?.BodyId == body.BodyId
            ? codexNotification
            : null;
        var footer = activeSample is not null || !string.IsNullOrEmpty(warning)
            ? string.Empty
            : allAnalyzed && body.IsFirstFootfall
                ? "All signals analyzed with the first-footfall bonus applied."
                : allAnalyzed
                    ? "All biological signals analyzed."
                    : currentNotification is not null
                        ? currentNotification.SummaryText
                    : body.Organisms.Count == 0
                        ? string.Empty
                        : body.IsFirstFootfall
                            ? "First-footfall rewards apply to analyzed organisms."
                            : "Use the Composition Scanner to identify organisms.";

        return new BiologyStatusViewModel(
            body.BodyId,
            body.ShortName,
            body.AnalyzedBiologicalSignalCount,
            body.BiologicalSignalCount,
            signals,
            activeSample,
            currentNotification,
            body.Organisms.Count == 0,
            warning,
            footer,
            showTemperatureRangeDebug
                ? CreateTemperatureRange(
                    snapshot,
                    body,
                    activeOrganism,
                    status,
                    predictionEvaluator ?? DefaultPredictionEvaluator.Value)
                : null);
    }

    private static BiologyTemperatureRangeViewModel CreateTemperatureRange(
        SystemScanSnapshot snapshot,
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot? organism,
        EliteStatus? status,
        BiologyPredictionEvaluator predictionEvaluator)
    {
        BiologyCriteriaClause? temperatureClause = null;
        var targetName = organism?.VariantLocalized
            ?? organism?.SpeciesLocalized;
        var inputs = BiologyPredictionContextBuilder.Build(
            snapshot,
            body.BodyId);
        if (inputs is not null && !string.IsNullOrWhiteSpace(targetName))
        {
            temperatureClause = predictionEvaluator.Evaluate(
                    inputs.Context,
                    inputs.Knowledge,
                    targetName)
                .TargetClauses
                .LastOrDefault(clause => clause.Property == "temp"
                    && clause.Operator == BiologyCriteriaOperator.Range);
        }

        return new BiologyTemperatureRangeViewModel(
            body.SurfaceTemperature,
            status?.Temperature is > 0 ? status.Temperature : null,
            temperatureClause?.Minimum,
            temperatureClause?.Maximum);
    }

    private static IReadOnlyList<BiologyStatusSignalViewModel> CreateSignals(
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot? activeOrganism,
        bool hideGeologicalSignals)
    {
        var signals = body.Organisms
            .Select(organism =>
            {
                var name = organism.GenusLocalized
                    ?? FormatJournalName(organism.Genus);
                var distance = organism.IsAnalyzed
                    ? string.Empty
                    : ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                        organism.GenusLocalized ?? organism.Genus) is var meters
                        && meters > 0
                            ? $"{meters:N0} m"
                            : string.Empty;
                return new BiologyStatusSignalViewModel(
                    name,
                    distance,
                    organism.IsAnalyzed,
                    ReferenceEquals(organism, activeOrganism),
                    false);
            })
            .ToList();
        if (!hideGeologicalSignals)
        {
            for (var index = 0; index < body.GeologicalSignalCount; index++)
            {
                var analyzed = index < body.AnalyzedGeologicalSignals.Count;
                signals.Add(new BiologyStatusSignalViewModel(
                    analyzed
                        ? body.AnalyzedGeologicalSignals[index]
                        : $"Geo #{index + 1:N0}",
                    string.Empty,
                    analyzed,
                    false,
                    true));
            }
        }

        return signals;
    }

    private static BiologyActiveSampleViewModel CreateActiveSample(
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot? organism,
        ExobiologySnapshot exobiology,
        EliteStatus? status)
    {
        var scan = exobiology.ScanTwo ?? exobiology.ScanOne!;
        var stage = exobiology.ScanTwo is null ? 1 : 2;
        var requiredDistance = scan.Radius;
        var nearestDistance = CalculateNearestDistance(
            body.Name,
            exobiology,
            status);
        var remainingDistance = nearestDistance is null
            ? (double?)null
            : Math.Max(0, requiredDistance - nearestDistance.Value);
        var reward = organism?.Reward ?? 0;
        if (body.IsFirstFootfall)
        {
            reward *= 5;
        }

        return new BiologyActiveSampleViewModel(
            organism?.VariantLocalized
                ?? organism?.SpeciesLocalized
                ?? organism?.GenusLocalized
                ?? FormatJournalName(scan.Species),
            stage,
            requiredDistance,
            nearestDistance,
            remainingDistance,
            reward,
            body.IsFirstFootfall);
    }

    private static double? CalculateNearestDistance(
        string bodyName,
        ExobiologySnapshot exobiology,
        EliteStatus? status)
    {
        if (status?.HasLatitudeLongitude != true || status.PlanetRadius <= 0)
        {
            return null;
        }

        var current = new SurfaceCoordinate(status.Latitude, status.Longitude);
        var samples = new[] { exobiology.ScanOne, exobiology.ScanTwo }
            .Where(sample => sample is not null
                && (string.IsNullOrWhiteSpace(sample.Body)
                    || string.Equals(
                        sample.Body,
                        bodyName,
                        StringComparison.OrdinalIgnoreCase)))
            .Cast<BioSampleSnapshot>()
            .ToArray();
        try
        {
            return samples.Length == 0
                ? null
                : samples.Min(sample => SurfaceNavigation.GetDistance(
                    new SurfaceCoordinate(
                        sample.Location.Latitude,
                        sample.Location.Longitude),
                    current,
                    (double)status.PlanetRadius));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static SystemScanBodySnapshot? ResolveCurrentBody(
        SystemScanSnapshot snapshot,
        EliteStatus? status)
    {
        var byName = !string.IsNullOrWhiteSpace(status?.BodyName)
            ? snapshot.Bodies.FirstOrDefault(body => string.Equals(
                body.Name,
                status.BodyName,
                StringComparison.OrdinalIgnoreCase))
            : null;
        return byName ?? (snapshot.CurrentBodyId is { } bodyId
            ? snapshot.Bodies.FirstOrDefault(body => body.BodyId == bodyId)
            : null);
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
}

public sealed record BiologyCodexNotificationViewModel(
    long EntryId,
    int BodyId,
    string DisplayName,
    long Reward,
    bool IsFirstFootfall,
    bool HasImage)
{
    public string SummaryText => DisplayName + " · " + FormatCredits(Reward)
        + (IsFirstFootfall ? " · FF bonus" : string.Empty);

    public string ActionText => HasImage
        ? "Reference image available · type .show"
        : "Codex details available · type .show";

    private static string FormatCredits(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:N2} M CR",
            >= 1_000 => $"{value / 1_000d:N1} K CR",
            _ => $"{value:N0} CR",
        };
    }
}

public sealed record BiologyStatusSignalViewModel(
    string Name,
    string Detail,
    bool IsAnalyzed,
    bool IsActive,
    bool IsGeological)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string DisplayText => HasDetail ? $"{Name} · {Detail}" : Name;

    public double RowOpacity => IsAnalyzed ? 0.52 : 1;
}

public sealed record BiologyActiveSampleViewModel(
    string DisplayName,
    int Stage,
    double RequiredDistanceMeters,
    double? NearestDistanceMeters,
    double? RemainingDistanceMeters,
    long Reward,
    bool IsFirstFootfall)
{
    public bool IsFirstSampleComplete => Stage >= 1;

    public bool IsSecondSampleComplete => Stage >= 2;

    public string StageText => $"Sample {Stage:N0} of 3 captured";

    public bool HasReward => Reward > 0;

    public string RewardText => HasReward
        ? FormatCredits(Reward) + (IsFirstFootfall ? " · FF bonus" : string.Empty)
        : string.Empty;

    public string RequiredDistanceText =>
        $"{RequiredDistanceMeters:N0} m sample separation";

    public double SeparationPercent => NearestDistanceMeters is null
        || RequiredDistanceMeters <= 0
            ? 0
            : Math.Clamp(
                NearestDistanceMeters.Value * 100 / RequiredDistanceMeters,
                0,
                100);

    public bool IsSeparationReady => RemainingDistanceMeters is <= 0;

    public string DistanceText => NearestDistanceMeters is null
        ? $"Move {RequiredDistanceMeters:N0} m from a prior sample."
        : IsSeparationReady
            ? $"{NearestDistanceMeters:N0} m from the nearest sample · separation reached"
            : $"{NearestDistanceMeters:N0} m from the nearest sample · "
                + $"{RemainingDistanceMeters:N0} m remaining";

    private static string FormatCredits(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:N2} M CR",
            >= 1_000 => $"{value / 1_000d:N1} K CR",
            _ => $"{value:N0} CR",
        };
    }
}

public sealed record BiologyTemperatureRangeViewModel(
    double BodyTemperature,
    double? LiveTemperature,
    double? Minimum,
    double? Maximum)
{
    public bool HasLiveTemperature => LiveTemperature is not null;

    public bool HasExpectedRange => Minimum is not null || Maximum is not null;

    public string BodyTemperatureText => $"Body baseline {BodyTemperature:N1} K";

    public string LiveTemperatureText => LiveTemperature is { } value
        ? $"Live suit temperature {value:N1} K"
        : "Live suit temperature unavailable";

    public string ExpectedRangeText => (Minimum, Maximum) switch
    {
        ({ } minimum, { } maximum) =>
            $"Expected organism range {minimum:N1} to {maximum:N1} K",
        ({ } minimum, null) => $"Expected organism minimum {minimum:N1} K",
        (null, { } maximum) => $"Expected organism maximum {maximum:N1} K",
        _ => "No organism-specific temperature range was resolved.",
    };

    public double BodyPositionPercent => CalculatePosition(BodyTemperature);

    public double LivePositionPercent => LiveTemperature is { } value
        ? CalculatePosition(value)
        : 0;

    private double CalculatePosition(double value)
    {
        if (Minimum is { } minimum
            && Maximum is { } maximum
            && maximum > minimum)
        {
            return Math.Clamp(
                (value - minimum) * 100d / (maximum - minimum),
                0,
                100);
        }

        return Math.Clamp(
            50d + (value - BodyTemperature) * 2.5d,
            0,
            100);
    }
}
