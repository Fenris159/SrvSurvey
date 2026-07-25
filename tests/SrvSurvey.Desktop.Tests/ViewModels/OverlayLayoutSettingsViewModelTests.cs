using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayLayoutSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SavePersistsChangesAndUpdatesSharedRuntimeLayout()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotJumpInfo\":\"center:0,top:8 "
            + "{ s: 20, p: <1, 2, 3>, r: <4, 5, 6>}\"}");
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "settings.json"),
            "{\"plotterOpacity\":65}");
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var activeLayout = store.Load();
        var viewModel = new OverlayLayoutSettingsViewModel(
            store,
            activeLayout);
        var editor = viewModel.Overlays.Single(
            overlay => overlay.Name == "PlotJumpInfo");
        viewModel.SelectedOverlay = editor;

        editor.HorizontalAnchor = LegacyHorizontalAnchor.Screen;
        editor.HorizontalOffset = -240;
        editor.VerticalAnchor = LegacyVerticalAnchor.Bottom;
        editor.VerticalOffset = 72;
        editor.UseCustomOpacity = true;
        editor.CustomOpacityPercent = 35;

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        viewModel.SaveCommand.Execute(null);

        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Contains("Saved 1 overlay position", viewModel.StatusMessage);
        Assert.Contains("Previous layout backup", viewModel.StatusMessage);
        Assert.Equal(0.35, activeLayout.GetOpacity("PlotJumpInfo"));
        Assert.Equal(
            new Avalonia.PixelPoint(-240, 688),
            activeLayout.GetPosition(
                "PlotJumpInfo",
                new Avalonia.PixelRect(100, 200, 1000, 600),
                new Avalonia.PixelSize(300, 40)));
        Assert.Contains(
            "{ s: 20, p: <1, 2, 3>, r: <4, 5, 6>}",
            File.ReadAllText(Path.Combine(temporaryDirectory, "plotters.json")));
    }

    [Fact]
    public void ResetSelectedRestoresLegacyDesktopDefault()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "plotters.json"),
            "{\"PlotTrackTarget\":\"right:99,bottom:77,0.4\"}");
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var viewModel = new OverlayLayoutSettingsViewModel(store, store.Load());
        viewModel.SelectedOverlay = viewModel.Overlays.Single(
            overlay => overlay.Name == "PlotTrackTarget");

        viewModel.ResetSelectedCommand.Execute(null);

        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Center,
                480,
                LegacyVerticalAnchor.Top,
                8,
                null),
            viewModel.SelectedOverlay.Placement);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void NotificationOverlayUsesTheLegacyBottomCenterDefault()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var viewModel = new OverlayLayoutSettingsViewModel(store, store.Load());

        var notification = viewModel.Overlays.Single(
            overlay => overlay.Name == "PlotFloatie");

        Assert.Equal(
            new LegacyOverlayPlacement(
                LegacyHorizontalAnchor.Center,
                0,
                LegacyVerticalAnchor.Bottom,
                24,
                null),
            notification.Placement);
        Assert.Equal(22, viewModel.Overlays.Count);
    }

    [Fact]
    public void MalformedLegacyLayoutCannotBeOverwrittenFromEditor()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "plotters.json");
        const string original = "{\"PlotJumpInfo\":\"sideways:0,top:8\"}";
        File.WriteAllText(path, original);
        var store = new LegacyOverlayLayoutStore(temporaryDirectory);
        var viewModel = new OverlayLayoutSettingsViewModel(store, store.Load());

        viewModel.SelectedOverlay!.HorizontalOffset = 42;

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Contains("unknown horizontal anchor", viewModel.StatusMessage);
        Assert.Equal(original, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
