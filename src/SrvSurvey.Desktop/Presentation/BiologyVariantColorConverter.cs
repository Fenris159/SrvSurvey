using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Presentation;

public sealed class BiologyVariantColorConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, Color> VariantColors =
        new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["Amethyst"] = ColorFromHex("#B57EDC"),
            ["Aquamarine"] = ColorFromHex("#7FFFD4"),
            ["Blue"] = ColorFromHex("#4DA3FF"),
            ["Cobalt"] = ColorFromHex("#3D7DFF"),
            ["Cyan"] = ColorFromHex("#54DFED"),
            ["Emerald"] = ColorFromHex("#00C878"),
            ["Gold"] = ColorFromHex("#FFD447"),
            ["Green"] = ColorFromHex("#43D65C"),
            ["Grey"] = ColorFromHex("#A8A8A8"),
            ["Indigo"] = ColorFromHex("#7C83FD"),
            ["Lime"] = ColorFromHex("#A8E66C"),
            ["Magenta"] = ColorFromHex("#FF66D9"),
            ["Maroon"] = ColorFromHex("#C45A7A"),
            ["Mauve"] = ColorFromHex("#D6A0D5"),
            ["Mulberry"] = ColorFromHex("#C05A9D"),
            ["Ocher"] = ColorFromHex("#D8A03A"),
            ["Orange"] = ColorFromHex("#FF8C42"),
            ["Peach"] = ColorFromHex("#FFB38A"),
            ["Red"] = ColorFromHex("#FF625F"),
            ["Sage"] = ColorFromHex("#A8C686"),
            ["Teal"] = ColorFromHex("#4FD1C5"),
            ["Turquoise"] = ColorFromHex("#40E0D0"),
            ["White"] = ColorFromHex("#F4F4F4"),
            ["Yellow"] = ColorFromHex("#FFEB3B"),
        };

    public static bool Supports(string? variant) =>
        !string.IsNullOrWhiteSpace(variant)
        && VariantColors.ContainsKey(variant.Trim());

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is string variant
            && VariantColors.TryGetValue(variant.Trim(), out var color)
                ? new SolidColorBrush(color)
                : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static Color ColorFromHex(string value) => Color.Parse(value);
}
