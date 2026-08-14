using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace SrvSurvey.Desktop.Presentation;

public sealed class BoxelSurveyIndentMarginConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var indent = value is int amount ? amount : 0;
        return new Thickness(indent == 0 ? 0 : 24, 0, 0, 0);
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}
