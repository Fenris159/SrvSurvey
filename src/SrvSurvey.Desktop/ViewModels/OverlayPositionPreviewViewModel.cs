using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record OverlayPositionPreviewViewModel(
    OverlayLayoutDefinition Definition,
    string Subtitle,
    string Context,
    IReadOnlyList<OverlayPositionPreviewRowViewModel> Rows,
    string Footer,
    string CompactText,
    bool IsCompact,
    bool ShowSubtitle,
    bool ShowFooter)
{
    public string Title => Definition.DisplayName;

    public bool HasRows => Rows.Count > 0;

    public static OverlayPositionPreviewViewModel Create(
        OverlayLayoutDefinition definition)
    {
        return Create(definition, OverlayPreviewSimulationState.Default);
    }

    internal static OverlayPositionPreviewViewModel Create(
        OverlayLayoutDefinition definition,
        OverlayPreviewSimulationState simulation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(simulation);
        var content = OverlayPreviewSimulationProjector.Project(
            definition,
            simulation);
        var isCompact = definition.PreviewSize.Height < 50;
        var showSubtitle = !isCompact
            && definition.PreviewSize.Height >= 140
            && definition.PreviewSize.Width >= 150;
        var showFooter = !isCompact
            && definition.PreviewSize.Height >= 70
            && definition.PreviewSize.Width >= 120;
        var reservedHeight = 42
            + (showSubtitle ? 24 : 0)
            + (showFooter ? 14 : 0);
        var availableRows = Math.Clamp(
            (definition.PreviewSize.Height - reservedHeight) / 24,
            0,
            content.Rows.Count);
        if (definition.PreviewSize.Width < 150)
        {
            availableRows = Math.Min(availableRows, 1);
        }

        return new OverlayPositionPreviewViewModel(
            definition,
            content.Subtitle,
            content.Context,
            content.Rows.Take(availableRows).ToArray(),
            content.Footer,
            string.IsNullOrWhiteSpace(content.CompactText)
                ? content.Footer
                : content.CompactText,
            isCompact,
            showSubtitle,
            showFooter);
    }
}

public sealed record OverlayPositionPreviewRowViewModel(
    string Label,
    string Value,
    double? Progress = null)
{
    public bool HasProgress => Progress is not null;
}
