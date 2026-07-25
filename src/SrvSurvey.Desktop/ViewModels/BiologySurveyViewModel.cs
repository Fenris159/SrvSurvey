using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record BiologySurveyViewModel(
    BiologySurveyMode Mode,
    string Heading,
    string ProgressText,
    IReadOnlyList<BiologyBodyRowViewModel> Bodies,
    IReadOnlyList<BiologyOrganismRowViewModel> Organisms,
    string RewardSummary,
    string FirstFootfallRewardSummary,
    bool RequiresDss,
    int GeologicalSignalCount,
    IReadOnlyList<string> GeologicalSignals)
{
    public bool IsBodyDetail => Mode == BiologySurveyMode.Body;

    public bool IsSystemOverview => Mode == BiologySurveyMode.System;

    public bool HasBodies => Bodies.Count > 0;

    public bool HasOrganisms => Organisms.Count > 0;

    public bool HasRewardSummary => !string.IsNullOrWhiteSpace(RewardSummary);

    public bool HasFirstFootfallRewardSummary => !string.IsNullOrWhiteSpace(
        FirstFootfallRewardSummary);

    public bool HasGeologicalSignals => GeologicalSignalCount > 0;

    public static BiologySurveyViewModel? Create(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        ExobiologySnapshot exobiology,
        bool drawBodyBiosOnlyWhenNear,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount)
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
            drawBodyBiosOnlyWhenNear);
        return body is null
            ? CreateSystem(snapshot, status, biologicalBodies)
            : CreateBody(
                body,
                exobiology,
                highlightRegionalFirsts,
                dimAnalyzedOrganisms,
                hideGeoCount);
    }

    private static BiologySurveyViewModel CreateSystem(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies)
    {
        var destinationBodyId = status?.Destination is { } destination
            && destination.System == snapshot.SystemAddress
            ? destination.Body
            : (int?)null;
        var currentBodyId = ResolveCurrentBody(snapshot, status)?.BodyId;
        var rows = biologicalBodies
            .Select(body =>
            {
                var knownReward = body.Organisms.Sum(
                    organism => organism.Reward ?? 0);
                return new BiologyBodyRowViewModel(
                    body.ShortName,
                    body.AnalyzedBiologicalSignalCount,
                    body.BiologicalSignalCount,
                    knownReward,
                    body.Organisms.Count(organism => organism.Reward is not null)
                        < body.BiologicalSignalCount,
                    body.BodyId == destinationBodyId,
                    body.BodyId == currentBodyId);
            })
            .ToArray();
        var analyzed = biologicalBodies.Sum(
            body => body.AnalyzedBiologicalSignalCount);
        var total = biologicalBodies.Sum(body => body.BiologicalSignalCount);
        var knownSystemReward = rows.Sum(row => row.KnownReward);
        var hasUnknownReward = rows.Any(row => row.HasUnknownReward);

        return new BiologySurveyViewModel(
            BiologySurveyMode.System,
            snapshot.SystemName ?? "Current system",
            $"{analyzed:N0} of {total:N0} biological signals analyzed",
            rows,
            [],
            FormatKnownReward(knownSystemReward, hasUnknownReward),
            string.Empty,
            false,
            0,
            []);
    }

    private static BiologySurveyViewModel CreateBody(
        SystemScanBodySnapshot body,
        ExobiologySnapshot exobiology,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms,
        bool hideGeoCount)
    {
        var organisms = body.Organisms
            .Select(organism => CreateOrganism(
                body,
                organism,
                exobiology,
                highlightRegionalFirsts,
                dimAnalyzedOrganisms))
            .ToList();
        while (organisms.Count < body.BiologicalSignalCount)
        {
            organisms.Add(BiologyOrganismRowViewModel.Unknown(
                organisms.Count + 1));
        }

        var knownReward = body.Organisms.Sum(
            organism => organism.Reward ?? 0);
        var hasUnknownReward = body.Organisms.Count(
            organism => organism.Reward is not null)
            < body.BiologicalSignalCount;
        var geoCount = hideGeoCount ? 0 : body.GeologicalSignalCount;
        var geoSignals = hideGeoCount
            ? []
            : body.AnalyzedGeologicalSignals;

        return new BiologySurveyViewModel(
            BiologySurveyMode.Body,
            $"{body.Name} biology",
            body.BiologicalSignalCount == 1
                ? "1 biological signal"
                : $"{body.BiologicalSignalCount:N0} biological signals",
            [],
            organisms,
            FormatKnownReward(knownReward, hasUnknownReward),
            body.IsFirstFootfall && knownReward > 0
                ? $"First-footfall value: {FormatCredits(knownReward * 5)}"
                : string.Empty,
            body.Organisms.Count == 0 && !body.IsDssComplete,
            geoCount,
            geoSignals);
    }

    private static BiologyOrganismRowViewModel CreateOrganism(
        SystemScanBodySnapshot body,
        SystemOrganismSnapshot organism,
        ExobiologySnapshot exobiology,
        bool highlightRegionalFirsts,
        bool dimAnalyzedOrganisms)
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

        return new BiologyOrganismRowViewModel(
            displayName,
            genusName,
            organism.Reward ?? 0,
            organism.Reward is not null,
            organism.IsAnalyzed,
            organism.IsRegionalFirst,
            highlightRegionalFirsts && organism.IsRegionalFirst,
            activeSample,
            organism.Variant is null,
            false,
            dimAnalyzedOrganisms && organism.IsAnalyzed);
    }

    private static SystemScanBodySnapshot? ResolveBody(
        SystemScanSnapshot snapshot,
        EliteStatus? status,
        IReadOnlyList<SystemScanBodySnapshot> biologicalBodies,
        bool drawBodyBiosOnlyWhenNear)
    {
        if (status?.GuiFocus is GuiFocus.ExternalPanel
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

        var current = ResolveCurrentBody(snapshot, status);
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
}

public enum BiologySurveyMode
{
    System,
    Body,
}

public sealed record BiologyBodyRowViewModel(
    string Name,
    int AnalyzedSignalCount,
    int SignalCount,
    long KnownReward,
    bool HasUnknownReward,
    bool IsDestination,
    bool IsCurrentBody)
{
    public string ProgressText => $"{AnalyzedSignalCount:N0}/{SignalCount:N0}";

    public bool IsComplete => SignalCount > 0
        && AnalyzedSignalCount >= SignalCount;

    public string RewardText => KnownReward <= 0
        ? ""
        : HasUnknownReward
            ? $"{KnownReward / 1_000_000d:N2} M+ CR"
            : $"{KnownReward / 1_000_000d:N2} M CR";

    public bool HasReward => KnownReward > 0;
}

public sealed record BiologyOrganismRowViewModel(
    string DisplayName,
    string GenusName,
    long Reward,
    bool HasReward,
    bool IsAnalyzed,
    bool IsRegionalFirst,
    bool IsHighlightedFirst,
    bool IsCurrentSample,
    bool IsPrediction,
    bool IsUnknown,
    bool ShouldDim)
{
    public string RewardText => HasReward
        ? Reward >= 1_000_000
            ? $"{Reward / 1_000_000d:N2} M CR"
            : $"{Reward:N0} CR"
        : IsPrediction
            ? "Prediction pending"
            : "Unidentified";

    public static BiologyOrganismRowViewModel Unknown(int index)
    {
        return new BiologyOrganismRowViewModel(
            $"Unidentified biological signal {index:N0}",
            "Genus unknown",
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            false);
    }
}
