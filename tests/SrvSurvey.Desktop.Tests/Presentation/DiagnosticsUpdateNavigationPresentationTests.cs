using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiagnosticsUpdateNavigationPresentationTests
{
    [AvaloniaFact]
    public async Task ReleaseUpdateNavigationAlignsUpdateCardWithViewportTop()
    {
        var diagnostics = new DiagnosticsView();
        var window = new Window
        {
            Width = 1100,
            Height = 500,
            Content = diagnostics,
        };

        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            var scroller = diagnostics.FindControl<ScrollViewer>(
                "DiagnosticsPageScroller");
            var updateCard = diagnostics.FindControl<Border>(
                "ApplicationUpdatesAnchor");
            Assert.NotNull(scroller);
            Assert.NotNull(updateCard);
            scroller.Offset = new Vector(
                0,
                scroller.Extent.Height - scroller.Viewport.Height);

            diagnostics.ScrollToApplicationUpdates();
            await Dispatcher.UIThread.InvokeAsync(
                static () => { },
                DispatcherPriority.Background);

            var origin = updateCard.TranslatePoint(default, scroller);
            Assert.NotNull(origin);
            Assert.InRange(origin.Value.Y, -0.5, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReleaseUpdateNavigationWaitsForHiddenDiagnosticsLayout()
    {
        var diagnostics = new DiagnosticsView();
        var alternatePage = new Border();
        var pages = new Grid
        {
            Children =
            {
                diagnostics,
                alternatePage,
            },
        };
        var window = new Window
        {
            Width = 1100,
            Height = 500,
            Content = pages,
        };

        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            var scroller = diagnostics.FindControl<ScrollViewer>(
                "DiagnosticsPageScroller");
            var updateCard = diagnostics.FindControl<Border>(
                "ApplicationUpdatesAnchor");
            Assert.NotNull(scroller);
            Assert.NotNull(updateCard);
            scroller.Offset = new Vector(
                0,
                scroller.Extent.Height - scroller.Viewport.Height);
            diagnostics.IsVisible = false;
            alternatePage.IsVisible = true;
            Assert.NotNull(window.CaptureRenderedFrame());

            diagnostics.ScrollToApplicationUpdates();
            await Dispatcher.UIThread.InvokeAsync(
                static () => { },
                DispatcherPriority.Background);
            alternatePage.IsVisible = false;
            diagnostics.IsVisible = true;
            Assert.NotNull(window.CaptureRenderedFrame());
            await Dispatcher.UIThread.InvokeAsync(
                static () => { },
                DispatcherPriority.Background);

            var origin = updateCard.TranslatePoint(default, scroller);
            Assert.NotNull(origin);
            Assert.InRange(origin.Value.Y, -0.5, 0.5);
        }
        finally
        {
            window.Close();
        }
    }
}
