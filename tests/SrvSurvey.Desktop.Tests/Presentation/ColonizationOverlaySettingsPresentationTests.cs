using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationOverlaySettingsPresentationTests
{
    /// <summary>Checks the subordinate checkbox's placement, enabled state, live binding, and persistence.</summary>
    [AvaloniaFact]
    public void HideCollapsedGroupsIsIndentedAndFollowsCollapseSetting()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-colonization-settings-{Guid.NewGuid():N}");
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder =>
                builder.WithAppDataPaths(
                    new(
                        Path.Combine(directory, "config"),
                        Path.Combine(directory, "data"),
                        Path.Combine(directory, "cache"),
                        []
                    )
                )
        );
        var view = new Views.OverlaySettingsView(OverlaySettingsCategory.Colonization) { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 650,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            CheckBox collapse = view.FindControl<CheckBox>("CollapseCoveredCommodityGroupsCheckBox")!;
            CheckBox hide = view.FindControl<CheckBox>("HideCollapsedCommodityGroupsCheckBox")!;
            CheckBox autoHide = view.FindControl<CheckBox>("ColonizationAutoHideUntilDockedCheckBox")!;
            CheckBox hideBadges = view.FindControl<CheckBox>("HideCommodityRowBadgesCheckBox")!;
            Assert.Equal("Hide row badges", hideBadges.Content);
            Assert.Equal(false, hideBadges.IsChecked);
            Assert.Equal("Auto hide colonization overlay until docked", autoHide.Content);
            Assert.Equal(true, autoHide.IsChecked);
            autoHide.IsChecked = false;
            Assert.False(main.Colonization.AutoShowCommodityOverlay);
            autoHide.IsChecked = true;
            Assert.True(hide.IsEffectivelyVisible);
            Assert.True(hide.IsEnabled);
            Assert.Equal(false, hide.IsChecked);
            Assert.True(hide.Bounds.Y > collapse.Bounds.Y);
            Assert.True(hide.Bounds.X > collapse.Bounds.X);
            Assert.Equal("Hide collapsed groups", hide.Content);

            var notifications = new List<string?>();
            main.Colonization.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            hide.IsChecked = true;
            Assert.True(main.Colonization.HideCollapsedCommodityGroups);
            Assert.Contains(nameof(ColonizationViewModel.HideCollapsedCommodityGroups), notifications);
            Assert.Contains("hidden", main.Colonization.CommodityOverlay.CollapseModeText);
            var store = new ColonizationSettingsStore(main.AppDataPaths.UiSettingsPath);
            Assert.True(store.LoadOverlayPreferences().HideCollapsedGroups);
            hideBadges.IsChecked = true;
            Assert.True(main.Colonization.HideCommodityRowBadges);
            Assert.Contains(nameof(ColonizationViewModel.HideCommodityRowBadges), notifications);
            Assert.True(main.Colonization.CommodityOverlay.Preferences.HideRowBadges);
            Assert.True(store.LoadOverlayPreferences().HideRowBadges);
            hideBadges.IsChecked = false;
            Assert.False(main.Colonization.HideCommodityRowBadges);
            Assert.False(store.LoadOverlayPreferences().HideRowBadges);

            collapse.IsChecked = false;
            Assert.False(hide.IsEnabled);
            Assert.Equal(true, hide.IsChecked);
            Assert.Contains("expanded", main.Colonization.CommodityOverlay.CollapseModeText);
            collapse.IsChecked = true;
            Assert.True(hide.IsEnabled);
            Assert.Contains("hidden", main.Colonization.CommodityOverlay.CollapseModeText);
            hide.IsChecked = false;
            Assert.False(store.LoadOverlayPreferences().HideCollapsedGroups);
            Assert.Contains("collapse automatically", main.Colonization.CommodityOverlay.CollapseModeText);

            CheckBox combine = view.FindControl<CheckBox>("CombineShipCargoCountsCheckBox")!;
            Assert.Equal("Combine Ship totals into FC counts", combine.Content);
            combine.IsChecked = true;
            Assert.True(main.Colonization.InlineFleetCarrierCargo);
            Assert.True(store.LoadOverlayPreferences().InlineFleetCarrierCargo);
            main.Colonization.ShowFleetCarrierCargo = false;
            Assert.False(combine.IsEnabled);
            Assert.Equal(true, combine.IsChecked);
        }
        finally
        {
            window.Close();
            main.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
