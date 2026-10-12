using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ExplorationPoiOverlayCoordinatorTests
{
    /// <summary>Verifies passive window uses shared presentation and honors visibility suppression.</summary>
    [AvaloniaFact]
    public void PassiveWindowUsesSharedPresentationAndHonorsVisibilitySuppression()
    {
        using ExplorationPoiViewModel model = OverlayEditorPreviewFactories.CreateExplorationPois();
        model.UpdateContext("Great Annihilator", 2587943, null, new EliteStatus { Flags = StatusFlags.InMainShip }, []);
        var registry = new OverlayWindowRegistry();
        using var harness = new HostedOverlayTestHarness(registry);
        using var coordinator = new ExplorationPoiOverlayCoordinator(model, harness.Session);
        harness.Tick();
        Assert.True(coordinator.IsVisible);
        Window window = Assert.Single(harness.PreparedWindows);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Great Annihilator");
        Assert.Single(
            window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == "Great Annihilator"
        );
        Assert.Single(
            window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == "Azura Initiative"
        );
        Assert.All(
            window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(t => t.Text is "Great Annihilator" or "Azura Initiative"),
            title => Assert.Contains("type-title", title.Classes)
        );
        using Avalonia.Media.Imaging.WriteableBitmap? capture = window.CaptureRenderedFrame();
        Assert.NotNull(capture);
        string? preview = Environment.GetEnvironmentVariable("SRVSURVEY_POI_OVERLAY_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(preview))
        {
            capture.Save(preview, PngBitmapEncoderOptions.Default);
        }
        coordinator.SetSuppressed(true);
        Assert.False(coordinator.IsVisible);
        coordinator.SetSuppressed(false);
        Assert.True(coordinator.IsVisible);
        registry.SetUserVisibility("PlotExplorationPois", false);
        harness.Tick();
        Assert.False(coordinator.IsVisible);
        Assert.True(model.ShouldShow);
        registry.SetUserVisibility("PlotExplorationPois", true);
        harness.Tick();
        Assert.True(coordinator.IsVisible);
        model.UpdateContext("Sol", 1, null, new EliteStatus { Flags = StatusFlags.InMainShip }, []);
        Assert.False(coordinator.IsVisible);
    }

    /// <summary>Verifies galaxy map preview uses selected poi target and titles without repeating names.</summary>
    [AvaloniaFact]
    public void GalaxyMapPreviewUsesSelectedPoiTargetAndTitlesWithoutRepeatingNames()
    {
        using GalaxyMapOverlayViewModel model = OverlayEditorPreviewFactories.CreateGalaxyMap();
        Assert.Equal("SELECTED", model.PrimarySystemDisplay.Label);
        Assert.Equal("Great Annihilator", model.PrimarySystemDisplay.Name);
        Assert.Equal(2, model.PrimaryPois.Count);
        Assert.False(model.PrimaryPois[0].ShowOverlayTitle);
        Assert.True(model.PrimaryPois[1].ShowOverlayTitle);
        var window = new Window
        {
            Content = new GalaxyMapOverlayPresentation { DataContext = model },
            Width = 240,
            Height = 800,
        };
        OverlayThemeResources.Apply(window);
        window.Resources["RavenOverlayTitleFontSize"] = 23d;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBlock[] names = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.Text is "Great Annihilator" or "Azura Initiative")
                .ToArray();
            Assert.Equal(2, names.Length);
            Assert.Equal(2, names.Select(t => t.Text).Distinct().Count());
            Assert.All(
                names,
                title =>
                {
                    Assert.Contains("type-title", title.Classes);
                    Assert.Equal(23, title.FontSize);
                }
            );
            using WriteableBitmap? capture = window.CaptureRenderedFrame();
            Assert.NotNull(capture);
            string? preview = Environment.GetEnvironmentVariable("SRVSURVEY_GALMAP_POI_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(preview))
            {
                capture.Save(preview, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
