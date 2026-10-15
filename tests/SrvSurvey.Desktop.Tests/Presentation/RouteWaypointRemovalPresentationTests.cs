using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class RouteWaypointRemovalPresentationTests
{
    [AvaloniaTheory]
    [InlineData(FollowRouteKind.Standard)]
    [InlineData(FollowRouteKind.FleetCarrier)]
    public async Task RowTrashRemovesOnlyThatWaypointAndUndoRestoresIt(FollowRouteKind kind)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-waypoint-removal-{Guid.NewGuid():N}");
        var store = new FollowRouteStore(directory, kind);
        FollowRouteDocument original = (await store.CreateNewAsync("F123")) with
        {
            Hops =
            [
                new FollowRouteHop(
                    "Sol",
                    1,
                    new GalacticCoordinate(0, 0, 0),
                    "Keep these notes",
                    true,
                    false,
                    [new FollowRouteBioTarget("Sol A 1", 10, ["Bacteria"], IsCompleted: true)]
                ),
                new FollowRouteHop("Sol", 2, new GalacticCoordinate(3, 4, 0), null, false, false),
                new FollowRouteHop("Achenar", 3, new GalacticCoordinate(3, 4, 12), null, false, true),
            ],
            IsActive = true,
            AutoCopy = true,
            LastReachedIndex = 0,
            SourceSpanshKind =
                kind == FollowRouteKind.FleetCarrier ? SpanshRouteKind.FleetCarrier : SpanshRouteKind.Exobiology,
        };
        await store.SaveAsAsync(original, "Row removal");
        var viewModel = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind
        );
        await viewModel.UpdateContextAsync("F123", "Sol", 1, new GalacticCoordinate(0, 0, 0));
        FollowRouteDocument before = viewModel.CreateSnapshot()!;
        var window = new RouteWindow(viewModel);
        try
        {
            window.Show();
            Render(window, kind, "initial");
            if (kind == FollowRouteKind.FleetCarrier)
            {
                ScrollViewer scroller = window.FindControl<ScrollViewer>("RouteHopScroller")!;
                scroller.Offset = new Avalonia.Vector(scroller.Extent.Width, 0);
            }
            Render(window, kind, "before");
            Button[] buttons = RowTrashButtons(window);

            Assert.Equal(3, buttons.Length);
            foreach (Button button in buttons)
            {
                Grid row = Assert.IsType<Grid>(button.Parent);
                Assert.Equal(row.ColumnDefinitions.Count - 1, Grid.GetColumn(button));
                Assert.True(button.IsEnabled);
                Assert.Equal(44, button.Bounds.Width);
                Assert.Equal(row.Bounds.Width, button.Bounds.Right);
            }

            viewModel.NotesCommand.Execute(null);
            Render(window, kind, "notes");
            Assert.All(buttons, button => Assert.False(button.IsEnabled));
            viewModel.DismissDialogs();
            Render(window, kind, "ready");
            Assert.All(buttons, button => Assert.True(button.IsEnabled));

            Button trash = Assert.Single(buttons, button => button.DataContext is RouteHopItemViewModel { Index: 1 });
            trash.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(window, kind, "after");

            Assert.Equal(["Sol", "Achenar"], viewModel.Hops.Select(hop => hop.Name));
            Assert.Equal([1, 2], viewModel.Hops.Select(hop => hop.Number));
            Assert.Equal(1, viewModel.ReachedCount);
            Assert.Equal("Achenar", viewModel.NextHopName);
            Assert.Equal(2, RowTrashButtons(window).Length);
            Assert.True(viewModel.IsDirty);
            FollowRouteDocument snapshot = viewModel.CreateSnapshot()!;
            Assert.Equal(before.Hops[0], snapshot.Hops[0]);
            Assert.Equal(before.Hops[2], snapshot.Hops[1]);
            Assert.Equal(original.SourceSpanshKind, snapshot.SourceSpanshKind);
            Assert.Equal(3, (await store.LoadAsync("F123")).Route!.Hops.Count);

            await viewModel.DiscardAsync();
            Render(window, kind, "restored");

            Assert.Equal(3, RowTrashButtons(window).Length);
            Assert.Equal(["Sol", "Sol", "Achenar"], viewModel.Hops.Select(hop => hop.Name));
            Assert.False(viewModel.IsDirty);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    private static Button[] RowTrashButtons(Window window)
    {
        return window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(button =>
                button.IsEffectivelyVisible
                && button.Classes.Contains("compact-danger")
                && button.DataContext is RouteHopItemViewModel
            )
            .ToArray();
    }

    private static void Render(Window window, FollowRouteKind kind, string stage)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_ROUTE_RENDER_OUTPUT");
        if (output is not null && stage is "before" or "after")
        {
            Directory.CreateDirectory(output);
            using FileStream stream = File.Create(Path.Combine(output, $"{kind}-{stage}.png"));
            frame.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class EmptyResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptySpanshClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }
}
