using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData("blue-light")]
    [InlineData("blue-dark")]
    [InlineData("orange-dark")]
    [InlineData("green-light")]
    [InlineData("green-dark")]
    [InlineData("monochrome-dark")]
    public async Task ShoppingListActionsMatchButtonStyleAndLockUsesTheSecondaryHighlight(string themeKey)
    {
        using ColonizationShoppingViewModel model = SupplyThemeModel();
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        RavenThemeDefinition theme = RavenThemeCatalog.Get(themeKey);
        var window = new ColonizationShoppingTestWindow(model) { Width = 1180 };
        ApplySupplyTheme(window, theme);
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Button open = window.FindControl<Button>("OpenShoppingWishList")!;
            ToggleButton toggle = window.FindControl<ToggleButton>("LockShoppingList")!;
            Button clear = window.FindControl<Button>("ClearShoppingList")!;
            Assert.True(toggle.Bounds.Left > open.Bounds.Right);
            Assert.True(clear.Bounds.Left > toggle.Bounds.Right);
            Assert.Equal(open.FontSize, toggle.FontSize);
            Assert.Equal(open.Padding, toggle.Padding);
            Assert.Equal(open.MinHeight, clear.MinHeight);
            LeftClick(window, toggle);
            using WriteableBitmap? lockedFrame = window.CaptureRenderedFrame();
            Assert.NotNull(lockedFrame);
            Assert.True(model.IsWishListLocked);
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(toggle.Background));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(toggle.Foreground));
            Assert.Equal(Color.Parse(theme.AccentColor), SupplyBrushColor(toggle.BorderBrush));
            ContentPresenter presenter = toggle.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(presenter.Background));
            if (themeKey == "monochrome-dark")
            {
                lockedFrame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-list-lock-monochrome.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            LeftClick(window, clear);
            Assert.Empty(model.CaptureState().WishList);
            Assert.True(model.IsWishListLocked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("blue-light")]
    [InlineData("blue-dark")]
    [InlineData("orange-dark")]
    [InlineData("green-light")]
    [InlineData("green-dark")]
    [InlineData("monochrome-dark")]
    public async Task MarketSupplyTagsHighlightShoppingSelectionsAndKeepShortSupplyDistinct(string themeKey)
    {
        using ColonizationShoppingViewModel model = SupplyThemeModel();
        await model.SearchAsync();
        RavenThemeDefinition theme = RavenThemeCatalog.Get(themeKey);
        var window = new ColonizationShoppingTestWindow(model) { Width = 1000 };
        ApplySupplyTheme(window, theme);
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Button shortage = SupplyTag(window, "water");
            Button enough = SupplyTag(window, "steel");
            ToggleButton legend = window.FindControl<ToggleButton>("ShowShortSupply")!;
            ToggleButton filter = window
                .FindControl<ItemsControl>("ShoppingNeeds")!
                .GetVisualDescendants()
                .OfType<ToggleButton>()
                .First();
            Assert.Equal(Color.Parse(theme.TagFillColor), SupplyBrushColor(filter.Background));
            Assert.Equal(SupplyBrushColor(legend.Foreground), SupplyBrushColor(shortage.BorderBrush));
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(shortage.Background));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(shortage.Foreground));
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(enough.Background));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(enough.Foreground));
            Assert.Equal(Color.Parse(theme.AccentColor), SupplyBrushColor(enough.BorderBrush));
            shortage.Command!.Execute(null);
            model
                .Results[0]
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
                .ToggleWishListCommand.Execute(null);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            shortage = SupplyTag(window, "water");
            enough = SupplyTag(window, "steel");
            Assert.Equal(SupplyBrushColor(legend.Foreground), SupplyBrushColor(shortage.Background));
            Assert.Equal(Color.Parse(theme.DangerColor), SupplyBrushColor(shortage.BorderBrush));
            Assert.Equal(Color.Parse(theme.DangerForegroundColor), SupplyBrushColor(shortage.Foreground));
            Assert.Equal(Color.Parse(theme.TagFillColor), SupplyBrushColor(enough.Background));
            Assert.Equal(Color.Parse(theme.AccentForegroundColor), SupplyBrushColor(enough.Foreground));
            Assert.Equal(Colors.Transparent, SupplyBrushColor(enough.BorderBrush));
            shortage.Command!.Execute(null);
            model
                .Results[0]
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
                .ToggleWishListCommand.Execute(null);
            using WriteableBitmap? final = window.CaptureRenderedFrame();
            Assert.NotNull(final);
            shortage = SupplyTag(window, "water");
            enough = SupplyTag(window, "steel");
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(shortage.Background));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(shortage.Foreground));
            Assert.Equal(Color.Parse(theme.DangerColor), SupplyBrushColor(shortage.BorderBrush));
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(enough.Background));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(enough.Foreground));
            Assert.Equal(Color.Parse(theme.AccentColor), SupplyBrushColor(enough.BorderBrush));
            if (themeKey == "monochrome-dark")
            {
                final.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-supply-monochrome.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("blue-light", "en")]
    [InlineData("blue-dark", "en")]
    [InlineData("orange-dark", "en")]
    [InlineData("green-light", "en")]
    [InlineData("green-dark", "en")]
    [InlineData("monochrome-dark", "en")]
    [InlineData("monochrome-dark", "de")]
    public async Task MarketSupplyTooltipsUseTheirOwnThemeTextColor(string themeKey, string language)
    {
        using ColonizationShoppingViewModel model = SupplyThemeModel();
        await model.SearchAsync();
        RavenThemeDefinition theme = RavenThemeCatalog.Get(themeKey);
        var window = new ColonizationShoppingTestWindow(model) { Width = 1000 };
        ApplySupplyTheme(window, theme);
        string previousLanguage = LocalizationCatalog.CurrentLanguage;
        try
        {
            LocalizationCatalog.Initialize(language);
            window.Show();
            foreach (string commodity in new[] { "steel", "water" })
            {
                using (WriteableBitmap? frame = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(frame);
                }
                Button tag = SupplyTag(window, commodity);
                ToolTip.SetIsOpen(tag, true);
                using WriteableBitmap? tooltipFrame = window.CaptureRenderedFrame();
                Assert.NotNull(tooltipFrame);
                ToolTip tooltip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>());
                string detail = Assert.IsType<ShoppingSupplyViewModel>(tag.DataContext).Detail;
                TextBlock[] texts = tooltip
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => !string.IsNullOrEmpty(block.Text))
                    .ToArray();
                foreach (TextBlock text in texts)
                {
                    LocalizationBehavior.SetEnabled(text, true);
                }
                string[] lines = detail.Split('\n');
                string[] translated = lines.Select(LocalizationCatalog.Translate).ToArray();
                Assert.Equal(translated, texts.SelectMany(text => text.Text!.Split('\n')));
                if (language != "en")
                {
                    Assert.All(lines, line => Assert.NotEqual(line, LocalizationCatalog.Translate(line)));
                }
                Assert.Equal(Color.Parse(theme.HighestSurfaceColor), SupplyBrushColor(tooltip.Background));
                Assert.All(
                    texts,
                    text => Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(text.Foreground))
                );
                if (themeKey == "monochrome-dark" && commodity == "steel")
                {
                    tooltipFrame.Save(
                        Path.Combine(Path.GetTempPath(), "srvsurvey-shopping-tooltip-monochrome.png"),
                        PngBitmapEncoderOptions.Default
                    );
                }
                ToolTip.SetIsOpen(tag, false);
            }
        }
        finally
        {
            window.Close();
            LocalizationCatalog.Initialize(previousLanguage);
        }
    }

    private static ColonizationShoppingViewModel SupplyThemeModel() =>
        new(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
                ColonizationShoppingViewModelTests.Quote(1, "water", 20),
            ])
        );

    private static Button SupplyTag(Window window, string commodity) =>
        Assert.Single(
            window.GetVisualDescendants().OfType<Button>(),
            button =>
                button.Classes.Contains("supply-button")
                && button.DataContext is ShoppingSupplyViewModel supply
                && supply.Need.Commodity == commodity
        );

    private static Color SupplyBrushColor(IBrush? brush) =>
        Assert.IsType<ISolidColorBrush>(brush, exactMatch: false).Color;

    private static void ApplySupplyTheme(Window window, RavenThemeDefinition theme)
    {
        window.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (
            KeyValuePair<string, string> entry in new Dictionary<string, string>
            {
                ["RavenWindowBrush"] = theme.WindowColor,
                ["RavenControlAccentBrush"] = theme.ControlAccentColor,
                ["RavenTagFillBrush"] = theme.TagFillColor,
                ["RavenAccentForegroundBrush"] = theme.AccentForegroundColor,
                ["RavenAccentBrush"] = theme.AccentColor,
                ["RavenSecondaryFillBrush"] = theme.SecondaryFillColor,
                ["RavenDangerBrush"] = theme.DangerColor,
                ["RavenDangerForegroundBrush"] = theme.DangerForegroundColor,
                ["RavenTextBrush"] = theme.TextColor,
                ["RavenHighestSurfaceBrush"] = theme.HighestSurfaceColor,
                ["RavenStrongBorderBrush"] = theme.StrongBorderColor,
            }
        )
        {
            window.Resources[entry.Key] = new SolidColorBrush(Color.Parse(entry.Value));
        }
    }
}
