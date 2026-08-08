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
    public void BuiltInPresetsAreAlwaysAvailableAndLoadWhenSelected()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(
            OverlayThemePresetCatalog.Presets.Select(preset => preset.Name),
            viewModel.SavedStates.Take(OverlayThemePresetCatalog.Presets.Count));
        Assert.Equal(OverlayThemePresetCatalog.DefaultName, viewModel.SelectedSavedState);

        viewModel.SelectedSavedState = "Nebula Cyan";

        Assert.Equal(Color.Parse("#5EC8F2"), GetColor(viewModel, "orange"));
        Assert.Equal(Color.Parse("#B8E8FF"), GetColor(viewModel, "cyan"));
        Assert.Equal(Color.Parse("#D6EEF9"), GetColor(viewModel, "white"));
        Assert.Equal(Color.Parse("#FFE8A3"), GetColor(viewModel, "yellow"));
        Assert.Equal(Color.Parse("#5EC8F2"), GetColor(viewModel, "guardian.primary"));
        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanDeleteSelectedState);
        Assert.Empty(viewModel.StateName);
    }

    [Fact]
    public void ExobiologyEditorsNameEveryRewardPipStatePrecisely()
    {
        var category = CreateViewModel().Categories.Single(
            candidate => candidate.Name == "Exobiology");

        Assert.Equal(
            [
                ("bio.confirmed", "Confirmed reward PIP"),
                ("bio.confirmedDim", "Analyzed reward PIP"),
                ("bio.potential", "Possible reward segment"),
                ("bio.prediction", "Predicted reward PIP"),
                ("bio.predictionPotential", "Predicted possible segment"),
                ("bio.gold", "First-discovery candidate PIP"),
                ("bio.goldDark", "First-discovery candidate (analyzed)"),
                ("bio.unknown", "Unknown reward frame"),
                ("bio.unknownGlyph", "Unknown reward question mark"),
                ("bio.hatch", "Prediction hatch lines"),
                ("bio.empty", "Empty reward segment"),
                ("bio.white", "Biology labels and values"),
            ],
            category.Colors.Select(color =>
                (color.Key, color.DisplayName)));
    }

    [Fact]
    public void LoadDefaultsRestoresEveryColorAndReselectsDefault()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedSavedState = "Crimson Wake";
        GetEditor(viewModel, "orange").HexValue = "#010203";

        viewModel.RestoreDefaultsCommand.Execute(null);

        var defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        Assert.Equal(OverlayThemePresetCatalog.DefaultName, viewModel.SelectedSavedState);
        Assert.All(defaults, entry =>
            Assert.Equal(entry.Value, GetColor(viewModel, entry.Key)));
        Assert.Contains("'Default'", viewModel.StatusMessage);
    }

    [Fact]
    public void UserSavedStatesFollowBuiltInsAndRemainLoadableAndDeletable()
    {
        var stateStore = new OverlayThemeStateStore(
            Path.Combine(temporaryDirectory, "states.json"));
        _ = stateStore.SaveState(
            "My custom theme",
            LegacyOverlayThemeStore.CreateDefault().Colors);
        var viewModel = CreateViewModel(stateStore);

        Assert.Equal("My custom theme", viewModel.SavedStates[^1]);

        viewModel.SelectedSavedState = "My custom theme";

        Assert.True(viewModel.CanDeleteSelectedState);
        viewModel.LoadStateCommand.Execute(null);
        Assert.Equal("My custom theme", viewModel.StateName);
    }

    [Fact]
    public void BuiltInPresetNamesCannotBeOverwrittenByNamedStates()
    {
        var viewModel = CreateViewModel();
        viewModel.StateName = " default ";

        Assert.False(viewModel.CanSaveState);

        viewModel.SaveStateCommand.Execute(null);

        Assert.Contains("built-in", viewModel.StatusMessage);
    }

    [Fact]
    public void LoadingBuiltInPresetAutomaticallyRefreshesPreviewWithoutSaving()
    {
        var themePath = Path.Combine(temporaryDirectory, "theme.json");
        var activeStore = new LegacyOverlayThemeStore(themePath);
        var activeTheme = LegacyOverlayThemeStore.CreateDefault();
        _ = activeStore.Save(activeTheme);
        var originalBytes = File.ReadAllBytes(themePath);
        var service = CreateThemeService(activeTheme);
        var viewModel = new OverlayThemeSettingsViewModel(
            activeStore,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json")),
            service,
            activeTheme);

        viewModel.SelectedSavedState = "Nebula Cyan";

        Assert.Equal(
            activeTheme.GetColor("orange"),
            service.CurrentOverlayTheme.GetColor("orange"));

        viewModel.LoadStateCommand.Execute(null);

        Assert.Equal(originalBytes, File.ReadAllBytes(themePath));
        Assert.Equal(
            Color.Parse("#5EC8F2"),
            service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Contains("Refreshed all open overlays", viewModel.StatusMessage);
    }

    [Fact]
    public void LoadingSavedStateAutomaticallyRefreshesPreviewWithoutSaving()
    {
        var themePath = Path.Combine(temporaryDirectory, "theme.json");
        var activeStore = new LegacyOverlayThemeStore(themePath);
        var activeTheme = LegacyOverlayThemeStore.CreateDefault();
        _ = activeStore.Save(activeTheme);
        var originalBytes = File.ReadAllBytes(themePath);
        var customColors = activeTheme.Colors.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        customColors["orange"] = Color.Parse("#010203");
        var stateStore = new OverlayThemeStateStore(
            Path.Combine(temporaryDirectory, "states.json"));
        _ = stateStore.SaveState("My custom theme", customColors);
        var service = CreateThemeService(activeTheme);
        var viewModel = new OverlayThemeSettingsViewModel(
            activeStore,
            stateStore,
            service,
            activeTheme);
        viewModel.SelectedSavedState = "My custom theme";

        viewModel.LoadStateCommand.Execute(null);

        Assert.Equal(originalBytes, File.ReadAllBytes(themePath));
        Assert.Equal(
            Color.Parse("#010203"),
            service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Contains("Refreshed all open overlays", viewModel.StatusMessage);
    }

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

    private OverlayThemeSettingsViewModel CreateViewModel(
        OverlayThemeStateStore? stateStore = null)
    {
        return new OverlayThemeSettingsViewModel(
            new LegacyOverlayThemeStore(
                Path.Combine(temporaryDirectory, "theme.json")),
            stateStore ?? new OverlayThemeStateStore(
                Path.Combine(temporaryDirectory, "states.json")),
            initialTheme: LegacyOverlayThemeStore.CreateDefault());
    }

    private RavenThemeService CreateThemeService(LegacyOverlayTheme activeTheme)
    {
        var service = new RavenThemeService(
            new Application(),
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json")),
            activeTheme);
        service.ApplyCurrent();
        return service;
    }

    private static OverlayThemeColorEditorViewModel GetEditor(
        OverlayThemeSettingsViewModel viewModel,
        string key)
    {
        return viewModel.Categories
            .SelectMany(category => category.Colors)
            .Single(editor => editor.Key == key);
    }

    private static Color GetColor(
        OverlayThemeSettingsViewModel viewModel,
        string key)
    {
        return GetEditor(viewModel, key).Color;
    }
}
