using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Theming;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationColumnThemeTests : IDisposable
{
    private static readonly string[] LoadingCommodities = ["steel", "titanium"];
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-column-theme-{Guid.NewGuid():N}");

    /// <summary>Checks readable column accents and consistent green/red status colors across all seven presets.</summary>
    [Theory]
    [InlineData("Default", "#54DFED", "#FFC100")]
    [InlineData("Monochrome Companion", "#8DB2BE", "#D4BF8F")]
    [InlineData("Nebula Cyan", "#B8E8FF", "#FFE8A3")]
    [InlineData("Toxic Green", "#BDE9FF", "#FFF066")]
    [InlineData("Crimson Wake", "#C7DCFF", "#FFD966")]
    [InlineData("Void Amethyst", "#E2CCFF", "#FFE8A3")]
    [InlineData("Cerulean Gold", "#C8E4FA", "#FFCC33")]
    public void PresetsHaveIndependentColumnsAndConsistentStatus(string name, string have, string ship)
    {
        Assert.True(OverlayThemePresetCatalog.TryGet(name, out OverlayThemePreset preset));
        Assert.Equal(Colors.White, preset.Colors["colonize.need"]);
        Assert.Equal(Color.Parse(have), preset.Colors["colonize.have"]);
        Assert.Equal(Color.Parse(ship), preset.Colors["colonize.ship"]);
        foreach (
            string key in new[]
            {
                "colonise.surplus",
                "colonise.surplusDark",
                "colonise.deficit",
                "colonise.deficitDark",
            }
        )
        {
            Assert.Equal(OverlayThemePresetCatalog.Default.Colors[key], preset.Colors[key]);
            Assert.NotEqual(preset.Colors[key], preset.Colors["colonize.have"]);
            Assert.NotEqual(preset.Colors[key], preset.Colors["colonize.ship"]);
        }
    }

    /// <summary>Upgrades active and saved older palettes using their matching preset without replacing existing choices.</summary>
    [Theory]
    [InlineData("Default")]
    [InlineData("Monochrome Companion")]
    [InlineData("Nebula Cyan")]
    [InlineData("Toxic Green")]
    [InlineData("Crimson Wake")]
    [InlineData("Void Amethyst")]
    [InlineData("Cerulean Gold")]
    public void OlderActiveAndSavedPalettesGainMatchingColumns(string name)
    {
        Assert.True(OverlayThemePresetCatalog.TryGet(name, out OverlayThemePreset preset));
        var colors = preset
            .Colors.Where(entry => !entry.Key.StartsWith("colonize.", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => LegacyOverlayThemeStore.FormatHtmlColor(entry.Value));
        colors["colonise.item"] = "#ABCDEF";
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "theme.json");
        File.WriteAllText(path, JsonSerializer.Serialize(colors));
        LegacyOverlayTheme active = new LegacyOverlayThemeStore(path).Load();
        Assert.Null(active.Error);

        string statesPath = Path.Combine(directory, "states.json");
        File.WriteAllText(
            statesPath,
            JsonSerializer.Serialize(new { version = 1, states = new[] { new { name = "Old state", colors } } })
        );
        OverlayThemeStateLoadResult result = new OverlayThemeStateStore(statesPath).Load();
        Assert.Null(result.Error);
        OverlayThemeState saved = Assert.Single(result.States);
        foreach (string key in new[] { "colonize.category", "colonize.need", "colonize.have", "colonize.ship" })
        {
            Assert.Equal(preset.Colors[key], active.GetColor(key));
            Assert.Equal(preset.Colors[key], saved.Colors[key]);
        }
        Assert.Equal(Color.Parse("#ABCDEF"), active.GetColor("colonise.item"));
        Assert.Equal(Color.Parse("#ABCDEF"), saved.Colors["colonise.item"]);
    }

    /// <summary>Persists independent columns and uses highlight RGB with the commodity row fill's opacity slider.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(36)]
    [InlineData(64)]
    [InlineData(100)]
    public void ColumnPickersSaveIndependentColorsAndApplyResources(int opacityPercent)
    {
        var store = new LegacyOverlayThemeStore(Path.Combine(directory, "theme.json"));
        var viewModel = new OverlayThemeSettingsViewModel(
            store,
            new OverlayThemeStateStore(Path.Combine(directory, "states.json"))
        );
        IReadOnlyList<OverlayThemeColorEditorViewModel> editors = viewModel
            .Categories.Single(category => category.Name == "Colonization")
            .Colors;
        var values = new Dictionary<string, string>
        {
            ["need"] = "#123456",
            ["have"] = "#654321",
            ["ship"] = "#ABCDEF",
        };
        foreach (KeyValuePair<string, string> value in values)
        {
            editors.Single(editor => editor.Key == "colonize." + value.Key).HexValue = value.Value;
        }
        editors.Single(editor => editor.Key == "colonise.highlight").HexValue = "#123ABC80";
        editors.Single(editor => editor.Key == "colonise.rowHighlight").OpacityPercent = opacityPercent;
        viewModel.ApplyCommand.Execute(null);
        LegacyOverlayTheme loaded = store.Load();
        var application = new Application();
        var service = new RavenThemeService(application, new ThemePreferenceStore(Path.Combine(directory, "ui.json")));
        service.ApplyOverlayTheme(loaded);
        foreach (KeyValuePair<string, string> value in values)
        {
            Assert.Equal(Color.Parse(value.Value), loaded.GetColor("colonize." + value.Key));
            string resource = "RavenOverlayColonise" + char.ToUpperInvariant(value.Key[0]) + value.Key[1..] + "Brush";
            Assert.Equal(
                Color.Parse(value.Value),
                Assert.IsType<SolidColorBrush>(application.Resources[resource]).Color
            );
        }
        Assert.Equal(Colors.Lime, loaded.GetColor("colonise.surplus"));
        Assert.Equal(Color.FromArgb(128, 18, 58, 188), loaded.GetColor("colonise.highlight"));
        SolidColorBrush background = Assert.IsType<SolidColorBrush>(
            application.Resources["RavenOverlayColoniseLoadHighlightBrush"]
        );
        Assert.Equal(Color.FromArgb(loaded.GetColor("colonise.rowHighlight").A, 18, 58, 188), background.Color);
        Assert.Equal(1, background.Opacity);
    }

    /// <summary>Preserves explicit column colors in custom palettes while filling only missing brushes.</summary>
    [Fact]
    public void CustomPaletteRetainsExplicitColumnColors()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "theme.json");
        File.WriteAllText(path, "{\"orange\":\"#123456\",\"colonize\":{\"need\":\"#ABCDEF\",\"ship\":\"#654321\"}}");
        var store = new LegacyOverlayThemeStore(path);
        LegacyOverlayTheme theme = store.Load();
        _ = store.Save(theme);
        LegacyOverlayTheme loaded = store.Load();
        Assert.Null(loaded.Error);
        Assert.Equal(Color.Parse("#ABCDEF"), loaded.GetColor("colonize.need"));
        Assert.Equal(Color.Parse("#654321"), loaded.GetColor("colonize.ship"));
        Assert.Equal(OverlayThemePresetCatalog.Default.Colors["colonize.have"], loaded.GetColor("colonize.have"));
    }

    /// <summary>Renders dedicated sum brushes and sign-based delta brushes while preserving market dimming and live edits.</summary>
    [AvaloniaTheory]
    [InlineData(false, false, false, 80, 20, "ColoniseHave")]
    [InlineData(false, false, true, 80, 20, "ColoniseHave")]
    [InlineData(true, false, false, 80, 20, "ColoniseHave")]
    [InlineData(false, true, false, 80, 120, "ColoniseDeficit")]
    [InlineData(false, true, true, 80, 120, "ColoniseDeficitDim")]
    [InlineData(false, true, false, 100, 0, "ColoniseSurplus")]
    [InlineData(false, true, true, 100, 0, "ColoniseSurplusDim")]
    [InlineData(false, true, false, 0, 0, "ColoniseDeficit")]
    [InlineData(true, true, false, 80, 20, "ColoniseSurplus")]
    [InlineData(true, true, true, 80, 20, "ColoniseSurplusDim")]
    [InlineData(true, true, false, 80, 19, "ColoniseDeficit")]
    [InlineData(true, true, true, 80, 19, "ColoniseDeficitDim")]
    public void OverlayColumnsUseTheirOwnBrushes(
        bool combined,
        bool delta,
        bool dim,
        int carrier,
        int ship,
        string role
    )
    {
        var brushes = new Dictionary<string, SolidColorBrush>
        {
            ["ColoniseNeed"] = new(Colors.White),
            ["ColoniseHave"] = new(Colors.Cyan),
            ["ColoniseShip"] = new(Colors.Gold),
            ["ColoniseSurplus"] = new(Colors.Lime),
            ["ColoniseSurplusDim"] = new(Colors.DarkGreen),
            ["ColoniseDeficit"] = new(Colors.Red),
            ["ColoniseDeficitDim"] = new(Colors.DarkRed),
            ["Primary"] = new(Colors.Orange),
        };
        Application application = Application.Current!;
        var originals = brushes.Keys.ToDictionary(
            key => key,
            key => application.Resources["RavenOverlay" + key + "Brush"]
        );
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                InlineFleetCarrierCargo = combined,
                ShowFleetCarrierDelta = delta,
                CollapseCoveredGroups = false,
                HighlightAlmostCoveredFleetCarrierLoads = true,
            }
        );
        viewModel.Apply(
            new ColonizationCommodityPlan
            {
                Title = "Build",
                ProjectNames = [],
                FleetCarriers = [],
                Rows =
                [
                    new()
                    {
                        Commodity = "steel",
                        DisplayName = "Steel",
                        Category = "Metals",
                        Needed = 100,
                        OnFleetCarriers = carrier,
                        InShip = ship,
                        IsAvailableAtCurrentMarket = !dim,
                        IsUnavailableAtCurrentMarket = dim,
                        CanCompleteFleetCarrierLoad = !dim,
                    },
                ],
            },
            null
        );
        var window = new Window { Content = new ColonizationCommodityOverlayPresentation { DataContext = viewModel } };
        try
        {
            foreach (KeyValuePair<string, SolidColorBrush> brush in brushes)
            {
                application.Resources["RavenOverlay" + brush.Key + "Brush"] = brush.Value;
            }
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            Border row = Assert.Single(
                window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("commodity-row")
            );
            Grid grid = Assert.IsType<Grid>(row.Child);
            TextBlock[] cells = grid.Children.OfType<TextBlock>().Where(cell => cell.IsVisible).ToArray();
            TextBlock need = Assert.Single(cells, cell => Grid.GetColumn(cell) == 1);
            TextBlock cargo = Assert.Single(cells, cell => Grid.GetColumn(cell) == 2);
            TextBlock inShip = Assert.Single(cells, cell => Grid.GetColumn(cell) == 3);
            Assert.Same(brushes["ColoniseNeed"], need.Foreground);
            Assert.Same(brushes[role], cargo.Foreground);
            Assert.Same(brushes["ColoniseShip"], inShip.Foreground);
            Assert.NotEmpty(cargo.Text!);
            Assert.Equal(dim ? 0.48 : 1, row.Opacity);
            Grid headers = Assert.Single(
                window.GetVisualDescendants().OfType<Grid>(),
                header => header.Classes.Contains("commodity-headers")
            );
            Assert.All(
                headers.Children.OfType<TextBlock>(),
                header => Assert.Same(brushes["Primary"], header.Foreground)
            );
            var changed = new SolidColorBrush(Colors.Magenta);
            window.Resources["RavenColoniseNeedBrush"] = changed;
            Assert.Same(changed, need.Foreground);
            Assert.Same(brushes[role], cargo.Foreground);
        }
        finally
        {
            window.Close();
            foreach (KeyValuePair<string, object?> original in originals)
            {
                application.Resources["RavenOverlay" + original.Key + "Brush"] = original.Value;
            }
        }
    }

    /// <summary>Overrides striped and plain rows only while loading guidance is active and restores their normal fills.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LoadingHighlightReplacesEntireRowBackground(bool enabled, bool canCompleteLoad)
    {
        var stripe = new SolidColorBrush(Color.FromArgb(92, 102, 102, 102));
        var highlight = new SolidColorBrush(Color.FromArgb(stripe.Color.A, 255, 255, 0));
        var brushes = new Dictionary<string, SolidColorBrush>
        {
            ["ColoniseRowHighlight"] = stripe,
            ["ColoniseLoadHighlight"] = highlight,
            ["ColoniseHave"] = new(Colors.Cyan),
        };
        Application application = Application.Current!;
        var originals = brushes.Keys.ToDictionary(
            key => key,
            key => application.Resources["RavenOverlay" + key + "Brush"]
        );
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            HighlightAlmostCoveredFleetCarrierLoads = enabled,
            ShowFleetCarrierDelta = false,
            CollapseCoveredGroups = false,
        };
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.ApplyPreferences(preferences);
        viewModel.Apply(
            new ColonizationCommodityPlan
            {
                Title = "Build",
                ProjectNames = [],
                FleetCarriers = [],
                Rows = LoadingCommodities
                    .Select(commodity => new ColonizationCommodityPlanRow
                    {
                        Commodity = commodity,
                        DisplayName = commodity,
                        Category = "Metals",
                        Needed = 100,
                        OnFleetCarriers = 80,
                        InShip = 20,
                        IsAvailableAtCurrentMarket = true,
                        CanCompleteFleetCarrierLoad = canCompleteLoad,
                    })
                    .ToArray(),
            },
            null
        );
        var window = new Window { Content = new ColonizationCommodityOverlayPresentation { DataContext = viewModel } };
        try
        {
            foreach (KeyValuePair<string, SolidColorBrush> brush in brushes)
            {
                application.Resources["RavenOverlay" + brush.Key + "Brush"] = brush.Value;
            }
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            Border[] rows = window
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("commodity-row"))
                .ToArray();
            Assert.Equal(2, rows.Length);
            bool highlighted = enabled && canCompleteLoad;
            Assert.Same(highlighted ? highlight : stripe, rows[0].Background);
            if (highlighted)
            {
                Assert.Same(highlight, rows[1].Background);
                var changed = new SolidColorBrush(Color.FromArgb(163, 255, 215, 0));
                stripe = new SolidColorBrush(Color.FromArgb(changed.Color.A, 102, 102, 102));
                window.Resources["RavenColoniseRowHighlightBrush"] = stripe;
                window.Resources["RavenColoniseLoadHighlightBrush"] = changed;
                Assert.All(rows, row => Assert.Same(changed, row.Background));
            }
            else
            {
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(rows[1].Background).Color);
            }
            Assert.All(rows, row => Assert.Equal(1, row.Opacity));
            Assert.All(
                rows,
                row =>
                    Assert.Same(
                        brushes["ColoniseHave"],
                        Assert
                            .Single(
                                Assert.IsType<Grid>(row.Child).Children.OfType<TextBlock>(),
                                cell => Grid.GetColumn(cell) == 2 && cell.IsVisible
                            )
                            .Foreground
                    )
            );

            viewModel.ApplyPreferences(preferences with { HighlightAlmostCoveredFleetCarrierLoads = false });
            window.UpdateLayout();
            Border[] restored = window
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("commodity-row"))
                .ToArray();
            Assert.Equal(2, restored.Length);
            Assert.Same(stripe, restored[0].Background);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(restored[1].Background).Color);
        }
        finally
        {
            window.Close();
            foreach (KeyValuePair<string, object?> original in originals)
            {
                application.Resources["RavenOverlay" + original.Key + "Brush"] = original.Value;
            }
        }
    }

    /// <summary>Removes isolated palette and settings fixtures after each test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
