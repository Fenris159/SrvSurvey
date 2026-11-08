using System.Globalization;
using Avalonia.Data.Converters;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>Distributes spare table width while retaining aligned, content-sized columns and horizontal scrolling.</summary>
public sealed class MiningTableColumnWidthConverter : IValueConverter
{
    private const double RowInset = 12;
    private const double ColumnSpacing = 20;

    private sealed record ColumnMeasure(double MinimumContentWidth, int ColumnCount, double FixedWidth, double Share)
    {
        public double ReservedWidth => 2 * RowInset + (ColumnCount - 1) * ColumnSpacing + FixedWidth;
    }

    private static readonly Dictionary<string, ColumnMeasure> Columns = new()
    {
        ["CargoName"] = new(260, 2, 90, 1),
        ["MaterialsName"] = new(640, 6, 0, 2.4 / 8.1),
        ["MaterialsFinds"] = new(640, 6, 0, 1 / 8.1),
        ["MaterialsQuality"] = new(640, 6, 0, 1.5 / 8.1),
        ["MaterialsAverage"] = new(640, 6, 0, 1.2 / 8.1),
        ["MaterialsBest"] = new(640, 6, 0, 1 / 8.1),
        ["MaterialsTons"] = new(640, 6, 0, 1 / 8.1),
        ["ProspectsMinerals"] = new(650, 3, 320, 1),
        ["EngineeringName"] = new(360, 3, 0, 0.5),
        ["EngineeringGrade"] = new(360, 3, 0, 0.25),
        ["EngineeringCount"] = new(360, 3, 0, 0.25),
        ["NoticesText"] = new(700, 3, 360, 1),
        ["HistorySystem"] = new(850, 5, 220, 1.2 / 4.8),
        ["HistoryRing"] = new(850, 5, 220, 1.6 / 4.8),
        ["HistoryTons"] = new(850, 5, 220, 1 / 4.8),
        ["HistoryRate"] = new(850, 5, 220, 1 / 4.8),
    };

    public static MiningTableColumnWidthConverter Instance { get; } = new();

    public static FuncValueConverter<double, string, double> MinimumTableWidth { get; } =
        new(
            (width, minimum) =>
                Math.Max(
                    width,
                    double.TryParse(minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out double limit)
                        ? limit
                        : 0
                )
        );

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && parameter is string column && Columns.TryGetValue(column, out ColumnMeasure? measure)
            ? Math.Floor((Math.Max(measure.MinimumContentWidth, width) - measure.ReservedWidth) * measure.Share)
            : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
