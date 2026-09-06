using Avalonia;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningDetectionCoordinatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-hud-coordinator-{Guid.NewGuid():N}");

    [AvaloniaTheory]
    [InlineData(true, false, 0, false, 1, 0)]
    [InlineData(false, false, 0, false, 0, 0)]
    [InlineData(true, false, 1, false, 0, 0)]
    [InlineData(true, false, 2, false, 0, 0)]
    [InlineData(false, true, 0, false, 1, 0)]
    [InlineData(true, false, 0, true, 1, 0)]
    [InlineData(true, true, 0, true, 1, 0)]
    [InlineData(true, false, 0, false, 0, 1)]
    [InlineData(true, false, 0, false, 1, 2)]
    [InlineData(true, true, 0, false, 1, 1)]
    public async Task FocusAndPanelGatesAreRecheckedAfterCapture(bool foreground, bool calibrating,
        int focus, bool loseFocusDuringCapture, int expectedCaptures, int movement)
    {
        var store = new SurfaceMiningSettingsStore(Path.Combine(root, "ui.json"));
        store.SaveDetection(new MiningDetectionSettings
        {
            Enabled = true,
        });
        var clock = new TestClock();
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(root), store, clock);
        var scan = new SystemScanState();
        foreach (var json in new[]
        {
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}""",
            """{"event":"Scan","StarSystem":"Test","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Radius":1000000,"PlanetClass":"Rocky body"}""",
        })
        {
            Assert.True(JournalEventEnvelope.TryParse(json, out var envelope, out _));
            scan.Apply(envelope!);
        }
        var session = new SurfaceSurveySessionContext("F123", "Test", "Test", 42, null);
        var status = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            BodyName = "Test 1",
            PlanetRadius = 1_000_000,
            GuiFocus = (GuiFocus)focus,
        };
        await mining.ApplyUpdateAsync(session, scan.CreateSnapshot(), status, "mev_rhino");
        mining.Detection.IsCalibrating = calibrating;
        clock.Advance(1);
        if (movement == 1)
            await mining.ApplyUpdateAsync(session, scan.CreateSnapshot(), status with { Latitude = .01 }, "mev_rhino");
        mining.Detection.IsCalibrationTesting = calibrating;
        if (calibrating) mining.Detection.StartCalibrationTest();
        var tracker = new Tracker { Snapshot = new((nint)1, 42, new(100, 200, 2000, 1000), true, foreground) };
        var capture = new FakeCapture(() =>
        {
            if (movement == 2)
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    await mining.ApplyUpdateAsync(session, scan.CreateSnapshot(), status with { Latitude = .01 }, "mev_rhino"))
                    .GetAwaiter().GetResult();
            if (loseFocusDuringCapture) tracker.Snapshot = tracker.Snapshot with { IsForeground = false };
        });
        using (var coordinator = new MiningDetectionCoordinator(mining, tracker, capture))
        {
            await coordinator.SynchronizeAsync();
            Assert.Equal(expectedCaptures, capture.Count);
            if (movement > 0 && !calibrating)
                Assert.StartsWith("Rhino moving", mining.Detection.StatusText);
            else if (expectedCaptures == 0 || loseFocusDuringCapture && !calibrating)
                Assert.StartsWith("Waiting for Elite", mining.Detection.StatusText);
            else Assert.StartsWith("HUD not located", mining.Detection.StatusText);
            Assert.All(mining.Rigs, rig => Assert.False(rig.IsSet));
        }
        Assert.True(capture.Disposed);
    }

    private sealed class Tracker : IGameWindowTracker
    {
        internal GameWindowSnapshot Snapshot { get; set; } = GameWindowSnapshot.Unavailable;
        public GameWindowSnapshot GetSnapshot() => Snapshot;
        public void Dispose() { }
    }
    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(double seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class FakeCapture(Action onCapture, bool withContrast = false) : IGameScreenCapture
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        internal int Count { get; private set; }
        internal bool Disposed { get; private set; }
        public CapturedPixelBuffer Capture(PixelRect bounds)
        {
            Count++;
            onCapture();
            var pixels = new byte[bounds.Width * bounds.Height * 4];
            if (withContrast)
            {
                for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i / 16) % 2 * 255);
            }
            return new(bounds.Width, bounds.Height, pixels);
        }
        public void Dispose() => Disposed = true;
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
