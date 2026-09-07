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
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Overlay settings"));
            Assert.Contains(main.NavigationItems, item => item.Key == "firegroups" && item.HasOverlaySettings);
            Assert.Single(main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.Firegroups));
            Assert.DoesNotContain(main.OverlayPanelVisibility.ForCategory(OverlaySettingsCategory.Global), p => p.PlotterName == "PlotMiningFiregroups");
        }
        finally { window.Close(); theme.Select(originalTheme); File.Delete(themePath); }
    }
    [AvaloniaFact]
    public void SavedRowTrashAsksYesNoWithoutSelectingOrDeletingOnCancel()
    {
        using var main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var journal = new JournalSessionState();
        var events = new[] { FiregroupsWorkspaceViewModelTests.Event("""{"event":"LoadGame","FID":"DeletePresentation","Commander":"Test","Ship":"python","ShipID":1}"""), FiregroupsWorkspaceViewModelTests.Loadout(1, "Survey Python") };
        foreach (var entry in events) journal.Apply(entry);
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        var model = main.Firegroups;
        model.Apply(new JournalMonitorUpdate(null, events, status, null, null, null, [], true), journal, status);
        model.Primary[0].SelectedModule = model.Primary[0].Options[0];
        model.ConfigurationName = "Delete this"; model.SaveCommand.Execute(null);
        model.NewCommand.Execute(null);
        model.Primary[0].SelectedModule = model.Primary[0].Options[1];
        model.ConfigurationName = "Keep this"; model.SaveCommand.Execute(null);
        model.ConfigurationName = "Unfinished edit";
        var view = new Views.FiregroupsView { DataContext = main };
        var window = new Window { Content = view, Width = 1000, Height = 1100 };
        try
        {
            window.Show(); using var frame = window.CaptureRenderedFrame();
            var trash = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Delete Delete this");
            trash.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows.OfType<DeleteFiregroupDialog>());
            using var promptFrame = dialog.CaptureRenderedFrame();
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Are you sure?");
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Delete this") == true);
            var renderOutput = Environment.GetEnvironmentVariable("SRVSURVEY_FIREGROUPS_RENDER_OUTPUT");
            if (renderOutput is not null)
            {
                Directory.CreateDirectory(renderOutput);
                using var stream = File.Create(Path.Combine(renderOutput, "firegroups-delete-confirmation.png"));
                promptFrame!.Save(stream, PngBitmapEncoderOptions.Default);
            }
            Assert.Equal(2, model.SavedProfiles.Count);
            Assert.Equal("Unfinished edit", model.ConfigurationName);
            Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "No")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, model.SavedProfiles.Count);
            trash.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = Assert.Single(window.OwnedWindows.OfType<DeleteFiregroupDialog>());
            using var secondPrompt = dialog.CaptureRenderedFrame();
            Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Yes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("Keep this", Assert.Single(model.SavedProfiles).Name);
            Assert.Equal("Unfinished edit", model.ConfigurationName);
            using var updated = window.CaptureRenderedFrame();
            var remove = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Remove"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = Assert.Single(window.OwnedWindows.OfType<DeleteFiregroupDialog>());
            dialog.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Single(model.SavedProfiles);
        }
        finally { window.Close(); }
    }

}
