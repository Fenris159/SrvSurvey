using Avalonia.Input;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ShortcutCaptureBoxTests
{
    [AvaloniaFact]
    public void UsesTheStandardTextBoxThemeAndRendersAtUsableSize()
    {
        var capture = new ShortcutCaptureBox
        {
            Width = 220,
            Chord = "CTRL K",
        };
        var window = new Window { Content = capture };
        try
        {
            window.Show();

            Assert.Equal(typeof(TextBox), capture.GetType().BaseType);
            Assert.Equal(220, capture.Bounds.Width);
            Assert.True(capture.Bounds.Height >= 20);
            Assert.Equal("CTRL K", capture.Text);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var outputPath = Environment.GetEnvironmentVariable(
                "SRVSURVEY_SHORTCUT_CAPTURE_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                frame.Save(outputPath, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeldCombinationPreviewsLiveAndCommitsAfterEveryKeyIsReleased()
    {
        var capture = new ShortcutCaptureBox { Chord = "ALT X" };

        capture.BeginCapture();
        capture.CaptureKeyDown(Key.LeftCtrl);
        Assert.Equal("CTRL", capture.Text);
        Assert.Equal("ALT X", capture.Chord);
        capture.CaptureKeyDown(Key.LeftShift);
        capture.CaptureKeyDown(Key.K);
        Assert.Equal("CTRL SHIFT K", capture.Text);
        Assert.Equal("ALT X", capture.Chord);

        capture.CaptureKeyUp(Key.K);
        capture.CaptureKeyUp(Key.LeftShift);
        Assert.True(capture.IsCapturing);
        Assert.Equal("ALT X", capture.Chord);
        capture.CaptureKeyUp(Key.LeftCtrl);

        Assert.False(capture.IsCapturing);
        Assert.Equal("CTRL SHIFT K", capture.Chord);
        Assert.Equal("CTRL SHIFT K", capture.Text);
    }

    [AvaloniaFact]
    public void EscapeCancelsAndDeleteOrBackspaceClears()
    {
        var capture = new ShortcutCaptureBox { Chord = "ALT X" };

        capture.BeginCapture();
        capture.CaptureKeyDown(Key.LeftCtrl);
        capture.CaptureKeyDown(Key.Escape);
        Assert.False(capture.IsCapturing);
        Assert.Equal("ALT X", capture.Chord);
        Assert.Equal("ALT X", capture.Text);

        capture.BeginCapture();
        capture.CaptureKeyDown(Key.Delete);
        Assert.Empty(capture.Chord);

        capture.Chord = "SHIFT Y";
        capture.BeginCapture();
        capture.CaptureKeyDown(Key.Back);
        Assert.Empty(capture.Chord);
    }

    [AvaloniaFact]
    public void SpecialKeysUseGlobalHookChordNames()
    {
        var capture = new ShortcutCaptureBox();

        capture.BeginCapture();
        capture.CaptureKeyDown(Key.LeftCtrl);
        capture.CaptureKeyDown(Key.OemPlus);
        capture.CaptureKeyUp(Key.OemPlus);
        capture.CaptureKeyUp(Key.LeftCtrl);

        Assert.Equal("CTRL +", capture.Chord);
    }

    [AvaloniaFact]
    public void CommittedChordWritesThroughTheTwoWayViewModelBinding()
    {
        var saved = string.Empty;
        var viewModel = new InputBindingViewModel(
            GlobalInputActionCatalog.Get(GlobalInputAction.MapZoomIn),
            "CTRL +",
            (_, chord) => saved = chord);
        var capture = new ShortcutCaptureBox();
        capture.Bind(
            ShortcutCaptureBox.ChordProperty,
            new Binding(nameof(InputBindingViewModel.Chord))
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay,
            });

        capture.BeginCapture();
        capture.CaptureKeyDown(Key.LeftAlt);
        capture.CaptureKeyDown(Key.Z);
        capture.CaptureKeyUp(Key.Z);
        capture.CaptureKeyUp(Key.LeftAlt);

        Assert.Equal("ALT Z", viewModel.Chord);
        Assert.Equal("ALT Z", saved);
    }
}
