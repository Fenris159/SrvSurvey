using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MineMapViewInteractionTests
{
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
        using var main = MainWindowViewModelTestBuilder.Create(null, builder => builder.WithAppDataPaths(paths));
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
            var button = FindFavoriteButton(view);
            var clickPoint =
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

    private static void SeedSurvey(string directory)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
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
