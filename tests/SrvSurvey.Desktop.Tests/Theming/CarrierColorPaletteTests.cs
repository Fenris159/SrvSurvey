using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class CarrierColorPaletteTests
{
    /// <summary>
    /// Checks repeatable carrier colors beyond the original ten-carrier request in both theme variants.
    /// </summary>
    [Fact]
    public void MoreThanTenCarriersHaveStableDistinctColorsAndThemeAppropriateBrightness()
    {
        foreach (bool isDark in new[] { false, true })
        {
            Color[] colors = Enumerable.Range(1, 100).Select(id => CarrierColorPalette.GetColor(id, isDark)).ToArray();
            Assert.Equal(100, colors.Distinct().Count());
            Assert.All(colors, color => Assert.Equal(255, color.A));
            Assert.Equal(colors[41], CarrierColorPalette.GetColor(42, isDark));
        }
        Assert.NotEqual(CarrierColorPalette.GetColor(42, true), CarrierColorPalette.GetColor(42, false));
    }

    /// <summary>Measures swatch contrast against each supported raised theme surface.</summary>
    [Fact]
    public void SwatchesRemainVisibleAgainstEveryApplicationThemeSurface()
    {
        foreach (RavenThemeDefinition theme in RavenThemeCatalog.All)
        {
            double background = Luminance(Color.Parse(theme.RaisedSurfaceColor));
            foreach (int id in Enumerable.Range(1, 100))
            {
                double foreground = Luminance(CarrierColorPalette.GetColor(id, theme.IsDark));
                double contrast = (Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05);
                Assert.True(contrast >= 3, $"Carrier {id} contrast in {theme.Key}: {contrast}");
            }
        }
    }

    /// <summary>Computes relative luminance for the palette contrast checks.</summary>
    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    /// <summary>Converts an sRGB channel to linear light before measuring contrast.</summary>
    private static double Linear(byte value)
    {
        double channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
