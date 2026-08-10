using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayPositionEditSession
{
    private readonly Dictionary<string, LegacyOverlayPlacement> original;
    private readonly double? originalDefaultOpacity;
    private readonly LegacyOverlayLayout workingLayout;
    private double? workingDefaultOpacity;

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
        originalDefaultOpacity = activeLayout.DefaultOpacity;
        workingDefaultOpacity = originalDefaultOpacity;
        workingLayout = new LegacyOverlayLayout(
            placements,
            activeLayout.DefaultOpacity,
            null);
        workingLayout.SetScaleIndex(activeLayout.ScaleIndex);
    }

    public bool HasChanges => Changes.Count > 0 || HasDefaultOpacityChange;

    public bool HasDefaultOpacityChange =>
        !EquivalentOpacity(workingDefaultOpacity, originalDefaultOpacity);

    public double DefaultOpacity => workingDefaultOpacity ?? 1d;

    public int ScaleIndex => workingLayout.ScaleIndex;

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

    public LegacyOverlayPlacement GetOriginalPlacement(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        return original.TryGetValue(plotterName, out var placement)
            ? placement
            : throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                $"Overlay '{plotterName}' is not supported by the position editor.");
    }

    public double GetOpacity(string plotterName) =>
        GetPlacement(plotterName).Opacity ?? DefaultOpacity;

    public int GetScaleIndex(string plotterName) =>
        GetPlacement(plotterName).ScaleIndex ?? ScaleIndex;

    public bool SetDefaultOpacity(double opacity)
    {
        ValidateOpacity(opacity, nameof(opacity));
        double? normalized = originalDefaultOpacity is null
            && Math.Abs(opacity - 1d) < 0.0001d
                ? null
                : opacity;
        if (EquivalentOpacity(workingDefaultOpacity, normalized))
        {
            return false;
        }

        workingDefaultOpacity = normalized;
        return true;
    }

    public bool SetOpacityOverride(string plotterName, double? opacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (opacity is not null)
        {
            ValidateOpacity(opacity.Value, nameof(opacity));
        }

        var placement = GetPlacement(plotterName);
        return workingLayout.SetPlacement(
            plotterName,
            placement with { Opacity = opacity });
    }

    public bool SetScaleOverride(string plotterName, int? scaleIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (scaleIndex is { } value && !OverlayScaleCatalog.IsSupported(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleIndex),
                $"Overlay scale index {value} is not supported.");
        }

        var placement = GetPlacement(plotterName);
        return workingLayout.SetPlacement(
            plotterName,
            placement with { ScaleIndex = scaleIndex });
    }

    public bool SetPlacement(
        string plotterName,
        LegacyOverlayPlacement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(placement);
        _ = GetPlacement(plotterName);
        return workingLayout.SetPlacement(plotterName, placement);
    }

    public bool MoveWithDefaultAnchors(
        string plotterName,
        PixelPoint position,
        PixelSize previewSize,
        PixelRect hostBounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var placement = GetPlacement(plotterName);
        var definition = OverlayLayoutCatalog.GetRequired(plotterName);
        var defaults = definition.DefaultPlacement;
        var reanchored = placement with
        {
            Horizontal = defaults.Horizontal,
            Vertical = definition.MoveVerticalAnchor,
        };
        var centered = OverlayInteractionViewModel.CreatePlacement(
            reanchored,
            position,
            previewSize,
            hostBounds);
        return workingLayout.SetPlacement(plotterName, centered);
    }

    public void SetScaleIndex(int index)
    {
        workingLayout.SetScaleIndex(index);
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
        return MoveWithDefaultAnchors(
            plotterName,
            position,
            previewSize,
            hostBounds);
    }

    private static void ValidateOpacity(double opacity, string parameterName)
    {
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Overlay opacity must be from 0 to 1.");
        }
    }

    private static bool EquivalentOpacity(double? left, double? right)
    {
        if (left.HasValue != right.HasValue)
        {
            return false;
        }

        return !left.HasValue
            || Math.Abs(left.Value - right!.Value) <= 0.0001d;
    }
}
