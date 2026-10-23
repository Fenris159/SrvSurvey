using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SurfaceMotionRenderingTests
{
    [AvaloniaFact]
    public async Task SettlementOverlayReceivesSuccessiveLiveStatusSamplesThroughItsBinding()
    {
        var site = new HumanSiteViewModel();
        var presentation = new HumanSiteOverlayPresentation
        {
            DataContext = new HumanSiteOverlayViewModel(
                site,
                OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows)
            ),
        };
        var window = new Window { Content = presentation };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            HumanSiteMapControl map = Assert.Single(presentation.GetVisualDescendants().OfType<HumanSiteMapControl>());
            Assert.Null(map.MotionStatus);
            EliteStatus first = Flight(0);
            await site.ApplyUpdateAsync([], first, "ship", allowExternalData: false);
            Assert.Same(first, map.MotionStatus);
            EliteStatus second = Flight(0.01) with { Heading = 20 };
            await site.ApplyUpdateAsync([], second, "ship", allowExternalData: false);
            Assert.Same(second, map.MotionStatus);
            site.UpdateStatus(null);
            Assert.Null(map.MotionStatus);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AllFourDrawingsProduceIntermediateFramesWithoutChangingAuthoritativeInputs()
    {
        foreach (SurfaceMotionControl control in CreateControls())
        {
            var clock = new FrameClock();
            control.MotionClock = clock;
            control.MotionStatus = Flight(0);
            var window = new Window
            {
                Width = 320,
                Height = 300,
                Content = control,
            };
            try
            {
                window.Show();
                Assert.NotNull(window.CaptureRenderedFrame());
                clock.Seconds = 1;
                control.MotionStatus = Flight(0.01);
                var frames = new HashSet<string>();
                foreach (double age in new[] { 0.02, 0.06, 0.2, 0.5, 0.9 })
                {
                    clock.Seconds = 1 + age;
                    control.InvalidateVisual();
                    using var stream = new MemoryStream();
                    window.CaptureRenderedFrame()!.Save(stream, PngBitmapEncoderOptions.Default);
                    frames.Add(Convert.ToHexString(SHA256.HashData(stream.ToArray())));
                }
                Assert.True(
                    frames.Count >= 3,
                    $"{control.GetType().Name} must render intermediate motion, got {frames.Count} frames."
                );
                Assert.Equal(0.01, control.MotionStatus.Latitude);
                Assert.Equal(0, control.MotionStatus.Heading);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void HiddenDetachedAndStationaryControlsStopSchedulingMotion()
    {
        var control = new ProbeControl();
        var clock = new FrameClock();
        control.MotionClock = clock;
        control.MotionStatus = Flight(0);
        var window = new Window
        {
            Width = 100,
            Height = 100,
            Content = control,
        };
        try
        {
            window.Show();
            window.CaptureRenderedFrame();
            clock.Seconds = 1;
            control.MotionStatus = Flight(0.01);
            clock.Seconds = 1.5;
            control.Sample();
            Assert.InRange(control.Pose!.Value.Location.Latitude, 0.0149, 0.0151);
            (double x, double y) = control.RelativePoint(0, 1_000);
            Assert.InRange(x, -0.1, 0.1);
            Assert.InRange(y, 912, 914);
            control.IsVisible = false;
            control.Sample();
            window.Content = null;
            clock.Seconds = 3;
            window.Content = control;
            control.Sample();
            Assert.Equal(0.01, control.Pose.Value.Location.Latitude);
            control.MotionStatus = null;
            control.Sample();
            Assert.Null(control.Pose);
            Assert.Equal((0d, 100d), control.RelativePoint(0, 100));
        }
        finally
        {
            window.Close();
        }
    }

    private static SurfaceMotionControl[] CreateControls() =>
        [
            new GroundTargetGuidanceControl
            {
                TargetLocation = new SurfaceCoordinate(0.04, 0.01),
                RelativeBearingDegrees = 30,
            },
            new SurfaceSurveyRadarControl
            {
                Markers =
                [
                    new SurfaceRadarMarkerViewModel
                    {
                        Location = new SurfaceCoordinate(0.02, 0),
                        RelativeBearingDegrees = 0,
                        DistanceMeters = 250,
                        RadiusMeters = 100,
                    },
                ],
                SplatBoundaries =
                [
                    new SurfaceRadarPathViewModel(
                        [new SurfaceRadarPointViewModel(80, 30), new SurfaceRadarPointViewModel(100, 60)],
                        false
                    ),
                ],
            },
            new GuardianSiteMapControl
            {
                Projection = new GuardianSiteMapProjection("Alpha", [], [], 500, IsRuins: true),
                Proximity = new GuardianSiteProximitySnapshot(100, 0, 100, 0, -100, null, null),
                ShowLegend = false,
            },
            new HumanSiteMapControl
            {
                Projection = new HumanSiteMapProjection(
                    HumanSiteEconomy.Unknown,
                    1,
                    "Test",
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    500,
                    0
                ),
                CommanderOffset = new HumanSiteMapPoint(0, 100),
            },
        ];

    private static EliteStatus Flight(double latitude) =>
        new()
        {
            Latitude = latitude,
            BodyName = "Test body",
            PlanetRadius = 1_000_000,
            Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
        };

    private sealed class FrameClock : TimeProvider
    {
        public double Seconds { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => (long)(Seconds * TimeSpan.TicksPerSecond);
    }

    private sealed class ProbeControl : SurfaceMotionControl
    {
        public SurfaceVisualPose? Pose => VisualPose;

        public void Sample() => BeginMotionFrame();

        public (double X, double Y) RelativePoint(double bearing, double distance) =>
            VisualRelativePoint(bearing, distance);
    }
}
