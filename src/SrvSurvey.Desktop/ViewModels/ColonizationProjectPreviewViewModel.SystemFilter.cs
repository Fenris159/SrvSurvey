using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationProjectPreviewViewModel
{
    private static readonly PreviewSystemOption AllSystems = new(null, "All Systems");
    private IReadOnlyList<ColonizationProjectPreviewData> reportData = [];
    private DateTimeOffset fetchedAt;
    private PreviewSystemOption selectedSystem = AllSystems;
    private bool refreshFailed;

    /// <summary>Lists systems from the entire successful report, independently of the current filter.</summary>
    public IReadOnlyList<PreviewSystemOption> SystemOptions { get; private set; } = [AllSystems];

    /// <summary>Rebuilds every report section from cached project data without requesting Raven again.</summary>
    public PreviewSystemOption? SelectedSystem
    {
        get => selectedSystem;
        set
        {
            // Replacing ComboBox items can briefly clear selection; retain the reconciled report filter.
            if (value is null || disposed || !isCombined || !SystemOptions.Contains(value) || selectedSystem == value)
            {
                return;
            }
            selectedSystem = value;
            ApplyReport();
            Notify();
        }
    }

    /// <summary>Preserves a system selection across refreshed membership and returns to All Systems if it disappears.</summary>
    private void UpdateSystemOptions()
    {
        if (!isCombined)
        {
            return;
        }
        SystemOptions =
        [
            AllSystems,
            .. reportData
                .Select(data => data.Project.SystemName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new PreviewSystemOption(name, name.Length == 0 ? "Unknown system" : name)),
        ];
        selectedSystem =
            SystemOptions.FirstOrDefault(option =>
                string.Equals(option.SystemName, selectedSystem.SystemName, StringComparison.OrdinalIgnoreCase)
            ) ?? AllSystems;
    }

    /// <summary>Aggregates only the chosen system's builds, retaining shared-carrier deduplication and the original fetch time.</summary>
    private void ApplyReport()
    {
        Snapshot = isCombined
            ? ColonizationProjectPreview.CreateCombined(
                reportData.Where(data =>
                    selectedSystem.SystemName is null
                    || string.Equals(
                        data.Project.SystemName.Trim(),
                        selectedSystem.SystemName,
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                fetchedAt
            )
            : new ColonizationProjectPreview(reportData[0], fetchedAt);
        ApplySnapshot(Snapshot);
        Status = refreshFailed
            ? "Refresh failed. Showing the last successful snapshot."
            : (Snapshot.CarrierDeficit, Snapshot.Statistics) switch
            {
                (null, _) => "Carrier cargo unavailable. Shortages and trips cannot be confirmed.",
                (_, null) => "Cargo updated. Delivery history unavailable.",
                _ => "Live Raven data · read-only preview",
            };
    }
}

/// <summary>Separates the All Systems choice from a real system name while providing a readable dropdown label.</summary>
public sealed record PreviewSystemOption(string? SystemName, string Label);
