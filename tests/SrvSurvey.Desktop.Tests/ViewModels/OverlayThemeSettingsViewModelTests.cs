using Avalonia;
using Avalonia.Media;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayThemeSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-editor-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PreviewRefreshesThemeWithoutSavingAndReloadRestoresActiveFile()
    {
        var themePath = Path.Combine(temporaryDirectory, "theme.json");
        var activeStore = new LegacyOverlayThemeStore(themePath);
        var activeTheme = LegacyOverlayThemeStore.CreateDefault();
        _ = activeStore.Save(activeTheme);
        var originalBytes = File.ReadAllBytes(themePath);
        var application = new Application();
        var service = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json")),
            activeTheme);
        service.ApplyCurrent();
        var viewModel = new OverlayThemeSettingsViewModel(
            activeStore,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json")),
            service,
            activeTheme);
        var primary = viewModel.Categories
            .SelectMany(category => category.Colors)
            .Single(color => color.Key == "orange");

        primary.HexValue = "#010203";
        viewModel.PreviewCommand.Execute(null);

        Assert.True(viewModel.IsDirty);
        Assert.Equal(originalBytes, File.ReadAllBytes(themePath));
        Assert.Equal(
            Color.Parse("#010203"),
            service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Contains("unsaved colours", viewModel.StatusMessage);

        viewModel.ReloadActiveCommand.Execute(null);

        Assert.False(viewModel.IsDirty);
        Assert.Equal(activeTheme.GetColor("orange"),
            service.CurrentOverlayTheme.GetColor("orange"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
