using Avalonia;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayPositionEditSession
{
    private readonly IReadOnlyDictionary<string, LegacyOverlayPlacement> original;
    private readonly LegacyOverlayLayout workingLayout;

    public OverlayPositionEditSession(LegacyOverlayLayout activeLayout)
    {
        ArgumentNullException.ThrowIfNull(activeLayout);
        var placements = OverlayLayoutCatalog.Supported.ToDictionary(
            definition => definition.Name,
            definition => activeLayout.Placements.GetValueOrDefault(
                definition.Name,
                definition.DefaultPlacement),
            StringComparer.Ordinal);
        original = new Dictionary<string, LegacyOverlayPlacement>(
            placements,
            StringComparer.Ordinal);
        workingLayout = new LegacyOverlayLayout(
            placements,
            activeLayout.DefaultOpacity,
            null);
        workingLayout.SetScaleIndex(activeLayout.ScaleIndex);
    }

    public bool HasChanges => Changes.Count > 0;

    public IReadOnlyDictionary<string, LegacyOverlayPlacement> Changes =>
        workingLayout.Placements
            .Where(entry => original.GetValueOrDefault(entry.Key) != entry.Value)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);

    public LegacyOverlayPlacement GetPlacement(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        return workingLayout.Placements.TryGetValue(plotterName, out var placement)
            ? placement
            : throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                $"Overlay '{plotterName}' is not supported by the position editor.");
    }

    public PixelPoint GetPosition(
        string plotterName,
        PixelRect hostBounds,
        PixelSize previewSize)
    {
        return workingLayout.GetPosition(plotterName, hostBounds, previewSize)
            ?? throw new InvalidOperationException(
                $"Overlay '{plotterName}' has no working position.");
    }

    public bool Move(
        string plotterName,
        PixelPoint position,
        PixelSize previewSize,
        PixelRect hostBounds)
    {
        var placement = OverlayInteractionViewModel.CreatePlacement(
            GetPlacement(plotterName),
            position,
            previewSize,
            hostBounds);
        return workingLayout.SetPlacement(plotterName, placement);
    }
}
