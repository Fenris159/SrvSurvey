using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationProjectPreviewViewModel
{
    private readonly Func<CancellationToken, Task<ColonizationNexus?>>? readNexus;
    private ColonizationNexus? nexusSnapshot;

    /// <summary>Reuses the combined report presentation while refreshing authenticated Nexus system requirements.</summary>
    public ColonizationProjectPreviewViewModel(
        Func<CancellationToken, Task<ColonizationNexus?>> readNexus,
        string? systemName,
        Func<bool> isEnabled,
        Func<int> currentShipCapacity
    )
        : this(null, () => [], true, isEnabled, currentShipCapacity, null)
    {
        this.readNexus = readNexus ?? throw new ArgumentNullException(nameof(readNexus));
        selectedSystem = systemName is null ? AllSystems : new(systemName, systemName);
    }

    private async Task RefreshNexusReportAsync()
    {
        ColonizationNexus? nexus = await readNexus!(lifetimeToken);
        if (disposed)
        {
            return;
        }
        ColonizationNexus candidate = nexus ?? throw new InvalidDataException("This Nexus is no longer available.");
        DateTimeOffset candidateTime = DateTimeOffset.UtcNow;
        string? scope = candidate.Systems.Any(system =>
            string.Equals(system.Name, selectedSystem.SystemName, StringComparison.OrdinalIgnoreCase)
        )
            ? selectedSystem.SystemName
            : null;
        var report = ColonizationProjectPreview.CreateNexusReport(candidate, scope, candidateTime);
        nexusSnapshot = candidate;
        fetchedAt = candidateTime;
        refreshFailed = false;
        UpdateSystemOptions();
        ApplyReport(report);
    }
}
