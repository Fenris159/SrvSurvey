using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using Xunit;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayResizeInteractionTests
{
    private static readonly int[] ScalePercents = [-25, 0, 50];
    private static readonly bool[] ConfiguredSizes = [false, true];
    private static readonly int[] PointerSteps = [1, 2, 3, 4, 5, 6, 7, 8, 6, 4, 2, 0];

    /// <summary>Manual grips track each pointer sample; fully automatic panels keep their grip hidden.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(ResizeCases))]
    public void ResizePolicyHidesAutomaticGripsAndTracksManualPointerSamples(
        string plotterName,
        int percent,
        bool configured
    )
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(plotterName));
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(percent), null, 1);
            if (configured)
            {
                preview.ConfigureSize(new OverlayPanelSize(420, 600));
            }
            preview.Show();
            using WriteableBitmap? first = preview.CaptureRenderedFrame();
            Control panel = preview.RuntimePresentation!;
            Size original = panel.Bounds.Size;
            Thumb grip = preview.FindControl<Thumb>("PanelResizeThumb")!;
            if (panel is IOverlayPanelSizePolicy { AutoWidth: true, AutoHeight: true })
            {
                Assert.False(grip.IsVisible);
                Assert.True(double.IsNaN(panel.Width));
                Assert.True(double.IsNaN(panel.Height));
                return;
            }
            Assert.True(grip.IsVisible);
            Point start = grip.TranslatePoint(new Point(11, 11), preview)!.Value;
            preview.MouseMove(start, RawInputModifiers.None);
            preview.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            foreach (int step in PointerSteps)
            {
                Vector delta = new(step * 12, step * 8);
                Point pointer = start + delta;
                preview.MouseMove(pointer, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap? frame = preview.CaptureRenderedFrame();
                Point actual = grip.TranslatePoint(new Point(11, 11), preview)!.Value;
                Assert.True(
                    Math.Abs(actual.X - pointer.X) <= 1
                        && (
                            panel is IOverlayPanelSizePolicy { AutoHeight: true } || Math.Abs(actual.Y - pointer.Y) <= 1
                        ),
                    $"{plotterName} step {step}: pointer={pointer}, grip={actual}; original={original}, panel={panel.Bounds.Size}"
                );
            }
            preview.MouseUp(start, MouseButton.Left, RawInputModifiers.None);
            Size releasedSize = panel.Bounds.Size;
            preview.MouseMove(start + new Vector(160, 120), RawInputModifiers.None);
            using WriteableBitmap? released = preview.CaptureRenderedFrame();
            Assert.Equal(releasedSize, panel.Bounds.Size);
        }
        finally
        {
            preview.Close();
        }
    }

    /// <summary>The visible panel background follows the resized frame without leaving an unfilled span.</summary>
    [AvaloniaTheory]
    [InlineData("PlotBuildCommodities")]
    [InlineData("PlotMassacre")]
    [InlineData("PlotSysStatus")]
    [InlineData("PlotFootCombat")]
    public void VisiblePanelFillsItsResizedFrame(string name)
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(name));
        try
        {
            preview.ConfigureSize(new(600, 400));
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            UserControl panel = Assert.IsType<UserControl>(preview.RuntimePresentation, exactMatch: false);
            Border background = Assert.IsType<Border>(panel.Content);
            Assert.Equal(panel.Bounds.Size, background.Bounds.Size);
        }
        finally
        {
            preview.Close();
        }
    }

    /// <summary>A narrow viewport wraps mission text while retaining visible kill counts.</summary>
    [AvaloniaFact]
    public void NarrowMassacrePanelRetainsMissionCounts()
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotMassacre"));
        try
        {
            preview.Show();
            OverlayThemeResources.ApplyPanelViewportWidth(preview.RuntimePresentation, 160);
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Control panel = preview.RuntimePresentation!;
            TextBlock[] counts = panel
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Classes.Contains("type-value"))
                .ToArray();
            Assert.NotEmpty(counts);
            foreach (TextBlock count in counts)
            {
                Point position = count.TranslatePoint(default, panel)!.Value;
                Assert.True(
                    position.X + count.Bounds.Width <= panel.Bounds.Width - 4,
                    $"Mission count extends beyond resized panel: {position.X + count.Bounds.Width} > {panel.Bounds.Width - 4}"
                );
                Assert.True(count.Bounds.Width >= count.TextLayout.WidthIncludingTrailingWhitespace);
            }
        }
        finally
        {
            preview.Close();
        }
    }

    /// <summary>Checks every shared presentation at natural and explicit sizes with reduced and enlarged scaling.</summary>
    public static IEnumerable<TheoryDataRow<string, int, bool>> ResizeCases() =>
        from definition in OverlayLayoutCatalog.Supported
        from percent in ScalePercents
        from configured in ConfiguredSizes
        select new TheoryDataRow<string, int, bool>(definition.Name, percent, configured);
}
