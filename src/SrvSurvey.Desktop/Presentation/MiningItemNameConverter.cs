using System.Globalization;
using Avalonia.Data.Converters;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>Formats mining table names without changing journal identifiers or stored observations.</summary>
public sealed class MiningItemNameConverter : IValueConverter
{
    public static MiningItemNameConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            CargoItem cargo => FormatCargoName(cargo, culture),
            MiningProspect prospect => string.Join(
                " · ",
                prospect.Materials.Select(material =>
                    $"{DisplayName(material.Name, culture)} {material.Percentage.ToString("0.0", culture)}%"
                )
            ),
            string name => DisplayName(name, culture),
            _ => string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Shares the exact visible label with cargo sorting while retaining raw journal identifiers.</summary>
    public static string FormatCargoName(CargoItem cargo, CultureInfo culture) =>
        DisplayName(string.IsNullOrWhiteSpace(cargo.LocalizedName) ? cargo.Name : cargo.LocalizedName, culture);

    private static string DisplayName(string name, CultureInfo culture)
    {
        string display = MiningCommodityName.Canonical(name.Trim());
        return display.Length == 0 ? display : char.ToUpper(display[0], culture) + display[1..];
    }
}
