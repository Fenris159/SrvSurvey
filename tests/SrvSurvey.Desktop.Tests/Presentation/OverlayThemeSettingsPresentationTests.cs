using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayThemeSettingsPresentationTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-theme-presentation-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public void SharedSettingsTemplateRendersCompactSingleRowColorEditors()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var overlayTheme = new OverlayThemeSettingsViewModel(
            new LegacyOverlayThemeStore(
                Path.Combine(temporaryDirectory, "theme.json")),
            new OverlayThemeStateStore(
                Path.Combine(temporaryDirectory, "states.json")),
            initialTheme: LegacyOverlayThemeStore.CreateDefault());
        using var viewModel = new MainWindowViewModel(
            Path.Combine(temporaryDirectory, "journals"),
            new MainWindowViewModelOptions
            {
                AppDataPaths = new AppDataPaths(
                    Path.Combine(temporaryDirectory, "config"),
                    Path.Combine(temporaryDirectory, "data"),
                    Path.Combine(temporaryDirectory, "cache"),
                    []),
                OverlayThemeSettings = overlayTheme,
            });
        var settings = new SettingsView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1100,
            Height = 800,
            Content = settings,
        };

        try
        {
            window.Show();
            var overlayTab = settings.GetLogicalDescendants()
                .OfType<TabItem>()
                .Single(tab => string.Equals(
                    tab.Header?.ToString(),
                    "In-game overlay appearance",
                    StringComparison.Ordinal));
            overlayTab.IsSelected = true;
            var frame = window.CaptureRenderedFrame();
            var presetCard = settings.FindControl<Border>("OverlayThemePresetCard");
            var actionsCard = settings.FindControl<Border>("OverlayThemeActionsCard");
            var colorEditorList = settings.FindControl<ItemsControl>(
                "OverlayThemeColorEditorList");
            var rows = settings.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Classes.Contains("overlay-theme-color-row"))
                .ToArray();

            Assert.NotNull(frame);
            Assert.NotNull(presetCard);
            Assert.NotNull(actionsCard);
            Assert.NotNull(colorEditorList);
            var presetOrigin = presetCard.TranslatePoint(default, settings);
            var actionsOrigin = actionsCard.TranslatePoint(default, settings);
            var colorEditorOrigin = colorEditorList.TranslatePoint(default, settings);
            Assert.NotNull(presetOrigin);
            Assert.NotNull(actionsOrigin);
            Assert.NotNull(colorEditorOrigin);
            Assert.True(presetOrigin.Value.Y < actionsOrigin.Value.Y);
            Assert.True(actionsOrigin.Value.Y < colorEditorOrigin.Value.Y);
            Assert.Equal(
                overlayTheme.Categories.Sum(category => category.Colors.Count),
                rows.Length);
            Assert.All(rows, row =>
            {
                Assert.InRange(row.Bounds.Height, 1, 40);
                var colorPicker = row.GetVisualDescendants()
                    .OfType<ColorPicker>()
                    .Single();
                var colorPreview = colorPicker.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(presenter => presenter.Name == "PART_ContentPresenter");
                var opacity = row.GetVisualDescendants()
                    .OfType<Slider>()
                    .Single(slider => slider.Name == "OverlayThemeOpacitySlider");
                var thumb = opacity.GetVisualDescendants()
                    .OfType<Thumb>()
                    .Single(candidate => candidate.Name == "thumb");
                var hex = row.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(textBox => textBox.Name == "OverlayThemeHexTextBox");
                var colorOrigin = colorPicker.TranslatePoint(default, row);
                var opacityOrigin = opacity.TranslatePoint(default, row);
                var thumbOrigin = thumb.TranslatePoint(default, opacity);
                var hexOrigin = hex.TranslatePoint(default, row);
                Assert.NotNull(colorOrigin);
                Assert.NotNull(opacityOrigin);
                Assert.NotNull(thumbOrigin);
                Assert.NotNull(hexOrigin);
                Assert.True(colorPreview.Bounds.Width >= 32);
                Assert.True(colorOrigin.Value.X >= 0);
                Assert.True(
                    colorOrigin.Value.X + colorPicker.Bounds.Width
                    <= row.Bounds.Width);
                Assert.InRange(thumb.Bounds.Width, 1, 16);
                Assert.InRange(thumb.Bounds.Height, 1, 16);
                Assert.True(thumbOrigin.Value.Y >= 0);
                Assert.True(
                    thumbOrigin.Value.Y + thumb.Bounds.Height
                    <= opacity.Bounds.Height);
                Assert.True(opacityOrigin.Value.X < hexOrigin.Value.X);
            });
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
