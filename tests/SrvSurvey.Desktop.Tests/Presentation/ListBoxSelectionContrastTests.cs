using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ListBoxSelectionContrastTests
{
    [AvaloniaFact]
    public void SelectedMutedTextOnlyUsesHighContrastInMonochromeTheme()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Avalonia application is missing.");
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-list-selection-tests-{Guid.NewGuid():N}");
        var service = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json")));
        var text = new TextBlock { Text = "510.0 m · 86.0°" };
        text.Classes.Add("muted");
        var semanticText = new TextBlock
        {
            Text = "Warning",
            Foreground = new SolidColorBrush(Color.Parse("#FF7B72")),
        };
        var row = new StackPanel
        {
            Children =
            {
                text,
                semanticText,
            },
        };
        var listBox = new ListBox
        {
            ItemsSource = new[] { row },
            SelectedIndex = 0,
        };
        var window = new Window
        {
            Width = 320,
            Height = 160,
            Content = listBox,
        };

        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());

            Assert.Equal(
                Color.Parse("#C8C8C8"),
                Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal(
                Color.Parse("#FF7B72"),
                Assert.IsAssignableFrom<ISolidColorBrush>(
                    semanticText.Foreground).Color);

            service.Select("monochrome-dark");
            Assert.NotNull(window.CaptureRenderedFrame());

            var selectedItem = listBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Single();
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(
                text.Foreground);

            Assert.True(selectedItem.IsSelected);
            Assert.Equal(Color.Parse("#0A0A0A"), foreground.Color);
            Assert.Equal(
                Color.Parse("#FF7B72"),
                Assert.IsAssignableFrom<ISolidColorBrush>(
                    semanticText.Foreground).Color);

            service.Select(RavenThemeCatalog.DefaultThemeKey);
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Equal(
                Color.Parse("#C8C8C8"),
                Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        }
        finally
        {
            window.Close();
            service.Select(RavenThemeCatalog.DefaultThemeKey);
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
