using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Retains a panel's growth corner while keeping fitting content inside its presentation bounds.</summary>
internal sealed class OverlayContentPlacement
{
    private PositionKey? key;
    private PixelRect referenceBounds;
    private PixelRect monitorBounds;
    private PixelRect visibleBounds;
    private PixelPoint corner;
    private bool alignRight;

    /// <summary>Reanchors only after a position or reference-frame change, independently of content size and opacity.</summary>
    public PixelPoint Resolve(
        PixelPoint requested,
        PixelSize size,
        PixelRect monitor,
        PixelRect reference,
        LegacyOverlayPlacement? placement,
        PixelRect? viewport = null
    )
    {
        var nextKey = PositionKey.From(placement);
        if (key != nextKey || referenceBounds != reference)
        {
            key = nextKey;
            referenceBounds = reference;
            monitorBounds = monitor;
            alignRight =
                SelectHorizontalAnchor(
                    requested,
                    size,
                    monitor,
                    placement?.GrowthDirection ?? OverlayGrowthDirection.Automatic,
                    placement?.GrowthAnchor
                ) == LegacyHorizontalAnchor.Right;
            corner = new PixelPoint(requested.X + (alignRight ? size.Width : 0), requested.Y);
        }
        visibleBounds = viewport ?? monitorBounds;
        return GetPosition(size);
    }

    /// <summary>Persists a geometry edit without rechoosing the corner when the larger panel crosses the midpoint.</summary>
    public LegacyOverlayPlacement CapturePlacement(LegacyOverlayPlacement appearance, PixelSize size)
    {
        PixelPoint position = GetPosition(size);
        LegacyOverlayPlacement placement = OverlayPositionEditSession.CreatePlacement(
            appearance with
            {
                Horizontal = alignRight ? LegacyHorizontalAnchor.Right : LegacyHorizontalAnchor.Left,
                Vertical = LegacyVerticalAnchor.Top,
                GrowthAnchor = alignRight ? LegacyHorizontalAnchor.Right : LegacyHorizontalAnchor.Left,
            },
            position,
            size,
            referenceBounds
        );
        corner = new PixelPoint(position.X + (alignRight ? size.Width : 0), position.Y);
        key = PositionKey.From(placement);
        return placement;
    }

    /// <summary>Shifts upward or sideways only when clearance runs out, retaining the preferred corner for shrink.</summary>
    private PixelPoint GetPosition(PixelSize size)
    {
        int x = corner.X - (alignRight ? size.Width : 0);
        return ClampToBounds(new PixelPoint(x, corner.Y), size, visibleBounds);
    }

    /// <summary>Keeps fitting panels inside a viewport and aligns oversized panels with its top-left edge.</summary>
    internal static PixelPoint ClampToBounds(PixelPoint position, PixelSize size, PixelRect bounds) =>
        new(
            Math.Clamp(position.X, bounds.X, Math.Max(bounds.X, bounds.Right - size.Width)),
            Math.Clamp(position.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - size.Height))
        );

    /// <summary>Forgets the corner when the desktop screen configuration changes.</summary>
    public void Reset() => key = null;

    /// <summary>Moves the retained corner to match a manual resize while preserving its growth direction.</summary>
    public void Reanchor(PixelPoint position, PixelSize size) =>
        corner = new PixelPoint(position.X + (alignRight ? size.Width : 0), position.Y);

    /// <summary>Chooses the panel's left or right edge using its center within its own physical monitor.</summary>
    public static LegacyHorizontalAnchor SelectHorizontalAnchor(
        PixelPoint position,
        PixelSize size,
        PixelRect monitor,
        OverlayGrowthDirection direction = OverlayGrowthDirection.Automatic,
        LegacyHorizontalAnchor? savedAnchor = null
    )
    {
        if (direction != OverlayGrowthDirection.Automatic)
        {
            return direction == OverlayGrowthDirection.Left
                ? LegacyHorizontalAnchor.Right
                : LegacyHorizontalAnchor.Left;
        }
        if (savedAnchor is LegacyHorizontalAnchor.Left or LegacyHorizontalAnchor.Right)
        {
            return savedAnchor.Value;
        }
        return (long)position.X * 2 + size.Width < (long)monitor.X * 2 + monitor.Width
            ? LegacyHorizontalAnchor.Left
            : LegacyHorizontalAnchor.Right;
    }

    /// <summary>Finds the monitor containing most of a panel, falling back to the supplied reference frame.</summary>
    public static PixelRect GetMonitorBounds(Window window, PixelPoint position, PixelSize size, PixelRect fallback)
    {
        PixelRect panel = new(position, size);
        return window
                .Screens.All.OrderByDescending(screen => IntersectionArea(screen.Bounds, panel))
                .FirstOrDefault(screen => IntersectionArea(screen.Bounds, panel) > 0)
                ?.Bounds
            ?? fallback;
    }

    /// <summary>Measures monitor overlap without overflowing on large virtual desktops.</summary>
    private static long IntersectionArea(PixelRect first, PixelRect second)
    {
        PixelRect overlap = first.Intersect(second);
        return (long)overlap.Width * overlap.Height;
    }

    /// <summary>Excludes appearance fields so typography, opacity and size edits cannot move the fixed corner.</summary>
    private readonly record struct PositionKey(
        bool IsSaved,
        LegacyHorizontalAnchor Horizontal,
        int HorizontalOffset,
        LegacyVerticalAnchor Vertical,
        int VerticalOffset,
        OverlayPositionReference? Reference,
        OverlayGrowthDirection GrowthDirection,
        LegacyHorizontalAnchor? GrowthAnchor
    )
    {
        /// <summary>Describes saved coordinates, including the absence of a saved placement.</summary>
        public static PositionKey From(LegacyOverlayPlacement? placement) =>
            new(
                placement is not null,
                placement?.Horizontal ?? LegacyHorizontalAnchor.Screen,
                placement?.HorizontalOffset ?? 0,
                placement?.Vertical ?? LegacyVerticalAnchor.Screen,
                placement?.VerticalOffset ?? 0,
                placement?.PositionReference,
                placement?.GrowthDirection ?? OverlayGrowthDirection.Automatic,
                placement?.GrowthAnchor
            );
    }
}
