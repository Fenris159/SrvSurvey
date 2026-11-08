using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationProjectPreviewTests
{
    /// <summary>The manifest spans the complete header, wraps naturally and repeats in loading-specific project details.</summary>
    [AvaloniaTheory]
    [InlineData(1180)]
    [InlineData(700)]
    public void LoadingPreviewWrapsBuildingListAcrossTheHeaderAndDetails(int width)
    {
        const string expected =
            "5x Alala-layout Military Hub, 1x Alastor-layout Military Installation, 2x Apollo-layout Orbis Starport, 2x Ares-layout Military Hub, 3x Artemis-layout Orbis Starport, 1x Asteroid-layout Asteroid Base, 4x Vacuna-layout Military Installation";
        string preparation =
            "{\"alala\":5,\"alastor?\":1,\"apollo\":2,\"artemis\":3,\"ares\":2,\"asteroid\":1,\"vacuna\":4}";
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (_, _) =>
                    Task.FromResult<ColonizationProjectPreviewData?>(
                        ColonizationProjectPreviewViewModelTests.LoadingPreviewData(preparation)
                    )
            ),
            "loading"
        );
        var window = new ColonizationProjectPreviewWindow(model) { Width = width, Height = 850 };
        ApplyLoadingPreviewTheme(window);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(expected, model.Subtitle);
            TextBlock subtitle = window.FindControl<TextBlock>("PreviewSubtitle")!;
            Assert.Equal(expected, subtitle.Text);
            ScrollViewer subtitleScroll = window.FindControl<ScrollViewer>("PreviewSubtitleScroller")!;
            Grid header = Assert.IsType<Grid>(Assert.IsType<Border>(subtitleScroll.Parent).Parent);
            Assert.Equal(header.Bounds.Width, subtitleScroll.Bounds.Width, 1);
            Assert.Equal(subtitleScroll.Viewport.Width, subtitle.Bounds.Width + subtitle.Margin.Right, 1);
            Assert.Equal(TextWrapping.Wrap, subtitle.TextWrapping);
            Assert.True(subtitle.TextLayout.TextLines.Count >= 2);
            Assert.True(window.FindControl<Button>("RefreshButton")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<Button>("ExportCsvButton")!.IsEffectivelyVisible);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-loading-preview-{width}.png"),
                PngBitmapEncoderOptions.Default
            );

            ItemsControl details = Assert.Single(
                window.GetVisualDescendants().OfType<ItemsControl>(),
                control => ReferenceEquals(control.ItemsSource, model.Details)
            );
            details.BringIntoView();
            using WriteableBitmap? detailFrame = window.CaptureRenderedFrame();
            Assert.NotNull(detailFrame);
            Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Buildings:");
            TextBlock buildings = Assert.Single(
                details.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == expected
            );
            Assert.True(buildings.TextLayout.TextLines.Count >= 2);
            Grid buildingField = Assert.IsType<Grid>(buildings.Parent);
            Assert.Equal(buildingField.Bounds.Width, buildings.Bounds.Width, 1);
            Assert.DoesNotContain(
                details.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text is "System" or "System address" or "Body" or "Faction"
            );
            detailFrame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-loading-preview-details-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            preparation = "{\"alastor?\":1}";
            window.FindControl<Button>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            details.BringIntoView();
            using WriteableBitmap? singleBuilding = window.CaptureRenderedFrame();
            Assert.NotNull(singleBuilding);
            Assert.Equal("1x Alastor-layout Military Installation", subtitle.Text);
            Assert.Equal(
                "1x Alastor-layout Military Installation",
                Assert.Single(model.Details, field => field.Label == "Buildings:").Value
            );
            singleBuilding.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-loading-preview-single-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ALongLoadingManifestKeepsThePreviewUsableAtMinimumWindowSize()
    {
        string preparation = JsonSerializer.Serialize(
            ColonizationBuildCatalog
                .LoadEmbedded()
                .Builds.SelectMany(build => build.Layouts)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(layout => layout, _ => 1)
        );
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (_, _) =>
                    Task.FromResult<ColonizationProjectPreviewData?>(
                        ColonizationProjectPreviewViewModelTests.LoadingPreviewData(preparation)
                    )
            ),
            "loading"
        );
        var window = new ColonizationProjectPreviewWindow(model) { Width = 700, Height = 500 };
        ApplyLoadingPreviewTheme(window);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ScrollViewer body = window
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .First(scroll =>
                    scroll.GetVisualDescendants().Contains(window.FindControl<ItemsControl>("CommodityRows")!)
                );
            Assert.True(body.Viewport.Height >= 100, $"Manifest left {body.Viewport.Height} pixels for the report.");
            TextBlock subtitle = window.FindControl<TextBlock>("PreviewSubtitle")!;
            Assert.Contains("1x Alastor-layout Military Installation", subtitle.Text);
            Assert.Contains("1x Vacuna-layout Military Installation", subtitle.Text);
            ScrollViewer manifest = Assert.Single(subtitle.GetVisualAncestors().OfType<ScrollViewer>());
            Assert.True(manifest.Extent.Height > manifest.Viewport.Height);
            manifest.ScrollToEnd();
            using WriteableBitmap? scrolled = window.CaptureRenderedFrame();
            Assert.NotNull(scrolled);
            Assert.True(manifest.Offset.Y > 0);
            Assert.Equal(manifest.Extent.Height, manifest.Offset.Y + manifest.Viewport.Height, 1);
            foreach (string buttonName in new[] { "RefreshButton", "ExportCsvButton" })
            {
                Button button = window.FindControl<Button>(buttonName)!;
                Assert.InRange(
                    button.TranslatePoint(default, window)!.Value.Y,
                    0,
                    window.ClientSize.Height - button.Bounds.Height
                );
            }
            scrolled.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-kilo-loading-minimum.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Uses the user's existing monochrome Raven palette without changing application-wide test resources.</summary>
    private static void ApplyLoadingPreviewTheme(Window window)
    {
        RavenThemeDefinition theme = RavenThemeCatalog.Get("monochrome-dark");
        window.RequestedThemeVariant = ThemeVariant.Dark;
        foreach (
            KeyValuePair<string, string> entry in new Dictionary<string, string>
            {
                ["RavenWindowBrush"] = theme.WindowColor,
                ["RavenSidebarBrush"] = theme.SidebarColor,
                ["RavenSurfaceBrush"] = theme.SurfaceColor,
                ["RavenRaisedSurfaceBrush"] = theme.RaisedSurfaceColor,
                ["RavenTextBrush"] = theme.TextColor,
                ["RavenMutedTextBrush"] = theme.MutedTextColor,
                ["RavenAccentBrush"] = theme.AccentColor,
                ["RavenBorderBrush"] = theme.BorderColor,
                ["RavenControlAccentBrush"] = theme.ControlAccentColor,
                ["RavenSecondaryFillBrush"] = theme.SecondaryFillColor,
                ["RavenAccentForegroundBrush"] = theme.AccentForegroundColor,
            }
        )
        {
            window.Resources[entry.Key] = new SolidColorBrush(Color.Parse(entry.Value));
        }
    }
}
