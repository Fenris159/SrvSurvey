using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record OverlayPositionPreviewViewModel(
    OverlayLayoutDefinition Definition,
    IReadOnlyList<OverlayPositionPreviewRowViewModel> Rows,
    bool ShowFooter)
{
    private static readonly IReadOnlyDictionary<
        OverlayLayoutCategory,
        IReadOnlyList<OverlayPositionPreviewRowViewModel>> CategoryRows =
        new Dictionary<
            OverlayLayoutCategory,
            IReadOnlyList<OverlayPositionPreviewRowViewModel>>
        {
            [OverlayLayoutCategory.ExplorationAndNavigation] =
            [
                new("System", "Example AA-A h42"),
                new("Scan progress", "18 / 24 bodies", 75),
                new("Signals", "3 biological • 2 geological"),
            ],
            [OverlayLayoutCategory.BiologyAndSurface] =
            [
                new("Species", "Bacterium Acies"),
                new("Sample progress", "2 of 3", 67),
                new("Distance", "146 m"),
            ],
            [OverlayLayoutCategory.SitesAndQuests] =
            [
                new("Site", "Guardian structure"),
                new("Survey progress", "7 / 12", 58),
                new("Objective", "Scan active obelisks"),
            ],
            [OverlayLayoutCategory.CombatAndColonization] =
            [
                new("Location", "Construction site"),
                new("Progress", "68%", 68),
                new("Remaining", "Steel • 2,450 t"),
            ],
            [OverlayLayoutCategory.StatusAndUtilities] =
            [
                new("Status", "Monitoring"),
                new("Activity", "Journal event received"),
                new("Updated", "Just now"),
            ],
        };

    public string Title => Definition.DisplayName;

    public bool HasRows => Rows.Count > 0;

    public static OverlayPositionPreviewViewModel Create(
        OverlayLayoutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var availableRows = Math.Clamp(
            (definition.PreviewSize.Height - 42) / 24,
            0,
            CategoryRows[definition.Category].Count);
        if (definition.PreviewSize.Width < 150)
        {
            availableRows = Math.Min(availableRows, 1);
        }

        return new OverlayPositionPreviewViewModel(
            definition,
            CategoryRows[definition.Category].Take(availableRows).ToArray(),
            definition.PreviewSize.Height >= 70
                && definition.PreviewSize.Width >= 120);
    }
}

public sealed record OverlayPositionPreviewRowViewModel(
    string Label,
    string Value,
    double? Progress = null)
{
    public bool HasProgress => Progress is not null;
}
