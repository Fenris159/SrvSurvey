using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Tests.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class FiregroupsPresentationTests
{
    [AvaloniaFact]
    public void SavingFiregroupImmediatelyDisplaysItsConfigurationAndDedicatedSettings()
    {
        using var main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var journal = new JournalSessionState();
        var events = new[] { FiregroupsWorkspaceViewModelTests.Event("""{"event":"LoadGame","FID":"FiregroupsPresentation","Commander":"Preview","Ship":"python","ShipID":1}"""), FiregroupsWorkspaceViewModelTests.Loadout(1, "Survey Python") };
        foreach (var entry in events) journal.Apply(entry);
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        main.Firegroups.Apply(new JournalMonitorUpdate(null, events, status, null, null, null, [], true), journal, status);
        var view = new Views.FiregroupsView { DataContext = main };
        var window = new Window { Content = view, Width = 1000, Height = 1100 };
        var themePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var theme = new RavenThemeService(Avalonia.Application.Current!, new ThemePreferenceStore(themePath));
        var originalTheme = theme.Current.Key;
        try
        {
            theme.Select("monochrome-dark");
            window.Show(); using var initial = window.CaptureRenderedFrame();
            var model = main.Firegroups;
            model.Primary[0].SelectedModule = model.Primary[0].Options[0];
            model.AddPrimaryCommand.Execute(null); model.Primary[1].SelectedModule = model.Primary[1].Options[1];
            model.Secondary[0].SelectedModule = model.Secondary[0].Options[3];
            model.AddGroupCommand.Execute(null);
            model.Primary[0].SelectedModule = model.Primary[0].Options[2];
            model.ConfigurationName = "Saved survey setup";
            var save = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Save"));
            save.Command!.Execute(save.CommandParameter);
            Assert.StartsWith("Saved", model.Status);
            Assert.Contains(model.SavedProfiles, p => p.Name == "Saved survey setup");
            var scroll = Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Parent is Grid);
            using var updated = window.CaptureRenderedFrame();
            scroll.ScrollToEnd(); using var savedFrame = window.CaptureRenderedFrame();
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Saved survey setup") && b.IsEffectivelyVisible);
            var expander = Assert.Single(view.GetVisualDescendants().OfType<Expander>());
            expander.IsExpanded = true; using var expanded = window.CaptureRenderedFrame();
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Group A" && t.IsEffectivelyVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Group B" && t.IsEffectivelyVisible);
            var remove = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Remove"));
            Assert.Same(save.Parent, remove.Parent);
            Assert.Equal(save.Bounds.Y, remove.Bounds.Y);
            var output = Environment.GetEnvironmentVariable("SRVSURVEY_FIREGROUPS_RENDER_OUTPUT");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, "saved-firegroups.png")); expanded!.Save(stream, PngBitmapEncoderOptions.Default);
                scroll.ScrollToHome(); using var editor = window.CaptureRenderedFrame();
                using var editorStream = File.Create(Path.Combine(output, "firegroups-editor.png")); editor!.Save(editorStream, PngBitmapEncoderOptions.Default);
            }
            var settingsButton = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Overlay settings"));
            settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = Assert.Single(window.OwnedWindows.OfType<OverlayCategorySettingsWindow>());
            using var settingsFrame = settings.CaptureRenderedFrame();
            Assert.Contains(settings.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Overlay Exceptions"));
            Assert.Single(main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.Firegroups));
            Assert.DoesNotContain(main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.Global), p => p.PlotterName == "PlotMiningFiregroups");
            settings.Close();
        }
        finally { window.Close(); theme.Select(originalTheme); File.Delete(themePath); }
    }
}
