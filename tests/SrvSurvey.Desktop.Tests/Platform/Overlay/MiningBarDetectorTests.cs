using System.IO.Compression;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class MiningBarDetectorTests
{
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
    [InlineData(4)] // Grayscale.
    [InlineData(5)] // Grayscale with reduced bar contrast: preserve uncertainty.
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
        if (recolor == 5)
        {
            Assert.NotEqual(MiningBarState.Absent, result.Slots[0]);
            Assert.NotEqual(MiningBarState.Absent, result.Slots[1]);
            Assert.All(result.Slots.Skip(2), state => Assert.NotEqual(MiningBarState.Present, state));
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
