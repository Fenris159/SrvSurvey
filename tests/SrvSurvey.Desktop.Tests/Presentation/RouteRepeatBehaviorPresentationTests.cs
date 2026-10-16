using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class RouteRepeatBehaviorPresentationTests
{
    [AvaloniaTheory]
    [InlineData(FollowRouteKind.Standard)]
    [InlineData(FollowRouteKind.FleetCarrier)]
    public async Task RepeatCheckboxesClearEachOtherAndAllowBothToBeOff(FollowRouteKind kind)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-repeat-ui-{Guid.NewGuid():N}");
        var store = new FollowRouteStore(directory, kind);
        var model = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind
        );
        await model.UpdateContextAsync("F123", "Elsewhere", 99, null);
        await model.ImportNamesAsync(["A", "B", "C", "A"]);
        var window = new RouteWindow(model);
        try
        {
            window.Show();
            Render(window);
            CheckBox fromStart = window.FindControl<CheckBox>("RepeatFromStartCheckBox")!;
            CheckBox reverse = window.FindControl<CheckBox>("RepeatAndReverseCheckBox")!;
            Assert.Equal("Repeat from start", fromStart.Content);
            Assert.Equal("Repeat and reverse", reverse.Content);
            Assert.True(fromStart.IsEffectivelyVisible);
            Assert.True(reverse.IsEffectivelyVisible);

            Click(window, fromStart);
            Assert.True(model.RepeatFromStart);
            Assert.True(fromStart.IsChecked);
            Assert.False(reverse.IsChecked);
            Click(window, reverse);
            Assert.True(model.RepeatAndReverse);
            Assert.False(model.RepeatFromStart);
            Assert.False(fromStart.IsChecked);
            Assert.True(reverse.IsChecked);
            Click(window, reverse);
            Assert.False(model.RepeatAndReverse);
            Assert.False(fromStart.IsChecked);
            Assert.False(reverse.IsChecked);
            Click(window, reverse);
            Click(window, fromStart);
            Assert.True(model.RepeatFromStart);
            Assert.False(reverse.IsChecked);
            Click(window, fromStart);
            Assert.False(model.RepeatFromStart);
            Assert.False(reverse.IsChecked);
            if (kind == FollowRouteKind.FleetCarrier)
            {
                model.RemoveHop(model.Hops[^1]);
                Render(window);
                Assert.False(fromStart.IsEnabled);
                Click(window, fromStart);
                Assert.False(model.RepeatFromStart);
                Assert.False(fromStart.IsChecked);
                Assert.True(reverse.IsEnabled);
            }
        }
        finally
        {
            window.Close();
            await model.FlushWorkspaceAsync();
            Directory.Delete(directory, true);
        }
    }

    private static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(12, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    private static void Render(Window window)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private sealed class EmptyResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class EmptySpanshClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
    }
}
