using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
    public async Task MarketSupplyTagsMatchShortSupplyColorAndKeepShoppingSelectionDistinct(string themeKey)
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
            Assert.Equal(SupplyBrushColor(legend.Foreground), SupplyBrushColor(shortage.Background));
            Assert.Equal(Color.Parse(theme.DangerForegroundColor), SupplyBrushColor(shortage.Foreground));
            Assert.Equal(Color.Parse(theme.TagFillColor), SupplyBrushColor(enough.Background));
            shortage.Command!.Execute(null);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            shortage = SupplyTag(window, "water");
            Assert.Equal(Color.Parse(theme.SecondaryFillColor), SupplyBrushColor(shortage.Background));
            Assert.Equal(Color.Parse(theme.DangerColor), SupplyBrushColor(shortage.BorderBrush));
            Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(shortage.Foreground));
            shortage.Command!.Execute(null);
            using WriteableBitmap? final = window.CaptureRenderedFrame();
            Assert.NotNull(final);
            Assert.Equal(Color.Parse(theme.DangerColor), SupplyBrushColor(SupplyTag(window, "water").Background));
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
    [InlineData("blue-light")]
    [InlineData("blue-dark")]
    [InlineData("orange-dark")]
    [InlineData("green-light")]
    [InlineData("green-dark")]
    [InlineData("monochrome-dark")]
    public async Task MarketSupplyTooltipsUseTheirOwnThemeTextColor(string themeKey)
    {
        using ColonizationShoppingViewModel model = SupplyThemeModel();
        await model.SearchAsync();
        RavenThemeDefinition theme = RavenThemeCatalog.Get(themeKey);
        var window = new ColonizationShoppingTestWindow(model) { Width = 1000 };
        ApplySupplyTheme(window, theme);
        try
        {
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
                TextBlock text = Assert.Single(
                    tooltip.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.Text == detail
                );
                Assert.Equal(Color.Parse(theme.HighestSurfaceColor), SupplyBrushColor(tooltip.Background));
                Assert.Equal(Color.Parse(theme.TextColor), SupplyBrushColor(text.Foreground));
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
