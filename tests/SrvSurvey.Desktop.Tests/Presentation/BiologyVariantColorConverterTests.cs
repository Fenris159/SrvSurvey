using System.Globalization;
using Avalonia;
using Avalonia.Media;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class BiologyVariantColorConverterTests
{
    [Theory]
    [InlineData("Emerald", "#00C878")]
    [InlineData("Grey", "#A8A8A8")]
    [InlineData("Lime", "#A8E66C")]
    [InlineData("Yellow", "#FFEB3B")]
    public void MapsBiologicalVariantNamesToSemanticTextColors(
        string variant,
        string expected)
    {
        var converter = new BiologyVariantColorConverter();

        var brush = Assert.IsType<ISolidColorBrush>(
            converter.Convert(
                variant,
                typeof(IBrush),
                null,
                CultureInfo.InvariantCulture),
            exactMatch: false);

        Assert.Equal(Color.Parse(expected), brush.Color);
    }

    [Fact]
    public void UnknownVariantUsesThePresentationFallback()
    {
        var converter = new BiologyVariantColorConverter();

        var result = converter.Convert(
            "Not a biological color",
            typeof(IBrush),
            null,
            CultureInfo.InvariantCulture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }
}
