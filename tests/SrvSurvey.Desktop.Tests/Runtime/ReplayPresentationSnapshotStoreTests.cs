using Avalonia;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Tests.Runtime;

public sealed class ReplayPresentationSnapshotStoreTests
{
    [Fact]
    public void CaptureAndApplyRoundTripEveryAnchorAndPortableSetting()
    {
        using var temp = new TemporaryDirectory();
        var paths = CreatePaths(Path.Combine(temp.Path, "source"));
        Directory.CreateDirectory(paths.ConfigDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        var names = OverlayLayoutCatalog.Supported
            .Take(4)
            .Select(definition => definition.Name)
            .ToArray();
        var sourcePlacements = new Dictionary<string, LegacyOverlayPlacement>
        {
            [names[0]] = new(
                LegacyHorizontalAnchor.Left,
                1,
                LegacyVerticalAnchor.Top,
                2,
                0.6,
                1),
            [names[1]] = new(
                LegacyHorizontalAnchor.Center,
                3,
                LegacyVerticalAnchor.Middle,
                4,
                0.7,
                2),
            [names[2]] = new(
                LegacyHorizontalAnchor.Right,
                5,
                LegacyVerticalAnchor.Bottom,
                6,
                0.8,
                3),
            [names[3]] = new(
                LegacyHorizontalAnchor.Screen,
                7,
                LegacyVerticalAnchor.Screen,
                8,
                null,
                4),
        };
        _ = new LegacyOverlayLayoutStore(paths.DataDirectory).Save(
            sourcePlacements,
            0.55,
            updateDefaultOpacity: true);
        new OverlayScaleSettingsStore(paths.UiSettingsPath).Save(
            new OverlayScalePreferences(3));
        var visibility = new OverlayPanelVisibilitySettingsStore(
            paths.UiSettingsPath).Load().ToDictionary();
        visibility[names[0]] = false;
        new OverlayPanelVisibilitySettingsStore(paths.UiSettingsPath).Save(
            visibility);

        var snapshot = ReplayPresentationSnapshotStore.Capture(
            paths,
            new PixelRect(10, 20, 2560, 1440));
        var defaultViewport = ReplayPresentationSnapshotStore.Capture(
            paths,
            new PixelRect(0, 0, 0, 0));
        var session = CreateSession(
            Path.Combine(temp.Path, "session"),
            snapshot);
        ReplayPresentationSnapshotStore.Apply(session);
        var applied = new LegacyOverlayLayoutStore(
            session.DataDirectory).Load();

        Assert.Equal(2560, snapshot.ViewportWidth);
        Assert.Equal(1440, snapshot.ViewportHeight);
        Assert.Equal(1920, defaultViewport.ViewportWidth);
        Assert.Equal(1080, defaultViewport.ViewportHeight);
        Assert.Equal(3, snapshot.GlobalScaleIndex);
        Assert.Equal(0.55, snapshot.DefaultOpacity);
        Assert.False(snapshot.OverlayEnablement[names[0]]);
        Assert.Equal(
            [
                ReplayHorizontalAnchor.Left,
                ReplayHorizontalAnchor.Center,
                ReplayHorizontalAnchor.Right,
                ReplayHorizontalAnchor.Screen,
            ],
            names.Select(name => snapshot.OverlayPlacements[name].Horizontal));
        Assert.Equal(
            [
                ReplayVerticalAnchor.Top,
                ReplayVerticalAnchor.Middle,
                ReplayVerticalAnchor.Bottom,
                ReplayVerticalAnchor.Screen,
            ],
            names.Select(name => snapshot.OverlayPlacements[name].Vertical));
        Assert.Equal(
            sourcePlacements,
            applied.Placements
                .Where(entry => names.Contains(entry.Key, StringComparer.Ordinal))
                .ToDictionary(entry => entry.Key, entry => entry.Value));
        Assert.Equal(0.55, applied.DefaultOpacity);
    }

    [Fact]
    public void CaptureAndApplyRejectInvalidPortablePresentation()
    {
        using var temp = new TemporaryDirectory();
        var paths = CreatePaths(Path.Combine(temp.Path, "invalid-source"));
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(
            Path.Combine(paths.DataDirectory, "plotters.json"),
            "{\"PlotBioStatus\":\"diagonal:8,top:8\"}");

        Assert.Throws<InvalidDataException>(() =>
            ReplayPresentationSnapshotStore.Capture(paths, viewport: null));

        var unsupportedScale = CreateSession(
            Path.Combine(temp.Path, "scale-session"),
            Snapshot(99, new Dictionary<string, ReplayOverlayPlacement>()));
        Assert.Throws<InvalidDataException>(() =>
            ReplayPresentationSnapshotStore.Apply(unsupportedScale));

        var unknownOverlay = CreateSession(
            Path.Combine(temp.Path, "overlay-session"),
            Snapshot(
                0,
                new Dictionary<string, ReplayOverlayPlacement>
                {
                    ["UnknownOverlay"] = new(
                        ReplayHorizontalAnchor.Left,
                        0,
                        ReplayVerticalAnchor.Top,
                        0,
                        null,
                        null),
                }));
        Assert.Throws<InvalidDataException>(() =>
            ReplayPresentationSnapshotStore.Apply(unknownOverlay));

        var invalidPlacementScale = CreateSession(
            Path.Combine(temp.Path, "placement-scale-session"),
            Snapshot(
                0,
                new Dictionary<string, ReplayOverlayPlacement>
                {
                    ["PlotBioStatus"] = new(
                        ReplayHorizontalAnchor.Left,
                        0,
                        ReplayVerticalAnchor.Top,
                        0,
                        null,
                        99),
                }));
        Assert.Throws<InvalidDataException>(() =>
            ReplayPresentationSnapshotStore.Apply(invalidPlacementScale));
        Assert.False(Directory.Exists(invalidPlacementScale.ConfigDirectory));
        Assert.False(Directory.Exists(invalidPlacementScale.DataDirectory));

        ReplayPresentationSnapshotStore.Apply(CreateSession(
            Path.Combine(temp.Path, "empty-session"),
            presentationSnapshot: null));
        ReplayPresentationSnapshotStore.Apply(CreateSession(
            Path.Combine(temp.Path, "empty-snapshot-session"),
            Snapshot(
                0,
                new Dictionary<string, ReplayOverlayPlacement>())));
    }

    private static ReplayPresentationSnapshot Snapshot(
        int scaleIndex,
        IReadOnlyDictionary<string, ReplayOverlayPlacement> placements)
    {
        return new ReplayPresentationSnapshot(
            1920,
            1080,
            scaleIndex,
            null,
            new Dictionary<string, bool>(),
            placements);
    }

    private static AppDataPaths CreatePaths(string root)
    {
        return new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []);
    }

    private static DiagnosticReplaySession CreateSession(
        string root,
        ReplayPresentationSnapshot? presentationSnapshot)
    {
        return new DiagnosticReplaySession(
            Path.Combine(root, "replay-session.json"),
            root,
            Path.Combine(root, "source", "journal.jsonl"),
            Path.Combine(root, "playback", "Journal.01.log"),
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "logs"),
            "test",
            ReplayPrivacyMode.Raw,
            new ReplayCommander("Replay Cmdr", "F123456"),
            [],
            presentationSnapshot);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-replay-presentation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
