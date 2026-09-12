using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MineMapViewInteractionTests
{
    [AvaloniaFact]
    public void MineMapControlPropertiesRoundTripAndRenderACompleteMap()
    {
        MineMapSurvey survey = RenderedSurvey();
        var playerLocation = new SurfaceCoordinate(14.261, -79.329);
        var planningCenter = new SurfaceCoordinate(14.262, -79.328);
        IReadOnlySet<string> materials = new HashSet<string>(["Ruby"], StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<Guid> markerIds = new HashSet<Guid>(survey.Markers.Select(marker => marker.Id));
        var control = new MineMapControl
        {
            Survey = survey,
            PlayerLocation = playerLocation,
            PlayerHeading = 120,
            ViewportZoom = 2,
            AllowViewportInteraction = true,
            ShowMarkerLabels = true,
            VisibleMaterials = materials,
            VisibleMarkerIds = markerIds,
            PlanningCircleCenter = planningCenter,
            MapBackground = Brushes.Black,
            GridBrush = Brushes.Gray,
            AccentBrush = Brushes.Cyan,
            PlayerBrush = Brushes.LimeGreen,
            TextBrush = Brushes.White,
            ZoneBrush = Brushes.Gold,
        };
        var window = new Window
        {
            Content = control,
            Width = 500,
            Height = 500,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Same(survey, control.Survey);
            Assert.Equal(playerLocation, control.PlayerLocation);
            Assert.Equal(120, control.PlayerHeading);
            Assert.Equal(2, control.ViewportZoom);
            Assert.True(control.AllowViewportInteraction);
            Assert.True(control.ShowMarkerLabels);
            Assert.Same(materials, control.VisibleMaterials);
            Assert.Same(markerIds, control.VisibleMarkerIds);
            Assert.Equal(planningCenter, control.PlanningCircleCenter);
            Assert.Same(Brushes.Black, control.MapBackground);
            Assert.Same(Brushes.Gray, control.GridBrush);
            Assert.Same(Brushes.Cyan, control.AccentBrush);
            Assert.Same(Brushes.LimeGreen, control.PlayerBrush);
            Assert.Same(Brushes.White, control.TextBrush);
            Assert.Same(Brushes.Gold, control.ZoneBrush);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MineMapControlSupportsWheelPanAndPlanningCirclePointerInteractions()
    {
        var control = new MineMapControl
        {
            Survey = RenderedSurvey(),
            AllowViewportInteraction = true,
            Width = 500,
            Height = 500,
        };
        var window = new Window
        {
            Content = control,
            Width = 500,
            Height = 500,
        };
        var center = new Point(250, 250);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.MouseWheel(center, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.True(control.ViewportZoom > 1);

            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(new Point(300, 275), RawInputModifiers.None);
            window.MouseUp(new Point(300, 275), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(center, MouseButton.Right, RawInputModifiers.None);
            Assert.NotNull(control.PlanningCircleCenter);
            SurfaceCoordinate? initialPlanningCenter = control.PlanningCircleCenter;
            window.MouseMove(new Point(320, 250), RawInputModifiers.None);
            window.MouseUp(new Point(320, 250), MouseButton.Right, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(initialPlanningCenter, control.PlanningCircleCenter);

            window.MouseDown(center, MouseButton.Right, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Right, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(control.PlanningCircleCenter);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingSurfaceMapStarPersistsFavoriteAndRefreshesToSolidGlyph()
    {
        using var directory = new TemporaryDirectory();
        var paths = new AppDataPaths(
            Path.Combine(directory.Path, "config"),
            Path.Combine(directory.Path, "data"),
            Path.Combine(directory.Path, "cache"),
            []
        );
        SeedSurvey(paths.DataDirectory);
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths)
        );
        var view = new MineMapView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1200,
            Height = 900,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Button button = FindFavoriteButton(view);
            Point clickPoint =
                button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("The favorite button was not arranged in the test window.");
            window.MouseMove(clickPoint, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.IsPointerOver);
            Assert.NotNull(button.Command);
            window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(clickPoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(Assert.Single(new BookmarkCatalog(paths.DataDirectory).Items).IsFavorite);
            Assert.Equal("★", Assert.IsType<TextBlock>(FindFavoriteButton(view).Content).Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static Button FindFavoriteButton(MineMapView view) =>
        Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            button =>
                string.Equals(ToolTip.GetTip(button)?.ToString(), "Add or remove favorite", StringComparison.Ordinal)
        );

    private static MineMapSurvey RenderedSurvey()
    {
        var center = new SurfaceCoordinate(14.2609, -79.3291);
        return new MineMapSurvey
        {
            SystemName = "LTT 4428",
            BodyName = "LTT 4428 D 5 a",
            LocationSignal = 20,
            LocationRadiusMeters = 6_380,
            PlanetRadiusMeters = 855_573.1875,
            Center = center,
            Markers =
            [
                new MineMapMarker
                {
                    Material = "Ruby",
                    MineralAmount = MineMapRating.High,
                    Density = MineMapRating.Medium,
                    Location = MineMapService.GetDestination(center, 45, 1_000, 855_573.1875),
                },
            ],
        };
    }

    private static void SeedSurvey(string directory)
    {
        var id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var survey = new MineMapSurvey
        {
            Id = id,
            FrontierId = "F123",
            CommanderName = "Fenris",
            SystemName = "LTT 4428",
            SystemAddress = 2_656_194_005_355,
            SystemPosition = new GalacticCoordinate(75.15625, 15.1875, 33.34375),
            BodyId = 30,
            BodyName = "LTT 4428 D 5 a",
            BodyType = "Rocky body",
            ArrivalDistanceLs = 19_797,
            LocationSignal = 20,
            LocationRadiusMeters = 6_380,
            PlanetRadiusMeters = 855_573.1875,
            Center = new SurfaceCoordinate(14.2609, -79.3291),
            CreatedAt = now,
            UpdatedAt = now,
        };
        new BookmarkCatalog(directory).Save(
            new GalacticBookmark
            {
                Id = id,
                System = survey.SystemName,
                Body = survey.BodyName,
                Position = survey.SystemPosition,
                CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
                SurfaceMiningMap = survey,
                Updated = now,
            }
        );
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SrvSurvey-MineMap-Interaction-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
