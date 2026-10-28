using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ApplicationWindowAppearanceTests
{
    /// <summary>Exercises the real shell, navigation and custom editor in every palette, including at minimum size.</summary>
    [AvaloniaTheory]
    [InlineData("blue-dark")]
    [InlineData("blue-light")]
    [InlineData("orange-dark")]
    [InlineData("green-light")]
    [InlineData("green-dark")]
    [InlineData("monochrome-dark")]
    public void PalettesPreserveNavigationAndCustomForms(string palette)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-appearance-{Guid.NewGuid():N}");
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(root, "ui.json"))
        );
        string previous = theme.Current.Key;
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var window = new MainWindow(main);
        var notes = new SystemNotesViewModel(new SystemNoteStore(root), new SystemNotesSettingsStore(root));
        var editor = new SystemNotesWindow(notes);
        try
        {
            theme.Select(palette);
            theme.ApplyCurrent();
            main.ThemeOptions.Single(option => option.Definition.Key == palette).SelectCommand.Execute(null);
            Assert.Equal(theme.Current.DisplayName, main.SelectedThemeName);
            window.Show();
            main.ToggleNavigationGroup("survey");
            Capture(window, palette, "overview");
            Assert.True(window.FindControl<Border>("MainSidebar")!.Bounds.Width > 200);
            Button settings = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button =>
                    button.Classes.Contains("nav-item")
                    && button.DataContext is NavigationItemViewModel { Key: "settings" }
                );
            settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(window, palette, "settings");
            Assert.True(main.IsSettingsSelected);
            ScrollViewer form = window
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single(control => control.Name == "SettingsFormScroller");
            Assert.True(form.Extent.Width <= form.Viewport.Width + 1);
            main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "theme");
            Capture(window, palette, "theme");
            Assert.True(main.IsThemeSelected);
            main.SelectedNavigation = main.NavigationItems.Single(item => item.Key == "settings");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Capture(window, palette, "minimum");
            Assert.True(window.FindControl<Grid>("MainWorkspace")!.Bounds.Width >= 600);
            Assert.True(form.Extent.Width >= 540);
            Assert.True(form.Extent.Width > form.Viewport.Width);

            notes.UpdateContext("F1234", "Surveyor", "Colonia", 1234, null);
            editor.Show();
            TextBox input = editor.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            input.Text = "Survey notes remain editable.";
            Capture(editor, palette, "notes");
            Assert.True(input.IsFocused);
            Assert.Equal(input.Text, notes.Notes);
            Assert.True(notes.IsDirty);
            Assert.Equal(
                Color.Parse(theme.Current.WindowColor),
                Assert.IsAssignableFrom<ISolidColorBrush>(input.Background).Color
            );
        }
        finally
        {
            editor.Close();
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Opting an application window into the reskin cannot restyle an independent overlay window.</summary>
    [AvaloniaFact]
    public void ApplicationStyleBoundaryLeavesOverlayChromeUntouched()
    {
        var applicationCard = new Border { Classes = { "card" } };
        var overlayCard = new Border { Classes = { "card" } };
        var application = new Window { Classes = { "app-window" }, Content = applicationCard };
        var overlay = new Window { Classes = { "srv-overlay" }, Content = overlayCard };
        try
        {
            application.Show();
            overlay.Show();
            Assert.Equal(new CornerRadius(5), applicationCard.CornerRadius);
            Assert.Equal(new CornerRadius(12), overlayCard.CornerRadius);
        }
        finally
        {
            overlay.Close();
            application.Close();
        }
    }

    /// <summary>Saves actual Avalonia renders when visual evidence is requested.</summary>
    private static void Capture(Window window, string palette, string stage)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_APPEARANCE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, $"{palette}-{stage}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
