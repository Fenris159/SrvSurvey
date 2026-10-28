using Avalonia;
using Avalonia.Media;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayThemeSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-editor-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void FivePersonalBrushesFollowGuardianAndPersistWithoutChangingThemeColors()
    {
        OverlayThemeSettingsViewModel viewModel = CreateViewModel();
        Assert.Equal("Guardian", viewModel.Categories[^2].Name);
        OverlayThemeCategoryViewModel custom = viewModel.Categories[^1];
        Assert.Equal("Custom", custom.Name);
        Assert.True(custom.IsCustomBrushGroup);
        Assert.False(viewModel.Categories[0].IsCustomBrushGroup);
        Assert.Equal(
            Enumerable.Range(1, 5).Select(index => $"Brush {index}"),
            custom.Colors.Select(editor => editor.DisplayName)
        );
        Assert.Equal(5, custom.Colors.Select(editor => editor.Color).Distinct().Count());
        Assert.False(viewModel.IsDirty);
        foreach (OverlayThemeColorEditorViewModel editor in custom.Colors)
        {
            editor.HexValue = "#12345680";
        }

        viewModel.ApplyCommand.Execute(null);

        var store = new LegacyOverlayThemeStore(Path.Combine(temporaryDirectory, "theme.json"));
        var reloaded = new OverlayThemeSettingsViewModel(
            store,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json"))
        );
        Assert.False(reloaded.IsDirty);
        Assert.Equal("Default", reloaded.SelectedSavedState);
        Assert.All(reloaded.Categories[^1].Colors, editor => Assert.Equal("#12345680", editor.HexValue));
        Assert.All(
            LegacyOverlayThemeStore.CreateDefault().Colors,
            entry => Assert.Equal(entry.Value, store.Load().GetColor(entry.Key))
        );
    }

    [Fact]
    public void PresetsSavedStatesAndDefaultsLeavePersonalBrushesAndValidationUnchanged()
    {
        var stateStore = new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json"));
        OverlayThemeSettingsViewModel viewModel = CreateViewModel(stateStore);
        OverlayThemeColorEditorViewModel personal = GetEditor(viewModel, "custom.1");
        personal.HexValue = "#12345680";
        viewModel.StateName = "Saved palette";
        viewModel.SaveStateCommand.Execute(null);
        Assert.DoesNotContain(
            stateStore.Load().States.Single().Colors.Keys,
            key => key.StartsWith("custom.", StringComparison.Ordinal)
        );
        // Also ignore personal swatches in an older/imported saved palette.
        var imported = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        imported["custom.1"] = Colors.Red;
        _ = stateStore.SaveState("Saved palette", imported);
        foreach (OverlayThemePreset preset in OverlayThemePresetCatalog.Presets)
        {
            viewModel.SelectedSavedState = preset.Name;
            Assert.Same(personal, GetEditor(viewModel, "custom.1"));
            Assert.Equal("#12345680", personal.HexValue);
        }

        viewModel.SelectedSavedState = "Saved palette";
        viewModel.LoadStateCommand.Execute(null);
        Assert.Equal("#12345680", GetEditor(viewModel, "custom.1").HexValue);
        personal.HexValue = "invalid";
        viewModel.RestoreDefaultsCommand.Execute(null);
        Assert.Same(personal, GetEditor(viewModel, "custom.1"));
        Assert.True(viewModel.HasValidationErrors);
        Assert.False(viewModel.CanApply);
        viewModel.ReloadActiveCommand.Execute(null);
        Assert.False(viewModel.HasValidationErrors);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(OverlayCustomBrushes.Defaults["custom.1"], GetColor(viewModel, "custom.1"));
    }

    [Fact]
    public void BuiltInPresetsAreAlwaysAvailableAndLoadWhenSelected()
    {
        OverlayThemeSettingsViewModel viewModel = CreateViewModel();

        Assert.Equal(
            OverlayThemePresetCatalog.Presets.Select(preset => preset.Name),
            viewModel.SavedStates.Take(OverlayThemePresetCatalog.Presets.Count)
        );
        Assert.Equal(OverlayThemePresetCatalog.DefaultName, viewModel.SelectedSavedState);

        viewModel.SelectedSavedState = "Nebula Cyan";

        Assert.Equal(Color.Parse("#5EC8F2"), GetColor(viewModel, "orange"));
        Assert.Equal(Color.Parse("#B8E8FF"), GetColor(viewModel, "cyan"));
        Assert.Equal(Color.Parse("#D6EEF9"), GetColor(viewModel, "white"));
        Assert.Equal(Color.Parse("#FFE8A3"), GetColor(viewModel, "yellow"));
        Assert.Equal(Color.Parse("#FFE8A3"), GetColor(viewModel, "header"));
        Assert.Equal(Color.Parse("#5EC8F2"), GetColor(viewModel, "guardian.primary"));
        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanDeleteSelectedState);
        Assert.Empty(viewModel.StateName);
    }

    [Fact]
    public void GeneralHeaderEditorPrecedesPrimaryAccent()
    {
        OverlayThemeCategoryViewModel general = CreateViewModel()
            .Categories.Single(candidate => candidate.Name == "General");

        Assert.Equal("header", general.Colors[0].Key);
        Assert.Equal("Header", general.Colors[0].DisplayName);
        Assert.Equal("orange", general.Colors[1].Key);
    }

    [Fact]
    public void ColorCategoriesBehaveAsASingleOpenAccordion()
    {
        OverlayThemeSettingsViewModel viewModel = CreateViewModel();
        OverlayThemeCategoryViewModel general = viewModel.Categories.Single(category => category.Name == "General");
        OverlayThemeCategoryViewModel guardian = viewModel.Categories.Single(category => category.Name == "Guardian");

        Assert.True(general.IsExpanded);
        Assert.Single(viewModel.Categories, category => category.IsExpanded);

        guardian.IsExpanded = true;

        Assert.False(general.IsExpanded);
        Assert.True(guardian.IsExpanded);
        Assert.Single(viewModel.Categories, category => category.IsExpanded);
    }

    [Fact]
    public void TypographyUsesExistingRoleSizesAndHalfPointSteps()
    {
        OverlayThemeSettingsViewModel viewModel = CreateViewModel();

        Assert.Equal(
            [("header", 10d), ("title", 15d), ("value", 12d), ("body", 11d), ("detail", 10d), ("caption", 9d)],
            viewModel.Typography.Select(editor => (editor.Key, editor.FontSize))
        );

        OverlayTypographyEditorViewModel header = GetTypographyEditor(viewModel, "header");
        header.FontSize = 10.26;

        Assert.Equal(10.5, header.FontSize);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void ExobiologyEditorsNameEveryRewardPipStatePrecisely()
    {
        OverlayThemeCategoryViewModel category = CreateViewModel()
            .Categories.Single(candidate => candidate.Name == "Exobiology");

        Assert.Equal(
            [
                ("bio.confirmed", "Confirmed organism reward PIP"),
                ("bio.confirmedDim", "Analyzed organism reward PIP"),
                ("bio.potential", "Confirmed reward-range upper segment"),
                ("bio.confirmedDimPotential", "Analyzed reward-range upper segment"),
                ("bio.prediction", "Predicted organism reward PIP"),
                ("bio.predictionPotential", "Predicted reward-range upper segment"),
                ("bio.gold", "Commander/regional-first marker"),
                ("bio.goldDark", "Commander/regional-first marker (analyzed)"),
                ("bio.goldFill", "Commander/regional-first PIP fill"),
                ("bio.goldDarkFill", "Commander/regional-first PIP fill (analyzed)"),
                ("bio.goldPotential", "Commander/regional-first possible segment"),
                ("bio.goldDarkPotential", "Analyzed first-discovery possible segment"),
                ("bio.galacticRegion", "Galactic-region candidate PIP"),
                ("bio.galacticRegionPotential", "Galactic-region possible segment"),
                ("bio.unknown", "Unknown reward frame"),
                ("bio.unknownGlyph", "Unknown reward question mark"),
                ("bio.hatch", "Prediction hatch lines"),
                ("bio.empty", "Empty reward segment"),
                ("bio.white", "Biology labels and values"),
                ("bio.confirmedEdge", "Confirmed PIP outer border"),
                ("bio.confirmedDimEdge", "Analyzed PIP outer border"),
                ("bio.predictionEdge", "Predicted PIP outer border"),
                ("bio.goldEdge", "Commander/regional-first PIP outer border"),
                ("bio.goldDarkEdge", "Analyzed first-discovery PIP outer border"),
                ("bio.galacticRegionEdge", "Galactic-region PIP outer border"),
                ("bio.unknownEdge", "Unknown reward PIP outer border"),
                ("bio.confirmedSegmentEdge", "Confirmed PIP filled-segment border"),
                ("bio.confirmedPotentialSegmentEdge", "Confirmed PIP possible-segment border"),
                ("bio.confirmedDimSegmentEdge", "Analyzed PIP filled-segment border"),
                ("bio.confirmedDimPotentialSegmentEdge", "Analyzed PIP possible-segment border"),
                ("bio.predictionSegmentEdge", "Predicted PIP filled-segment border"),
                ("bio.predictionPotentialSegmentEdge", "Predicted PIP possible-segment border"),
                ("bio.goldSegmentEdge", "Commander/regional-first filled-segment border"),
                ("bio.goldPotentialSegmentEdge", "Commander/regional-first possible-segment border"),
                ("bio.goldDarkSegmentEdge", "Analyzed first-discovery filled-segment border"),
                ("bio.goldDarkPotentialSegmentEdge", "Analyzed first-discovery possible-segment border"),
                ("bio.galacticRegionSegmentEdge", "Galactic-region filled-segment border"),
                ("bio.galacticRegionPotentialSegmentEdge", "Galactic-region possible-segment border"),
            ],
            category.Colors.Select(color => (color.Key, color.DisplayName))
        );
    }

    [Fact]
    public void LoadDefaultsRestoresEveryColorAndReselectsDefault()
    {
        OverlayThemeSettingsViewModel viewModel = CreateViewModel();
        viewModel.SelectedSavedState = "Crimson Wake";
        GetEditor(viewModel, "orange").HexValue = "#010203";
        GetTypographyEditor(viewModel, "header").FontSize = 20;

        viewModel.RestoreDefaultsCommand.Execute(null);

        IReadOnlyDictionary<string, Color> defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        Assert.Equal(OverlayThemePresetCatalog.DefaultName, viewModel.SelectedSavedState);
        Assert.All(defaults, entry => Assert.Equal(entry.Value, GetColor(viewModel, entry.Key)));
        Assert.Equal(OverlayTypographySettings.Default.Header, GetTypographyEditor(viewModel, "header").FontSize);
        Assert.Contains("'Default'", viewModel.StatusMessage);
    }

    [Fact]
    public void UserSavedStatesFollowBuiltInsAndRemainLoadableAndDeletable()
    {
        var stateStore = new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json"));
        _ = stateStore.SaveState("My custom theme", LegacyOverlayThemeStore.CreateDefault().Colors);
        OverlayThemeSettingsViewModel viewModel = CreateViewModel(stateStore);

        Assert.Equal("My custom theme", viewModel.SavedStates[^1]);

        viewModel.SelectedSavedState = "My custom theme";

        Assert.True(viewModel.CanDeleteSelectedState);
        viewModel.LoadStateCommand.Execute(null);
        Assert.Equal("My custom theme", viewModel.StateName);
    }

    [Fact]
    public void BuiltInPresetNamesCannotBeOverwrittenByNamedStates()
    {
        OverlayThemeSettingsViewModel viewModel = CreateViewModel();
        viewModel.StateName = " default ";

        Assert.False(viewModel.CanSaveState);

        viewModel.SaveStateCommand.Execute(null);

        Assert.Contains("built-in", viewModel.StatusMessage);
    }

    [Fact]
    public void LoadingBuiltInPresetAutomaticallyRefreshesPreviewWithoutSaving()
    {
        string themePath = Path.Combine(temporaryDirectory, "theme.json");
        var activeStore = new LegacyOverlayThemeStore(themePath);
        LegacyOverlayTheme activeTheme = LegacyOverlayThemeStore.CreateDefault();
        _ = activeStore.Save(activeTheme);
        byte[] originalBytes = File.ReadAllBytes(themePath);
        RavenThemeService service = CreateThemeService(activeTheme);
        var viewModel = new OverlayThemeSettingsViewModel(
            activeStore,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json")),
            service,
            activeTheme
        );

        viewModel.SelectedSavedState = "Nebula Cyan";

        Assert.Equal(activeTheme.GetColor("orange"), service.CurrentOverlayTheme.GetColor("orange"));

        viewModel.LoadStateCommand.Execute(null);

        Assert.Equal(originalBytes, File.ReadAllBytes(themePath));
        Assert.Equal(Color.Parse("#5EC8F2"), service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Contains("Refreshed all open overlays", viewModel.StatusMessage);
    }

    [Fact]
    public void LoadingSavedStateAutomaticallyRefreshesPreviewWithoutSaving()
    {
        string themePath = Path.Combine(temporaryDirectory, "theme.json");
        var activeStore = new LegacyOverlayThemeStore(themePath);
        LegacyOverlayTheme activeTheme = LegacyOverlayThemeStore.CreateDefault();
        _ = activeStore.Save(activeTheme);
        byte[] originalBytes = File.ReadAllBytes(themePath);
        var customColors = activeTheme.Colors.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal
        );
        customColors["orange"] = Color.Parse("#010203");
        OverlayTypographySettings customTypography = OverlayTypographySettings.Default with { Value = 13.5 };
        var stateStore = new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json"));
        _ = stateStore.SaveState("My custom theme", customColors, customTypography);
        RavenThemeService service = CreateThemeService(activeTheme);
        var viewModel = new OverlayThemeSettingsViewModel(activeStore, stateStore, service, activeTheme);
        viewModel.SelectedSavedState = "My custom theme";

        viewModel.LoadStateCommand.Execute(null);

        Assert.Equal(originalBytes, File.ReadAllBytes(themePath));
        Assert.Equal(Color.Parse("#010203"), service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Equal(customTypography, service.CurrentOverlayTheme.EffectiveTypography);
        Assert.Contains("Refreshed all open overlays", viewModel.StatusMessage);
    }

    [Fact]
    public void PreviewRefreshesThemeWithoutSavingAndReloadRestoresActiveFile()
    {
        string themePath = Path.Combine(temporaryDirectory, "theme.json");
        var activeStore = new LegacyOverlayThemeStore(themePath);
        LegacyOverlayTheme activeTheme = LegacyOverlayThemeStore.CreateDefault();
        _ = activeStore.Save(activeTheme);
        byte[] originalBytes = File.ReadAllBytes(themePath);
        var application = new Application();
        var service = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json")),
            activeTheme
        );
        service.ApplyCurrent();
        var viewModel = new OverlayThemeSettingsViewModel(
            activeStore,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json")),
            service,
            activeTheme
        );
        OverlayThemeColorEditorViewModel primary = viewModel
            .Categories.SelectMany(category => category.Colors)
            .Single(color => color.Key == "orange");
        OverlayTypographyEditorViewModel header = GetTypographyEditor(viewModel, "header");

        primary.HexValue = "#010203";
        header.FontSize = 11.5;
        viewModel.PreviewCommand.Execute(null);

        Assert.True(viewModel.IsDirty);
        Assert.Equal(originalBytes, File.ReadAllBytes(themePath));
        Assert.Equal(Color.Parse("#010203"), service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Equal(11.5, service.CurrentOverlayTheme.EffectiveTypography.Header);
        Assert.Equal(11.5, application.Resources["RavenOverlayHeaderFontSize"]);
        Assert.Contains("unsaved colours", viewModel.StatusMessage);

        viewModel.ReloadActiveCommand.Execute(null);

        Assert.False(viewModel.IsDirty);
        Assert.Equal(activeTheme.GetColor("orange"), service.CurrentOverlayTheme.GetColor("orange"));
        Assert.Equal(activeTheme.EffectiveTypography, service.CurrentOverlayTheme.EffectiveTypography);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private OverlayThemeSettingsViewModel CreateViewModel(OverlayThemeStateStore? stateStore = null)
    {
        return new OverlayThemeSettingsViewModel(
            new LegacyOverlayThemeStore(Path.Combine(temporaryDirectory, "theme.json")),
            stateStore ?? new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json")),
            initialTheme: LegacyOverlayThemeStore.CreateDefault()
        );
    }

    private RavenThemeService CreateThemeService(LegacyOverlayTheme activeTheme)
    {
        var service = new RavenThemeService(
            new Application(),
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "ui.json")),
            activeTheme
        );
        service.ApplyCurrent();
        return service;
    }

    private static OverlayThemeColorEditorViewModel GetEditor(OverlayThemeSettingsViewModel viewModel, string key)
    {
        return viewModel.Categories.SelectMany(category => category.Colors).Single(editor => editor.Key == key);
    }

    private static Color GetColor(OverlayThemeSettingsViewModel viewModel, string key)
    {
        return GetEditor(viewModel, key).Color;
    }

    private static OverlayTypographyEditorViewModel GetTypographyEditor(
        OverlayThemeSettingsViewModel viewModel,
        string key
    ) => viewModel.Typography.Single(editor => editor.Key == key);
}
