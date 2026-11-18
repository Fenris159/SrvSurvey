using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Tests.ProfileSync;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class RouteWorkspaceVirtualizationTests
{
    [AvaloniaTheory]
    [InlineData(FollowRouteKind.Standard)]
    [InlineData(FollowRouteKind.FleetCarrier)]
    public async Task LongRoutesKeepRowsBoundedAndScrolledControlsTargetTheCorrectWaypoint(FollowRouteKind kind)
    {
        using var temp = new ProfileSyncTestRoot();
        var store = new FollowRouteStore(temp.Paths.DataDirectory, kind);
        RouteWorkspaceViewModel model = await CreateRouteAsync(store, kind, includeBio: false);
        var window = new RouteWindow(model) { Width = 1000, Height = 800 };
        try
        {
            window.Show();
            Render(window);
            Assert.Equal(200, model.Hops.Count);
            Assert.InRange(RealizedIndices(window).Length, 1, 32);
            Assert.Contains(0, RealizedIndices(window));
            Assert.DoesNotContain(199, RealizedIndices(window));

            ScrollViewer scroller = window.FindControl<ScrollViewer>("RouteHopScroller")!;
            scroller.ScrollToEnd();
            Render(window);
            Assert.Contains(199, RealizedIndices(window));
            Assert.DoesNotContain(0, RealizedIndices(window));
            Assert.InRange(RealizedIndices(window).Length, 1, 32);
            Button copy = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible && Equals(ClipboardCopyBehavior.GetText(button), "Stop 200")
            );
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Stop 200", await window.Clipboard!.TryGetTextAsync());

            CheckBox progress = Assert.Single(
                window.GetVisualDescendants().OfType<CheckBox>(),
                checkbox =>
                    checkbox.IsEffectivelyVisible && checkbox.DataContext is RouteHopItemViewModel { Index: 199 }
            );
            progress.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(window);
            Assert.Equal(200, model.ReachedCount);
            Assert.All(model.Hops, hop => Assert.True(hop.IsReached));

            scroller.ScrollToEnd();
            Render(window);
            if (kind == FollowRouteKind.FleetCarrier)
            {
                scroller.Offset = new Vector(scroller.Extent.Width, scroller.Offset.Y);
            }
            Render(window);
            Button remove = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                button =>
                    button.Classes.Contains("compact-danger")
                    && button.DataContext is RouteHopItemViewModel { Index: 199 }
            );
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(window);
            Assert.Equal(199, model.Hops.Count);
            Assert.Equal("Stop 199", model.Hops[^1].Name);
            Assert.DoesNotContain(model.Hops, hop => hop.Name == "Stop 200");
            Assert.InRange(RealizedIndices(window).Length, 1, 32);
        }
        finally
        {
            window.Close();
            await model.FlushWorkspaceAsync();
        }
    }

    [AvaloniaFact]
    public async Task ScrolledBiologyControlsPreserveAndPersistTheCorrectBodyCompletion()
    {
        using var temp = new ProfileSyncTestRoot();
        var store = new FollowRouteStore(temp.Paths.DataDirectory);
        RouteWorkspaceViewModel model = await CreateRouteAsync(store, FollowRouteKind.Standard, includeBio: true);
        var window = new RouteWindow(model) { Width = 1100, Height = 900 };
        try
        {
            window.Show();
            Render(window);
            CheckBox first = BodyCheckbox(window, "Stop 1 A 1");
            Assert.False(first.IsChecked);
            await CompleteBodyAsync(model, first);
            Render(window);
            Assert.True(BodyCheckbox(window, "Stop 1 A 1").IsChecked);

            ScrollViewer scroller = window.FindControl<ScrollViewer>("RouteHopScroller")!;
            scroller.ScrollToEnd();
            Render(window);
            CheckBox last = BodyCheckbox(window, "Stop 200 A 1");
            Assert.False(last.IsChecked);
            await CompleteBodyAsync(model, last);
            Render(window);
            Assert.True(BodyCheckbox(window, "Stop 200 A 1").IsChecked);
            scroller.ScrollToHome();
            Render(window);
            Assert.True(BodyCheckbox(window, "Stop 1 A 1").IsChecked);

            FollowRouteDocument saved = (await store.LoadAsync("F123")).Route!;
            Assert.Equal(200, saved.Hops.Count);
            Assert.True(Assert.Single(saved.Hops[0].BioTargets).IsCompleted);
            Assert.True(Assert.Single(saved.Hops[199].BioTargets).IsCompleted);
            Assert.Equal("Stop 1 A 1", Assert.Single(saved.Hops[0].BioTargets).BodyName);
            Assert.Equal("Stop 200 A 1", Assert.Single(saved.Hops[199].BioTargets).BodyName);
        }
        finally
        {
            window.Close();
            await model.FlushWorkspaceAsync();
        }
    }

    private static async Task CompleteBodyAsync(RouteWorkspaceViewModel model, CheckBox checkbox)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(RouteWorkspaceViewModel.CanEditWaypoints) && model.CanEditWaypoints)
            {
                finished.TrySetResult();
            }
        }
        model.PropertyChanged += Changed;
        try
        {
            checkbox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            model.PropertyChanged -= Changed;
        }
    }

    private static CheckBox BodyCheckbox(Window window, string name) =>
        Assert.Single(
            window.GetVisualDescendants().OfType<CheckBox>(),
            checkbox => checkbox.DataContext is RouteBioTargetItemViewModel target && target.BodyName == name
        );

    private static int[] RealizedIndices(Window window) =>
        window
            .GetVisualDescendants()
            .OfType<Control>()
            .Select(control => control.DataContext)
            .OfType<RouteHopItemViewModel>()
            .Select(hop => hop.Index)
            .Distinct()
            .ToArray();

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? first = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? settled = window.CaptureRenderedFrame();
    }

    private static async Task<RouteWorkspaceViewModel> CreateRouteAsync(
        FollowRouteStore store,
        FollowRouteKind kind,
        bool includeBio
    )
    {
        FollowRouteDocument route = (await store.CreateNewAsync("F123")) with
        {
            Hops = Enumerable
                .Range(1, 200)
                .Select(index => new FollowRouteHop(
                    "Stop " + index,
                    index,
                    new GalacticCoordinate(index, 0, 0),
                    null,
                    false,
                    false,
                    includeBio && (index == 1 || index == 200)
                        ? [new FollowRouteBioTarget($"Stop {index} A 1", index * 100, ["Bacterium Acies"])]
                        : []
                ))
                .ToArray(),
            SourceSpanshKind =
                kind == FollowRouteKind.FleetCarrier ? SpanshRouteKind.FleetCarrier : SpanshRouteKind.Exobiology,
        };
        await store.SaveAsAsync(route, "Long route");
        var model = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind,
            () => new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)
        );
        await model.UpdateContextAsync("F123", "Origin", 0, new GalacticCoordinate(0, 0, 0));
        return model;
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
