using Avalonia;
using Avalonia.Input;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media;
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

    [AvaloniaFact]
    public void ControllerChordPreviewsAndWritesThroughTheBinding()
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
        Assert.True(ShortcutCaptureSession.TryCapture(
            new ControllerInputChange("B2", IsPressed: true)));
        Assert.True(ShortcutCaptureSession.TryCapture(
            new ControllerInputChange("B1", IsPressed: true)));
        Assert.Equal("B1 B2", capture.Text);
        Assert.Equal("CTRL +", capture.Chord);

        Assert.True(ShortcutCaptureSession.TryCapture(
            new ControllerInputChange("B2", IsPressed: false)));

        Assert.False(capture.IsCapturing);
        Assert.Equal("B1 B2", viewModel.Chord);
        Assert.Equal("B1 B2", saved);
    }

    [AvaloniaFact]
    public void SecondEscapeReleasesFocusAfterCancellingCapture()
    {
        var capture = new ShortcutCaptureBox { Chord = "ALT X" };
        var window = new Window { Content = capture };
        try
        {
            window.Show();
            capture.Focus();
            capture.CaptureKeyDown(Key.LeftCtrl);

            capture.CaptureKeyDown(Key.Escape);
            Assert.True(capture.IsFocused);
            Assert.False(capture.IsCapturing);
            Assert.Equal("ALT X", capture.Chord);

            capture.CaptureKeyDown(Key.Escape);

            Assert.False(capture.IsFocused);
            Assert.False(capture.IsCapturing);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CommittedEntryDoesNotRestartUntilItIsClickedAgain()
    {
        var capture = new ShortcutCaptureBox { Chord = "ALT X" };
        var window = new Window { Content = capture };
        try
        {
            window.Show();
            capture.Focus();
            capture.CaptureKeyDown(Key.LeftCtrl);
            capture.CaptureKeyDown(Key.K);
            capture.CaptureKeyUp(Key.K);
            capture.CaptureKeyUp(Key.LeftCtrl);
            Assert.False(capture.IsCapturing);
            Assert.True(capture.IsFocused);

            capture.CaptureKeyDown(Key.Z);

            Assert.False(capture.IsCapturing);
            Assert.Equal("CTRL K", capture.Chord);
            Assert.Equal("CTRL K", capture.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingNonFocusableSpaceCancelsCaptureAndReleasesFocus()
    {
        var capture = new ShortcutCaptureBox
        {
            Height = 36,
            Chord = "ALT X",
        };
        var window = new Window
        {
            Width = 320,
            Height = 200,
            Content = new StackPanel
            {
                Children =
                {
                    capture,
                    new Border
                    {
                        Height = 120,
                        Background = Brushes.Transparent,
                    },
                },
            },
        };
        try
        {
            window.Show();
            capture.Focus();
            capture.CaptureKeyDown(Key.LeftCtrl);
            Assert.True(capture.IsCapturing);

            window.MouseDown(
                new Point(20, 100),
                MouseButton.Left,
                RawInputModifiers.None);
            window.MouseUp(
                new Point(20, 100),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.False(capture.IsCapturing);
            Assert.False(capture.IsFocused);
            Assert.Equal("ALT X", capture.Chord);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeAfterCommitRevertsBeforeASecondEscapeReleasesFocus()
    {
        var capture = new ShortcutCaptureBox { Chord = "ALT X" };
        var window = new Window { Content = capture };
        try
        {
            window.Show();
            capture.Focus();
            capture.CaptureKeyDown(Key.LeftCtrl);
            capture.CaptureKeyDown(Key.K);
            capture.CaptureKeyUp(Key.K);
            capture.CaptureKeyUp(Key.LeftCtrl);
            Assert.Equal("CTRL K", capture.Chord);
            Assert.True(capture.IsFocused);

            capture.CaptureKeyDown(Key.Escape);

            Assert.Equal("ALT X", capture.Chord);
            Assert.Equal("ALT X", capture.Text);
            Assert.True(capture.IsFocused);
            Assert.False(capture.IsCapturing);

            capture.CaptureKeyDown(Key.Escape);
            Assert.False(capture.IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingNonFocusableSpaceAfterCommitAcceptsAndReleasesFocus()
    {
        var capture = new ShortcutCaptureBox
        {
            Height = 36,
            Chord = "ALT X",
        };
        var window = new Window
        {
            Width = 320,
            Height = 200,
            Content = new StackPanel
            {
                Children =
                {
                    capture,
                    new Border
                    {
                        Height = 120,
                        Background = Brushes.Transparent,
                    },
                },
            },
        };
        try
        {
            window.Show();
            capture.Focus();
            capture.CaptureKeyDown(Key.LeftCtrl);
            capture.CaptureKeyDown(Key.K);
            capture.CaptureKeyUp(Key.K);
            capture.CaptureKeyUp(Key.LeftCtrl);
            Assert.Equal("CTRL K", capture.Chord);
            Assert.True(capture.IsFocused);

            window.MouseDown(
                new Point(20, 100),
                MouseButton.Left,
                RawInputModifiers.None);
            window.MouseUp(
                new Point(20, 100),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.Equal("CTRL K", capture.Chord);
            Assert.False(capture.IsFocused);
            Assert.False(capture.IsCapturing);
        }
        finally
        {
            window.Close();
        }
    }
}
