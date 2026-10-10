using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuidesPresentationTests
{
    /// <summary>The real view opens a searched task and keeps search accessible at smaller desktop sizes.</summary>
    [AvaloniaTheory]
    [InlineData(900, "blue-dark")]
    [InlineData(1100, "blue-light")]
    public void SearchAndAccordionOpenTheSameReader(int width, string palette)
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(root, "theme.json"))
        );
        string previous = theme.Current.Key;
        theme.Select(palette);
        theme.ApplyCurrent();
        var view = new GuidesView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 760,
        };
        try
        {
            window.Show();
            Capture(window, palette, "browse");
            Assert.True(view.FindControl<TextBox>("GuideSearch")!.IsEffectivelyVisible);
            Assert.Single(view.GetVisualDescendants().OfType<Expander>(), expander => expander.IsExpanded);
            TextBox search = view.FindControl<TextBox>("GuideSearch")!;
            search.Text = "Planetary Mining";
            Capture(window, palette, "search");
            Assert.True(main.Guides.IsSearching);
            Assert.True(view.FindControl<StackPanel>("GuideSearchResults")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<StackPanel>("TopicReader")!.IsEffectivelyVisible);
            Button result = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button =>
                    button.DataContext is GuideSearchResultViewModel hit && hit.Title == "Plan mining for Powerplay"
                );
            Assert.True(result.IsEffectivelyVisible);
            result.Command!.Execute(null);
            Capture(window, palette, "powerplay");
            Assert.Equal("Plan mining for Powerplay", main.Guides.SelectedTopic.Title);
            Assert.Equal(string.Empty, search.Text);
            Assert.True(view.FindControl<ScrollViewer>("GuideNavigationScroller")!.Offset.Y > 0);
            Assert.True(view.FindControl<StackPanel>("TopicReader")!.IsEffectivelyVisible);
            GuideNavigationCategoryViewModel biology = main.Guides.Navigation.Single(category =>
                category.Category.Key == "exobiology"
            );
            biology.IsExpanded = true;
            Capture(window, palette, "expanded");
            Button task = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.DataContext == biology.Topics[0]);
            task.Command!.Execute(null);
            Capture(window, palette, "illustrated");
            Assert.Equal("Predictions and bio signals", main.Guides.SelectedTopic.Title);
            Assert.Equal(3, main.Guides.SelectedTopic.Illustrations.Count);
            Assert.Contains("selected", task.Classes);
            Assert.True(search.Bounds.Width > 400);
            Assert.True(view.FindControl<ScrollViewer>("GuideReader")!.Bounds.Width > 400);
        }
        finally
        {
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Opening another task resets reading position and old data contexts stop controlling the view.</summary>
    [AvaloniaFact]
    public void ReaderResetsForTopicsAndSearchAndUnsubscribesPreviousContext()
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        using MainWindowViewModel other = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var view = new GuidesView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 900,
            Height = 500,
        };
        try
        {
            window.Show();
            GuideTopicViewModel calibration = main
                .Guides.Navigation.SelectMany(category => category.Topics)
                .Single(topic => topic.Title == "Calibrate the six rig circles");
            calibration.OpenCommand.Execute(null);
            Capture(window, "reset", "long");
            ScrollViewer reader = view.FindControl<ScrollViewer>("GuideReader")!;
            reader.Offset = new Vector(0, 100);
            Assert.True(reader.Offset.Y > 0);
            main.Guides.SelectedCategory = main.Guides.Categories[0];
            Assert.Equal(default, reader.Offset);
            calibration.OpenCommand.Execute(null);
            Capture(window, "reset", "again");
            reader.Offset = new Vector(0, 100);
            main.Guides.SearchText = "rig";
            Assert.Equal(default, reader.Offset);
            view.DataContext = null;
            view.DataContext = other;
            other
                .Guides.Navigation.SelectMany(category => category.Topics)
                .Single(topic => topic.Title == calibration.Title)
                .OpenCommand.Execute(null);
            Capture(window, "reset", "context");
            reader.Offset = new Vector(0, 100);
            main.Guides.SelectedCategory = main.Guides.Categories[1];
            Assert.True(reader.Offset.Y > 0);
            other.Guides.SearchText = "rig";
            Assert.Equal(default, reader.Offset);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Optionally writes actual rendered views for visual quality checks.</summary>
    private static void Capture(Window window, string palette, string stage)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_GUIDES_RENDER_OUTPUT");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            using FileStream stream = File.Create(Path.Combine(output, $"guides-{palette}-{stage}.png"));
            frame.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }
}
