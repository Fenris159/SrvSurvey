using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayPositionEditSession
{
    private readonly Dictionary<string, LegacyOverlayPlacement> original;
    private readonly double? originalDefaultOpacity;
    private readonly int originalScaleIndex;
    private readonly LegacyOverlayLayout workingLayout;
    private double? workingDefaultOpacity;

    public OverlayPositionEditSession(LegacyOverlayLayout activeLayout)
    {
        ArgumentNullException.ThrowIfNull(activeLayout);
        var placements = OverlayLayoutCatalog.Supported.ToDictionary(
            definition => definition.Name,
            definition => activeLayout.Placements.GetValueOrDefault(definition.Name, definition.DefaultPlacement),
            StringComparer.Ordinal
        );
        original = new Dictionary<string, LegacyOverlayPlacement>(placements, StringComparer.Ordinal);
        originalDefaultOpacity = activeLayout.DefaultOpacity;
        originalScaleIndex = activeLayout.ScaleIndex;
        workingDefaultOpacity = originalDefaultOpacity;
        workingLayout = new LegacyOverlayLayout(placements, activeLayout.DefaultOpacity, null);
        workingLayout.SetScaleIndex(activeLayout.ScaleIndex);
    }

    /// <summary>Exposes the opening opacity for conflict detection against a newer persisted value.</summary>
    internal double? OriginalDefaultOpacity => originalDefaultOpacity;

    /// <summary>Indicates when preview geometry should replace a captured live panel size.</summary>
    internal bool HasGeometryChanges(string plotterName)
    {
        LegacyOverlayPlacement current = GetPlacement(plotterName);
        LegacyOverlayPlacement previous = GetOriginalPlacement(plotterName);
        return originalScaleIndex != ScaleIndex
            || current.ScaleIndex != previous.ScaleIndex
            || current.TypographyScale != previous.TypographyScale
            || current.SizeOverride != previous.SizeOverride;
    }

    /// <summary>Applies only edited fields to fresh persisted records and refuses conflicting edits to the same field.</summary>
    internal IReadOnlyDictionary<string, LegacyOverlayPlacement> MergeWith(LegacyOverlayLayout latest)
    {
        var merged = new Dictionary<string, LegacyOverlayPlacement>(StringComparer.Ordinal);
        foreach ((string name, LegacyOverlayPlacement requested) in Changes)
        {
            LegacyOverlayPlacement previous = GetOriginalPlacement(name);
            LegacyOverlayPlacement current = latest.Placements.GetValueOrDefault(
                name,
                OverlayLayoutCatalog.GetRequired(name).DefaultPlacement
            );
            (
                LegacyHorizontalAnchor Horizontal,
                int HorizontalOffset,
                LegacyVerticalAnchor Vertical,
                int VerticalOffset,
                OverlayPositionReference? PositionReference,
                LegacyHorizontalAnchor? GrowthAnchor
            ) position = MergeField(Position(previous), Position(requested), Position(current), name, "position");
            merged[name] = current with
            {
                Horizontal = position.Horizontal,
                HorizontalOffset = position.HorizontalOffset,
                Vertical = position.Vertical,
                VerticalOffset = position.VerticalOffset,
                PositionReference = position.PositionReference,
                GrowthAnchor = position.GrowthAnchor,
                Opacity = MergeField(previous.Opacity, requested.Opacity, current.Opacity, name, "opacity"),
                ScaleIndex = MergeField(previous.ScaleIndex, requested.ScaleIndex, current.ScaleIndex, name, "scale"),
                TypographyScale = MergeField(
                    previous.TypographyScale,
                    requested.TypographyScale,
                    current.TypographyScale,
                    name,
                    "text scale"
                ),
                SizeOverride = MergeField(
                    previous.SizeOverride,
                    requested.SizeOverride,
                    current.SizeOverride,
                    name,
                    "size"
                ),
                GrowthDirection = MergeField(
                    previous.GrowthDirection,
                    requested.GrowthDirection,
                    current.GrowthDirection,
                    name,
                    "growth direction"
                ),
            };
        }
        return merged;
    }

    /// <summary>Treats the coordinate reference and its anchors as one value so concurrent edits cannot split them.</summary>
    private static (
        LegacyHorizontalAnchor Horizontal,
        int HorizontalOffset,
        LegacyVerticalAnchor Vertical,
        int VerticalOffset,
        OverlayPositionReference? PositionReference,
        LegacyHorizontalAnchor? GrowthAnchor
    ) Position(LegacyOverlayPlacement value) =>
        (
            value.Horizontal,
            value.HorizontalOffset,
            value.Vertical,
            value.VerticalOffset,
            value.PositionReference,
            value.GrowthAnchor
        );

    /// <summary>Preserves independent edits while requiring an explicit retry after a same-field conflict.</summary>
    internal static T MergeField<T>(T previous, T requested, T current, string name, string field)
    {
        if (EqualityComparer<T>.Default.Equals(previous, requested))
        {
            return current;
        }
        if (
            !EqualityComparer<T>.Default.Equals(current, previous)
            && !EqualityComparer<T>.Default.Equals(current, requested)
        )
        {
            throw new InvalidDataException(
                $"The {field} for {name} changed in another editor or instance. No changes were saved; reopen the editor to choose the desired value."
            );
        }
        return requested;
    }

    public bool HasChanges => Changes.Count > 0 || HasDefaultOpacityChange;

    public bool HasDefaultOpacityChange => !EquivalentOpacity(workingDefaultOpacity, originalDefaultOpacity);

    public double DefaultOpacity => workingDefaultOpacity ?? 1d;

    public int ScaleIndex => workingLayout.ScaleIndex;

    public IReadOnlyDictionary<string, LegacyOverlayPlacement> Changes =>
        workingLayout
            .Placements.Where(entry => original.GetValueOrDefault(entry.Key) != entry.Value)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    public LegacyOverlayPlacement GetPlacement(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        return workingLayout.Placements.TryGetValue(plotterName, out LegacyOverlayPlacement? placement)
            ? placement
            : throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                $"Overlay '{plotterName}' is not supported by the position editor."
            );
    }

    public LegacyOverlayPlacement GetOriginalPlacement(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        return original.TryGetValue(plotterName, out LegacyOverlayPlacement? placement)
            ? placement
            : throw new ArgumentOutOfRangeException(
                nameof(plotterName),
                $"Overlay '{plotterName}' is not supported by the position editor."
            );
    }

    public double GetOpacity(string plotterName) => GetPlacement(plotterName).Opacity ?? DefaultOpacity;

    public int GetScaleIndex(string plotterName) => GetPlacement(plotterName).ScaleIndex ?? ScaleIndex;

    public OverlayTypographyScale GetTypographyScale(string plotterName) =>
        GetPlacement(plotterName).TypographyScale ?? OverlayTypographyScale.Default;

    public OverlayPanelSize? GetSizeOverride(string plotterName) => GetPlacement(plotterName).SizeOverride;

    public bool SetDefaultOpacity(double opacity)
    {
        ValidateOpacity(opacity, nameof(opacity));
        double? normalized = originalDefaultOpacity is null && Math.Abs(opacity - 1d) < 0.0001d ? null : opacity;
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

        LegacyOverlayPlacement placement = GetPlacement(plotterName);
        return workingLayout.SetPlacement(plotterName, placement with { Opacity = opacity });
    }

    public bool SetScaleOverride(string plotterName, int? scaleIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (scaleIndex is { } value && !OverlayScaleCatalog.IsSupported(value))
        {
            throw new ArgumentOutOfRangeException(nameof(scaleIndex), $"Overlay scale index {value} is not supported.");
        }

        LegacyOverlayPlacement placement = GetPlacement(plotterName);
        int? normalized = scaleIndex is { } requestedIndex ? OverlayScaleCatalog.NormalizeIndex(requestedIndex) : null;
        return workingLayout.SetPlacement(plotterName, placement with { ScaleIndex = normalized });
    }

    public bool SetTypographyScale(string plotterName, OverlayTypographyScale scale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(scale);
        LegacyOverlayPlacement placement = GetPlacement(plotterName);
        return workingLayout.SetPlacement(
            plotterName,
            placement with
            {
                TypographyScale = scale.IsDefault ? null : scale,
            }
        );
    }

    public bool SetSizeOverride(string plotterName, OverlayPanelSize? size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (
            size is { } value
            && (
                !double.IsFinite(value.Width) || !double.IsFinite(value.Height) || value.Width <= 0 || value.Height <= 0
            )
        )
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Overlay size dimensions must be positive.");
        }

        LegacyOverlayPlacement placement = GetPlacement(plotterName);
        return workingLayout.SetPlacement(plotterName, placement with { SizeOverride = size });
    }

    /// <summary>Changes a panel's growth preference without discarding its appearance or placement.</summary>
    public bool SetGrowthDirection(string plotterName, OverlayGrowthDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        LegacyOverlayPlacement placement = GetPlacement(plotterName);
        return workingLayout.SetPlacement(plotterName, placement with { GrowthDirection = direction });
    }

    /// <summary>Updates the draft for a supported panel without replacing its opening snapshot.</summary>
    public bool SetPlacement(string plotterName, LegacyOverlayPlacement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(placement);
        _ = GetPlacement(plotterName);
        return workingLayout.SetPlacement(plotterName, placement);
    }

    /// <summary>Saves the inward-facing top corner chosen from the panel's physical monitor.</summary>
    public bool MoveWithContentAnchors(
        string plotterName,
        PixelPoint position,
        PixelSize previewSize,
        PixelRect hostBounds,
        PixelRect? monitorBounds = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        LegacyOverlayPlacement placement = GetPlacement(plotterName);
        LegacyHorizontalAnchor anchor = OverlayContentPlacement.SelectHorizontalAnchor(
            position,
            previewSize,
            monitorBounds ?? hostBounds,
            placement.GrowthDirection
        );
        LegacyOverlayPlacement reanchored = placement with
        {
            Horizontal = anchor,
            Vertical = LegacyVerticalAnchor.Top,
            GrowthAnchor = anchor,
        };
        LegacyOverlayPlacement positioned = CreatePlacement(reanchored, position, previewSize, hostBounds);
        return workingLayout.SetPlacement(plotterName, positioned);
    }

    internal static LegacyOverlayPlacement CreatePlacement(
        LegacyOverlayPlacement original,
        PixelPoint position,
        PixelSize overlaySize,
        PixelRect gameBounds
    )
    {
        ArgumentNullException.ThrowIfNull(original);
        int horizontalOffset = original.Horizontal switch
        {
            LegacyHorizontalAnchor.Left => position.X - gameBounds.X,
            LegacyHorizontalAnchor.Center => position.X - (gameBounds.X + ((gameBounds.Width - overlaySize.Width) / 2)),
            LegacyHorizontalAnchor.Right => gameBounds.Right - overlaySize.Width - position.X,
            _ => position.X,
        };
        int verticalOffset = original.Vertical switch
        {
            LegacyVerticalAnchor.Top => position.Y - gameBounds.Y,
            LegacyVerticalAnchor.Middle => position.Y - (gameBounds.Y + ((gameBounds.Height - overlaySize.Height) / 2)),
            LegacyVerticalAnchor.Bottom => gameBounds.Bottom - overlaySize.Height - position.Y,
            _ => position.Y,
        };
        return original with
        {
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
            PositionReference = new OverlayPositionReference(gameBounds.Width, gameBounds.Height),
        };
    }

    public void SetScaleIndex(int index)
    {
        workingLayout.SetScaleIndex(index);
    }

    public PixelPoint GetPosition(string plotterName, PixelRect hostBounds, PixelSize previewSize)
    {
        return workingLayout.GetPosition(plotterName, hostBounds, previewSize)
            ?? throw new InvalidOperationException($"Overlay '{plotterName}' has no working position.");
    }

    /// <summary>Records a user drag with a stable corner for subsequent content growth.</summary>
    public bool Move(
        string plotterName,
        PixelPoint position,
        PixelSize previewSize,
        PixelRect hostBounds,
        PixelRect? monitorBounds = null
    )
    {
        return MoveWithContentAnchors(plotterName, position, previewSize, hostBounds, monitorBounds);
    }

    private static void ValidateOpacity(double opacity, string parameterName)
    {
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Overlay opacity must be from 0 to 1.");
        }
    }

    private static bool EquivalentOpacity(double? left, double? right)
    {
        if (left.HasValue != right.HasValue)
        {
            return false;
        }

        return !left.HasValue || Math.Abs(left.Value - right!.Value) <= 0.0001d;
    }
}
