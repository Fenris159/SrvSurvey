using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class StealthModePresentationTests
{
    [AvaloniaFact]
    public async Task AFailedSaveCannotLeaveTheCheckboxShowingPrivacyIsEnabled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "stealth-save-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths)
        );
        var view = new SettingsView { DataContext = model };
        var window = new Window { Content = view };
        string parent = Path.GetDirectoryName(paths.UiSettingsPath)!;
        string backup = Path.Combine(directory, "saved-config");
        Directory.CreateDirectory(parent);
        Directory.Move(parent, backup);
        await File.WriteAllTextAsync(parent, "occupied");
        try
        {
            model.SettingsWorkspace.SelectCategory("privacy");
            window.Show();
            window.UpdateLayout();
            CheckBox checkbox = view.FindControl<CheckBox>("StealthModeCheckBox")!;
            checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            checkbox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(model.NetworkPrivacy.StealthModeEnabled);
            Assert.False(checkbox.IsChecked);
            Assert.Contains("could not be saved", model.NetworkPrivacy.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            File.Delete(parent);
            Directory.Move(backup, parent);
            await model.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task PrivacyStartsWithStealthAndTheCheckboxPersistsWithoutEnablingProviders()
    {
        string directory = Path.Combine(Path.GetTempPath(), "stealth-ui-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths)
        );
        var view = new SettingsView { DataContext = model };
        var window = new Window
        {
            Width = 1100,
            Height = 900,
            Content = view,
        };
        try
        {
            model.SettingsWorkspace.SelectCategory("privacy");
            window.Show();
            window.UpdateLayout();
            Border stealth = view.FindControl<Border>("StealthModeCard")!;
            Border nicknames = view.FindControl<Border>("SystemNicknamesCard")!;
            Assert.True(stealth.IsVisible);
            Assert.True(stealth.Bounds.Y < nicknames.Bounds.Y);
            CheckBox checkbox = view.FindControl<CheckBox>("StealthModeCheckBox")!;
            checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            checkbox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(new NetworkPrivacySettingsStore(paths.UiSettingsPath).Load().StealthModeEnabled);
            Assert.False(model.NetworkPrivacy.EddnUploadEnabled);
            Assert.False(model.NetworkPrivacy.EdAstroUploadEnabled);
            model.SettingsWorkspace.SearchQuery = "stealth";
            Assert.Single(Assert.Single(model.SettingsWorkspace.GroupedSearchResults).Results);
            model.SettingsWorkspace.SearchQuery = "";
            model.SettingsWorkspace.SelectCategory("application");
            Assert.False(stealth.IsVisible);
        }
        finally
        {
            window.Close();
            await model.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
