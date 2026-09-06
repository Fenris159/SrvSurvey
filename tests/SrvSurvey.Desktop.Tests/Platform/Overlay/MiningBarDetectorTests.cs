using System.IO.Compression;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class MiningBarDetectorTests
{
    [Theory]
    [InlineData(19, 0, false)]
    [InlineData(20, 0, false)]
    [InlineData(21, 0, false)]
    [InlineData(20, -18, false)]
    [InlineData(20, 24, true)]
    [InlineData(20, 0, true)]
    public void ContinuousGrayCircleCannotBeADeploymentBar(double calibratedRadius, double rotation, bool inverted)
    {
        var settings = new MiningDetectionSettings { RotationDegrees = rotation };
        var geometry = new MiningHudGeometry(settings);
        var pixels = new byte[96 * 96 * 4];
        for (var y = 0; y < 96; y++) for (var x = 0; x < 96; x++)
            {
                var r = geometry.RingDistance(x - 48d, y - 48d, 1);
                var color = (byte)(r >= 21 && r <= 24 ? 160 : 20);
                if (inverted) color = (byte)(180 - color);
                var p = (y * 96 + x) * 4;
                pixels[p] = pixels[p + 1] = pixels[p + 2] = color; pixels[p + 3] = 255;
            }
        var score = MiningBarDetector.ScoreBar(new CapturedPixelBuffer(96, 96, pixels), 48, 48, calibratedRadius,
            settings);
        var rim = MiningCircleMask.Locate(new CapturedPixelBuffer(96, 96, pixels), 48, 48, calibratedRadius,
            geometry);
        Assert.True(score < .82, $"Continuous rim scored {score}; rim {rim}");
    }

    [Fact]
    public void MissingCircleIsUncertainRatherThanAnAbsentRig()
    {
        var source = new CapturedPixelBuffer(96, 96, new byte[96 * 96 * 4]);
        Assert.True(double.IsNaN(MiningBarDetector.ScoreBar(source, 48, 48, 22, new())));
    }

    [Theory]
    [InlineData(255, 0, 0, true)]
    [InlineData(0, 255, 0, true)]
    [InlineData(0, 0, 255, true)]
    [InlineData(255, 255, 0, true)]
    [InlineData(0, 255, 255, true)]
    [InlineData(255, 0, 255, true)]
    [InlineData(255, 255, 255, false)]
    [InlineData(140, 140, 140, false)]
    [InlineData(255, 245, 240, false)]
    [InlineData(0, 0, 0, false)]
    [InlineData(40, 0, 0, false)]
    public void OnlyBrightChromaticPixelsContributeToTheBar(byte red, byte green, byte blue, bool accepted)
    {
        Assert.Equal(accepted, MiningBarShape.ColoredBrightness(new(red, green, blue)) > 0);
    }

    [Fact]
    public void BrightBarCanBeRecognizedWithoutANeutralCircle()
    {
        var source = Load(20);
        var bytes = source.BgraPixels.ToArray();
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
            {
                if (MiningBarShape.ColoredBrightness(source.GetPixel(x, y)) >= 80) continue;
                var p = (y * source.Width + x) * 4;
                bytes[p] = bytes[p + 1] = bytes[p + 2] = 0;
            }
        var colored = new CapturedPixelBuffer(source.Width, source.Height, bytes);
        var settings = CalibratedSettings();
        var rim = MiningCircleMask.Locate(colored, 178, 94, 22, new MiningHudGeometry(settings));
        Assert.True(rim.Confidence < 8, $"Unexpected rim {rim}");
        Assert.True(MiningBarDetector.ScoreBar(colored, 178, 94, 22, settings) >= .82);
    }

    [Fact]
    public void SlotDisplayDoesNotPresentAContradictedStableBarAsCurrent()
    {
        var model = new MiningDetectionViewModel(null);
        var present = new MiningBarAnalysis(Enumerable.Repeat(MiningBarState.Present, 6).ToArray(), 0, 0);
        var absent = new MiningBarAnalysis(Enumerable.Repeat(MiningBarState.Absent, 6).ToArray(), 0, 0);
        for (var i = 0; i < 3; i++) model.Apply(present);
        Assert.Contains("1 BAR", model.SlotsText);
        model.Apply(absent);
        Assert.Contains("1 …", model.SlotsText);
        Assert.DoesNotContain("BAR", model.SlotsText);
        model.Apply(absent);
        model.Apply(absent);
        Assert.Contains("1 empty", model.SlotsText);
        Assert.Contains("Bar disappeared", model.LastAppearance);
    }
    [Theory]
    [InlineData(48.5, .75)]
    [InlineData(44, .65)]
    [InlineData(46, .65)]
    [InlineData(48.5, .65)]
    [InlineData(48.5, .7)]
    public void LiveObserverFrameDoesNotMistakeTheSecondRimForABar(double diameter, double aspect)
    {
        var source = Load("live-observer");
        var settings = new MiningDetectionSettings
        {
            CircleWidth = diameter / 400,
            CircleAspectRatio = aspect,
            RotationDegrees = -6,
            BarGap = 0,
            MotionMargin = 56d / 400,
            Markers = [new(123d/400,102d/220),new(189d/400,96d/220),new(250d/400,90d/220),
                new(123d/400,148d/220),new(189d/400,139d/220),new(250d/400,131d/220)]
        };
        settings = settings with { LabelTemplates = MiningBarDetector.CaptureReference(source, settings) };
        var result = MiningBarDetector.Analyze(source, settings);
        Assert.True(result.Slots[0] != MiningBarState.Absent,
            $"{string.Join(',', result.Slots)}; scores {string.Join(',', result.BarScores)}");
        Assert.True(result.Slots.Skip(1).All(state => state != MiningBarState.Present),
            $"{string.Join(',', result.Slots)}; scores {string.Join(',', result.BarScores)}");
    }
    [Theory]
    [InlineData(.65, -14, .14)]
    [InlineData(.75, -6, 0)]
    public void LatestDeploymentScreenshotRecognizesTheActiveSlot(double aspect, double rotation, double gap)
    {
        var source = Load("reported-live");
        var settings = new MiningDetectionSettings
        {
            CircleWidth = 94d / 500,
            RotationDegrees = rotation,
            CircleAspectRatio = aspect,
            BarGap = gap,
            Markers = [new(117d/500,120d/286),new(246d/500,108d/286),new(373d/500,96d/286),
                new(117d/500,208d/286),new(246d/500,191d/286),new(373d/500,176d/286)]
        };
        settings = settings with { LabelTemplates = MiningBarDetector.CaptureReference(source, settings) };
        var result = MiningBarDetector.Analyze(source, settings);
        Assert.True(result.Slots[0] == MiningBarState.Present,
            $"{string.Join(',', result.Slots)}; scores {string.Join(',', result.BarScores)}");
        Assert.All(result.Slots.Skip(1), state => Assert.NotEqual(MiningBarState.Present, state));
    }
    [Theory]
    [InlineData(-14, false)]
    [InlineData(-10, false)]
    [InlineData(-18, false)]
    [InlineData(-14, true)]
    public void ReportedHudRecognizesTheVisibleBar(double rotation, bool recolor)
    {
        var source = Load("reported");
        if (recolor)
        {
            var bytes = source.BgraPixels.ToArray();
            for (var i = 0; i < bytes.Length; i += 4) (bytes[i + 1], bytes[i + 2]) = (bytes[i + 2], bytes[i + 1]);
            source = new(source.Width, source.Height, bytes);
        }
        var settings = new MiningDetectionSettings
        {
            CircleWidth = 96d / 506,
            CircleAspectRatio = .65,
            RotationDegrees = rotation,
            Markers = [new(143d/506,116d/260),new(274d/506,104d/260),new(399d/506,90d/260),
                new(143d/506,206d/260),new(274d/506,191d/260),new(399d/506,174d/260)]
        };
        settings = settings with { LabelTemplates = MiningBarDetector.CaptureReference(source, settings) };
        var result = MiningBarDetector.Analyze(source, settings);
        Assert.True(result.Slots[0] == MiningBarState.Present,
            $"{string.Join(',', result.Slots)}; scores {string.Join(',', result.BarScores)}");
        Assert.All(result.Slots.Skip(1), state => Assert.NotEqual(MiningBarState.Present, state));
    }
    [Theory]
    [InlineData(10, 1)]
    [InlineData(20, 1)]
    [InlineData(40, 2)]
    [InlineData(60, 2)]
    [InlineData(100, 3)]
    public void RecordedBarsAreRecognizedDespiteMovement(int frame, int active)
    {
        var source = Load(frame);
        var settings = CalibratedSettings();
        var result = MiningBarDetector.Analyze(source, settings);
        var debug = string.Join(",", result.BarScores.Select(s => s.ToString("F3")));
        for (var slot = 0; slot < 6; slot++)
        {
            if (slot < active)
                Assert.True(result.Slots[slot] == MiningBarState.Present,
                    $"Frame {frame}, slot {slot + 1}: {string.Join(',', result.Slots)}; scores {debug}");
            else Assert.NotEqual(MiningBarState.Present, result.Slots[slot]);
        }
    }

    private static MiningDetectionSettings CalibratedSettings()
    {
        var settings = new MiningDetectionSettings
        {
            CircleWidth = .11,
            MotionMargin = .25,
            Markers = [new(.445,.47), new(.595,.43), new(.74,.39),
                new(.445,.67), new(.595,.64), new(.74,.60)],
        };
        return settings with { LabelTemplates = MiningBarDetector.CaptureReference(Load(20), settings) };
    }

    [Theory]
    [InlineData(0)] // Swap red and green: the original green bar becomes red.
    [InlineData(1)] // Swap blue and green.
    [InlineData(2)] // Muted colors.
    [InlineData(3)] // Inverted light/dark polarity.
    [InlineData(4)] // Bright grayscale is not a deployment color.
    [InlineData(5)] // Dim grayscale is not a deployment color.
    public void CalibrationAndBarRecognitionDoNotRequireGreen(int recolor)
    {
        static CapturedPixelBuffer Transform(CapturedPixelBuffer source, int mode)
        {
            var bytes = source.BgraPixels.ToArray();
            for (var i = 0; i < bytes.Length; i += 4)
            {
                if (mode == 0) (bytes[i + 1], bytes[i + 2]) = (bytes[i + 2], bytes[i + 1]);
                else if (mode == 1) (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
                else if (mode is 4 or 5)
                {
                    var gray = mode == 4 ? Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]))
                        : (byte)((bytes[i] + bytes[i + 1] + bytes[i + 2]) / 3);
                    bytes[i] = bytes[i + 1] = bytes[i + 2] = gray;
                }
                else for (var c = 0; c < 3; c++) bytes[i + c] = mode == 2
                    ? (byte)(80 + bytes[i + c] * .5) : (byte)(255 - bytes[i + c]);
            }
            return new(source.Width, source.Height, bytes);
        }
        var settings = CalibratedSettings();
        settings = settings with { LabelTemplates = MiningBarDetector.CaptureReference(Transform(Load(20), recolor), settings) };
        var result = MiningBarDetector.Analyze(Transform(Load(40), recolor), settings);
        if (recolor >= 4)
        {
            Assert.All(result.Slots, state => Assert.NotEqual(MiningBarState.Present, state));
            return;
        }
        Assert.True(result.Slots[0] == MiningBarState.Present && result.Slots[1] == MiningBarState.Present,
            $"{recolor}: {string.Join(',', result.Slots)} scores {string.Join(',', result.BarScores)}");
        Assert.All(result.Slots.Skip(2), state => Assert.NotEqual(MiningBarState.Present, state));
    }

    [Fact]
    public void TheSameCalibrationRecognizesADoubledViewport()
    {
        var original = Load(20);
        var bytes = new byte[original.Width * original.Height * 16];
        var width = original.Width * 2;
        var height = original.Height * 2;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = original.GetPixel(x / 2, y / 2);
                var offset = (y * width + x) * 4;
                bytes[offset] = p.Blue;
                bytes[offset + 1] = p.Green;
                bytes[offset + 2] = p.Red;
                bytes[offset + 3] = 255;
            }
        var settings = CalibratedSettings();
        var expected = MiningBarDetector.Analyze(original, settings);
        var actual = MiningBarDetector.Analyze(new CapturedPixelBuffer(width, height, bytes), settings);
        Assert.Equal(expected.Slots, actual.Slots);
        Assert.Equal(MiningBarState.Present, actual.Slots[0]);
    }

    [Theory]
    [InlineData(-16)]
    [InlineData(20)]
    public void RotatedHudUsesTheCalibratedAngleForLabelsAndBars(double degrees)
    {
        var source = Load(20);
        var angle = degrees * Math.PI / 180;
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        var bytes = new byte[source.Width * source.Height * 4];
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var sx = (x - 200) * c + (y - 100) * s + 200;
                var sy = -(x - 200) * s + (y - 100) * c + 100;
                if (sx < 0 || sy < 0 || sx >= source.Width - 1 || sy >= source.Height - 1) continue;
                var ix = (int)sx;
                var iy = (int)sy;
                var fx = sx - ix;
                var fy = sy - iy;
                for (var channel = 0; channel < 4; channel++)
                {
                    double Sample(int px, int py) => source.BgraPixels.Span[(py * source.Width + px) * 4 + channel];
                    bytes[(y * source.Width + x) * 4 + channel] = (byte)Math.Round(
                        Sample(ix, iy) * (1 - fx) * (1 - fy) + Sample(ix + 1, iy) * fx * (1 - fy)
                        + Sample(ix, iy + 1) * (1 - fx) * fy + Sample(ix + 1, iy + 1) * fx * fy);
                }
            }
        var image = new CapturedPixelBuffer(source.Width, source.Height, bytes);
        var settings = CalibratedSettings();
        settings = settings with
        {
            RotationDegrees = MiningDetectionSettings.ReferenceRotationDegrees + degrees,
            Markers = settings.Markers.Select(p => new MiningDetectionPoint(
                ((p.X * 400 - 200) * c - (p.Y * 200 - 100) * s + 200) / 400,
                ((p.X * 400 - 200) * s + (p.Y * 200 - 100) * c + 100) / 200)).ToArray()
        };
        settings = settings with { LabelTemplates = MiningBarDetector.CaptureReference(image, settings) };
        var result = MiningBarDetector.Analyze(image, settings);
        Assert.Equal(MiningBarState.Present, result.Slots[0]);
        Assert.All(result.Slots.Skip(1), state => Assert.NotEqual(MiningBarState.Present, state));
    }

    [Theory]
    [InlineData(-30, .5)]
    [InlineData(20, 1)]
    public void GuidesUseTheRequestedAbsoluteAngleAndOvalHeight(double degrees, double height)
    {
        var geometry = new MiningHudGeometry(new() { RotationDegrees = degrees, CircleAspectRatio = height });
        var major = geometry.RingPoint(0, 22);
        var minor = geometry.RingPoint(Math.PI / 2, 22);
        Assert.Equal(22, major.Length, 6);
        Assert.Equal(22 * height, minor.Length, 6);
        Assert.Equal(degrees, Math.Atan2(major.Y, major.X) * 180 / Math.PI, 6);
        Assert.Equal(0, major.X * minor.X + major.Y * minor.Y, 6);
    }

    [Fact]
    public void SearchAdjustmentRetainsLearnedLabelsButGeometryChangesRequireRelearning()
    {
        var model = new MiningDetectionViewModel(null) { IsCalibrating = true };
        model.BeginEdit();
        model.RequestReference();
        var labels = CalibratedSettings().LabelTemplates!;
        model.ApplyReference(labels);
        model.UpdateCalibration(model.Settings with { MotionMargin = model.Settings.MotionMargin + .02 });
        Assert.NotNull(model.Settings.LabelTemplates);
        Assert.True(model.IsCalibrationTesting);
        model.UpdateCalibration(model.Settings with { BarGap = .3 });
        Assert.NotNull(model.Settings.LabelTemplates);
        Assert.True(model.IsCalibrationTesting);
        model.UpdateCalibration(model.Settings with { RotationDegrees = 12 });
        Assert.Null(model.Settings.LabelTemplates);
        Assert.False(model.IsCalibrationTesting);
    }

    [Theory]
    [InlineData(80)] // Glare washes out label 6.
    [InlineData(120)] // The first circle is beyond the calibrated movement allowance.
    [InlineData(140)] // Looking away from the HUD.
    public void UnreadableOrOutOfRangeHudIsUnknown(int frame)
    {
        var settings = CalibratedSettings();
        Assert.All(MiningBarDetector.Analyze(Load(frame), settings).Slots,
            state => Assert.Equal(MiningBarState.Unknown, state));
        Assert.All(MiningBarDetector.Analyze(new CapturedPixelBuffer(400, 200, new byte[400 * 200 * 4]), settings).Slots,
            state => Assert.Equal(MiningBarState.Unknown, state));
    }

    [Fact]
    public void AppearanceNeedsBaselineAndThreeReadingsAndDoesNotRepeat()
    {
        var confirm = new MiningBarConfirmation();
        var present = new MiningBarAnalysis(Enumerable.Repeat(MiningBarState.Present, 6).ToArray(), 0, 0);
        var absent = new MiningBarAnalysis(Enumerable.Repeat(MiningBarState.Absent, 6).ToArray(), 0, 0);
        for (var i = 0; i < 4; i++) Assert.Empty(confirm.Apply(present));
        for (var i = 0; i < 3; i++) confirm.Apply(absent);
        Assert.Empty(confirm.Apply(present));
        Assert.Empty(confirm.Apply(MiningBarAnalysis.Unknown()));
        Assert.Empty(confirm.Apply(present));
        Assert.Empty(confirm.Apply(present));
        Assert.Equal([1, 2, 3, 4, 5, 6], confirm.Apply(present));
        Assert.Empty(confirm.Apply(present));
        Assert.Empty(confirm.Apply(MiningBarAnalysis.Unknown()));
        Assert.All(confirm.States, state => Assert.Equal(MiningBarState.Present, state));
        confirm.Apply(absent);
        confirm.Apply(MiningBarAnalysis.Unknown());
        Assert.Empty(confirm.Disappeared);
        confirm.Apply(absent);
        confirm.Apply(absent);
        Assert.Empty(confirm.Disappeared);
        confirm.Apply(absent);
        Assert.Equal([1, 2, 3, 4, 5, 6], confirm.Disappeared);
        confirm.Apply(absent);
        Assert.Empty(confirm.Disappeared);
    }

    [Fact]
    public void CalibrationPersistencePreservesOtherMiningPreferencesAndRejectsMalformedReferences()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-calibration-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SurfaceMiningSettingsStore(path);
            store.SaveAutoClearRigsOnShipBoarding(false);
            var expected = CalibratedSettings() with { Enabled = true, BarGap = .3 };
            store.SaveDetection(expected);
            var restored = store.LoadDetection();
            Assert.True(restored.Enabled);
            Assert.True(restored.HasSameCalibration(expected));
            Assert.False(store.LoadAutoClearRigsOnShipBoarding());
            store.SaveDetection(expected with { LabelTemplates = [new byte[3]] });
            Assert.Null(store.LoadDetection().LabelTemplates);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResizingTheCaptureFramePreservesGuideSizeAndOffsets()
    {
        var settings = new MiningDetectionSettings();
        var viewport = new PixelRect(0, 0, 1920, 1080);
        var before = settings.GetBounds(viewport);
        var after = new PixelRect(before.Position, new PixelSize(before.Width + 150, before.Height + 100));
        var resized = settings.WithBounds(after, viewport);
        Assert.Equal(settings.CircleWidth * before.Width, resized.CircleWidth * after.Width, 6);
        Assert.Equal(settings.MotionMargin * before.Width, resized.MotionMargin * after.Width, 6);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(settings.Markers[i].X * before.Width, resized.Markers[i].X * after.Width, 6);
            Assert.Equal(settings.Markers[i].Y * before.Height, resized.Markers[i].Y * after.Height, 6);
        }
    }

    [Fact]
    public void CalibrationBoundsFollowViewportAndCancelDoesNotPersist()
    {
        var settings = new MiningDetectionSettings();
        var viewport = new PixelRect(100, 200, 1920, 1080);
        var bounds = new PixelRect(580, 740, 384, 216);
        settings = settings.WithBounds(bounds, viewport);
        Assert.Equal(bounds, settings.GetBounds(viewport));
        Assert.Equal(new PixelRect(-960, 1080, 768, 432),
            settings.GetBounds(new PixelRect(-1920, 0, 3840, 2160)));
        var vm = new MiningDetectionViewModel(null);
        vm.BeginEdit();
        vm.UpdateCalibration(settings);
        Assert.True(vm.HasCalibrationChanges);
        vm.EndEdit();
        Assert.Equal(.15, vm.Settings.X);
        vm.BeginEdit();
        vm.UpdateCalibration(settings);
        vm.SaveEdit();
        vm.EndEdit();
        Assert.Equal(settings.X, vm.Settings.X);
    }

    private static CapturedPixelBuffer Load(int frame) => Load(frame.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static CapturedPixelBuffer Load(string frame)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MiningHud", $"{frame}.bgra.gz"));
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(zip);
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        return new(width, height, reader.ReadBytes(width * height * 4));
    }
}
