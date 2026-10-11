using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Theming;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCategoryThemeTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-category-{Guid.NewGuid():N}"
    );

    /// <summary>Checks distinct category accents and the default grey fill's RGB and opacity.</summary>
    [Theory]
    [InlineData("Default", "#FFC100")]
    [InlineData("Monochrome Companion", "#D4BF8F")]
    [InlineData("Nebula Cyan", "#FFE8A3")]
    [InlineData("Toxic Green", "#FFF066")]
    [InlineData("Crimson Wake", "#FFD966")]
    [InlineData("Void Amethyst", "#7FFFD4")]
    [InlineData("Cerulean Gold", "#FFCC33")]
    public void PresetsDistinguishCategoriesFromItems(string presetName, string categoryColor)
    {
        Assert.True(OverlayThemePresetCatalog.TryGet(presetName, out OverlayThemePreset preset));
        Assert.Equal(Color.Parse(categoryColor), preset.Colors["colonize.category"]);
        Assert.NotEqual(preset.Colors["colonise.item"], preset.Colors["colonize.category"]);
        Assert.Equal(
            Color.FromArgb(92, 102, 102, 102),
            OverlayThemePresetCatalog.Default.Colors["colonise.rowHighlight"]
        );
    }

    /// <summary>Upgrades older presets without overwriting their item colors or row opacity.</summary>
    [Theory]
    [InlineData("Default")]
    [InlineData("Monochrome Companion")]
    [InlineData("Nebula Cyan")]
    [InlineData("Toxic Green")]
    [InlineData("Crimson Wake")]
    [InlineData("Void Amethyst")]
    [InlineData("Cerulean Gold")]
    public void OlderPalettesReceiveMatchingCategories(string presetName)
    {
        Assert.True(OverlayThemePresetCatalog.TryGet(presetName, out OverlayThemePreset preset));
        var colors = preset
            .Colors.Where(entry => entry.Key != "colonize.category")
            .ToDictionary(
                entry => entry.Key,
                entry => new int[] { entry.Value.A, entry.Value.R, entry.Value.G, entry.Value.B }
            );
        colors["colonise.item"] = [255, 12, 34, 56];
        colors["colonise.rowHighlight"] = [72, 56, 56, 56];
        string path = Path.Combine(temporaryDirectory, "theme.json");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(colors));

        LegacyOverlayTheme loaded = new LegacyOverlayThemeStore(path).Load();

        Assert.Null(loaded.Error);
        Assert.Equal(preset.Colors["colonize.category"], loaded.GetColor("colonize.category"));
        Assert.Equal(Color.FromArgb(255, 12, 34, 56), loaded.GetColor("colonise.item"));
        Assert.Equal(Color.FromArgb(72, 56, 56, 56), loaded.GetColor("colonise.rowHighlight"));
    }

    /// <summary>Retains explicitly saved categories and supplies a default for unrecognized custom palettes.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomPalettePreservesExplicitCategory(bool hasCategory)
    {
        string path = Path.Combine(temporaryDirectory, "theme.json");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            path,
            hasCategory
                ? "{\"orange\":\"#123456\",\"colonize\":{\"category\":\"#ABCDEF\"}}"
                : "{\"orange\":\"#123456\"}"
        );
        var store = new LegacyOverlayThemeStore(path);

        LegacyOverlayTheme loaded = store.Load();
        _ = store.Save(loaded);

        Assert.Null(loaded.Error);
        Assert.Equal(Color.Parse(hasCategory ? "#ABCDEF" : "#FFC100"), store.Load().GetColor("colonize.category"));
        Assert.Equal(Color.Parse("#123456"), store.Load().GetColor("orange"));
    }

    /// <summary>Places an independently editable category picker immediately above Item.</summary>
    [Fact]
    public void CategoryEditorPrecedesItemAndSavesIndependently()
    {
        var store = new LegacyOverlayThemeStore(Path.Combine(temporaryDirectory, "theme.json"));
        var viewModel = new OverlayThemeSettingsViewModel(
            store,
            new OverlayThemeStateStore(Path.Combine(temporaryDirectory, "states.json"))
        );
        OverlayThemeColorEditorViewModel[] editors = viewModel
            .Categories.Single(category => category.Name == "Colonization")
            .Colors.ToArray();
        int itemIndex = Array.FindIndex(editors, editor => editor.Key == "colonise.item");
        OverlayThemeColorEditorViewModel category = editors[itemIndex - 1];
        Color itemColor = editors[itemIndex].Color;

        Assert.Equal("Item Category", category.DisplayName);
        Assert.Equal("colonize.category", category.Key);
        category.HexValue = "#ABCDEF";
        viewModel.ApplyCommand.Execute(null);

        Assert.Equal(Color.Parse("#ABCDEF"), store.Load().GetColor("colonize.category"));
        Assert.Equal(itemColor, store.Load().GetColor("colonise.item"));
    }

    /// <summary>Verifies category and item brushes remain independent in the rendered overlay and refresh live.</summary>
    [AvaloniaFact]
    public void OverlayRendersIndependentCategoryAndItemBrushes()
    {
        Application application = Application.Current!;
        object? originalCategory = application.Resources["RavenOverlayColoniseCategoryBrush"];
        object? originalItem = application.Resources["RavenOverlayColoniseItemBrush"];
        var categoryBrush = new SolidColorBrush(Color.Parse("#ABCDEF"));
        var itemBrush = new SolidColorBrush(Color.Parse("#FEDCBA"));
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(
            new ColonizationCommodityPlan
            {
                Title = "Test build",
                ProjectNames = [],
                FleetCarriers = [],
                Rows =
                [
                    new()
                    {
                        Commodity = "steel",
                        DisplayName = "Steel",
                        Category = "Metals",
                        Needed = 10,
                    },
                    new()
                    {
                        Commodity = "robotics",
                        DisplayName = "Robotics",
                        Category = "Technology",
                        Needed = 10,
                    },
                    new()
                    {
                        Commodity = "water",
                        DisplayName = "Water",
                        Category = "Chemicals",
                        Needed = 10,
                    },
                ],
            },
            null
        );
        var window = new Window { Content = new ColonizationCommodityOverlayPresentation { DataContext = viewModel } };
        try
        {
            application.Resources["RavenOverlayColoniseCategoryBrush"] = categoryBrush;
            application.Resources["RavenOverlayColoniseItemBrush"] = itemBrush;
            OverlayThemeResources.Apply(window);
            window.Show();
            window.UpdateLayout();
            TextBlock[] labels = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            TextBlock[] categories = labels
                .Where(label => label.Text is "Metals" or "Technology" or "Chemicals")
                .ToArray();
            TextBlock[] items = labels
                .Where(label => label.IsVisible && label.Text is "Steel" or "Robotics" or "Water")
                .ToArray();

            Assert.Equal(3, categories.Length);
            Assert.Equal(3, items.Length);
            Assert.All(categories, label => Assert.Same(categoryBrush, label.Foreground));
            Assert.All(items, label => Assert.Same(itemBrush, label.Foreground));
            var changedCategory = new SolidColorBrush(Color.Parse("#FFC100"));
            window.Resources["RavenColoniseCategoryBrush"] = changedCategory;
            Assert.All(categories, label => Assert.Same(changedCategory, label.Foreground));
            Assert.All(items, label => Assert.Same(itemBrush, label.Foreground));
        }
        finally
        {
            window.Close();
            application.Resources["RavenOverlayColoniseCategoryBrush"] = originalCategory;
            application.Resources["RavenOverlayColoniseItemBrush"] = originalItem;
        }
    }

    /// <summary>Removes temporary theme fixtures after each test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
