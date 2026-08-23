using Avalonia;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Runtime;

internal static class ReplayPresentationSnapshotStore
{
    private static readonly PixelRect DefaultViewport = new(0, 0, 1920, 1080);

    public static ReplayPresentationSnapshot Capture(
        AppDataPaths paths,
        PixelRect? viewport)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var bounds = viewport is { Width: > 0, Height: > 0 }
            ? viewport.Value
            : DefaultViewport;
        var layout = new LegacyOverlayLayoutStore(paths.DataDirectory).Load();
        if (layout.Error is not null)
        {
            throw new InvalidDataException(layout.Error);
        }

        var placements = layout.Placements.ToDictionary(
            entry => entry.Key,
            entry => new ReplayOverlayPlacement(
                Convert(entry.Value.Horizontal),
                entry.Value.HorizontalOffset,
                Convert(entry.Value.Vertical),
                entry.Value.VerticalOffset,
                entry.Value.Opacity,
                entry.Value.ScaleIndex),
            StringComparer.Ordinal);
        return new ReplayPresentationSnapshot(
            bounds.Width,
            bounds.Height,
            new OverlayScaleSettingsStore(paths.UiSettingsPath).Load().Index,
            layout.DefaultOpacity,
            new OverlayPanelVisibilitySettingsStore(paths.UiSettingsPath).Load(),
            placements);
    }

    public static void Apply(DiagnosticReplaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.PresentationSnapshot is not { } snapshot)
        {
            return;
        }

        if (!OverlayScaleCatalog.IsSupported(snapshot.GlobalScaleIndex))
        {
            throw new InvalidDataException(
                "The replay overlay presentation uses an unsupported scale.");
        }

        var supportedNames = OverlayLayoutCatalog.Supported
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (snapshot.OverlayEnablement.Keys.Any(name => !supportedNames.Contains(name))
            || snapshot.OverlayPlacements.Keys.Any(name => !supportedNames.Contains(name)))
        {
            throw new InvalidDataException(
                "The replay overlay presentation contains an unknown overlay.");
        }

        Directory.CreateDirectory(session.ConfigDirectory);
        Directory.CreateDirectory(session.DataDirectory);
        var uiSettingsPath = Path.Combine(
            session.ConfigDirectory,
            "cross-platform-ui.json");
        new OverlayPanelVisibilitySettingsStore(uiSettingsPath).Save(
            snapshot.OverlayEnablement);
        new OverlayScaleSettingsStore(uiSettingsPath).Save(
            new OverlayScalePreferences(snapshot.GlobalScaleIndex));
        var placements = snapshot.OverlayPlacements.ToDictionary(
            entry => entry.Key,
            entry => new LegacyOverlayPlacement(
                Convert(entry.Value.Horizontal),
                entry.Value.HorizontalOffset,
                Convert(entry.Value.Vertical),
                entry.Value.VerticalOffset,
                entry.Value.Opacity,
                entry.Value.ScaleIndex),
            StringComparer.Ordinal);
        if (placements.Count > 0 || snapshot.DefaultOpacity is not null)
        {
            _ = new LegacyOverlayLayoutStore(session.DataDirectory).Save(
                placements,
                snapshot.DefaultOpacity ?? 1d,
                updateDefaultOpacity: snapshot.DefaultOpacity is not null);
        }
    }

    private static ReplayHorizontalAnchor Convert(
        LegacyHorizontalAnchor anchor) => anchor switch
        {
            LegacyHorizontalAnchor.Left => ReplayHorizontalAnchor.Left,
            LegacyHorizontalAnchor.Center => ReplayHorizontalAnchor.Center,
            LegacyHorizontalAnchor.Right => ReplayHorizontalAnchor.Right,
            _ => ReplayHorizontalAnchor.Screen,
        };

    private static ReplayVerticalAnchor Convert(
        LegacyVerticalAnchor anchor) => anchor switch
        {
            LegacyVerticalAnchor.Top => ReplayVerticalAnchor.Top,
            LegacyVerticalAnchor.Middle => ReplayVerticalAnchor.Middle,
            LegacyVerticalAnchor.Bottom => ReplayVerticalAnchor.Bottom,
            _ => ReplayVerticalAnchor.Screen,
        };

    private static LegacyHorizontalAnchor Convert(
        ReplayHorizontalAnchor anchor) => anchor switch
        {
            ReplayHorizontalAnchor.Left => LegacyHorizontalAnchor.Left,
            ReplayHorizontalAnchor.Center => LegacyHorizontalAnchor.Center,
            ReplayHorizontalAnchor.Right => LegacyHorizontalAnchor.Right,
            _ => LegacyHorizontalAnchor.Screen,
        };

    private static LegacyVerticalAnchor Convert(
        ReplayVerticalAnchor anchor) => anchor switch
        {
            ReplayVerticalAnchor.Top => LegacyVerticalAnchor.Top,
            ReplayVerticalAnchor.Middle => LegacyVerticalAnchor.Middle,
            ReplayVerticalAnchor.Bottom => LegacyVerticalAnchor.Bottom,
            _ => LegacyVerticalAnchor.Screen,
        };
}
