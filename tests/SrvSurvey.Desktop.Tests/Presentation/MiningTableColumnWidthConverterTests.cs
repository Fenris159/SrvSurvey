using System.Globalization;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class MiningTableColumnWidthConverterTests
{
    [Theory]
    [InlineData(1124, "CargoName", 990)]
    [InlineData(1124, "MaterialsName", 296)]
    [InlineData(640, "MaterialsQuality", 95)]
    [InlineData(100, "CargoName", 126)]
    [InlineData(1124, "HistorySystem", 200)]
    [InlineData(500, "ProspectsMinerals", 266)]
    [InlineData(360, "EngineeringName", 148)]
    [InlineData(700, "NoticesText", 276)]
    public void ColumnsDistributeAvailableWidthAndKeepAMinimumAtNarrowSizes(
        double viewport,
        string column,
        double expected
    ) =>
        Assert.Equal(
            expected,
            MiningTableColumnWidthConverter.Instance.Convert(
                viewport,
                typeof(double),
                column,
                CultureInfo.InvariantCulture
            )
        );

    [Theory]
    [InlineData(100, "640", 640)]
    [InlineData(1124, "640", 1124)]
    [InlineData(1124, "unknown", 1124)]
    public void TablesFillTheViewportWhileRetainingTheirReadableMinimum(
        double viewport,
        string minimum,
        double expected
    ) =>
        Assert.Equal(
            expected,
            MiningTableColumnWidthConverter.MinimumTableWidth.Convert(
                viewport,
                typeof(double),
                minimum,
                CultureInfo.InvariantCulture
            )
        );

    [Fact]
    public void AnUnrecognizedColumnDoesNotReserveTableSpace() =>
        Assert.Equal(
            0d,
            MiningTableColumnWidthConverter.Instance.Convert(
                1124d,
                typeof(double),
                "unknown",
                CultureInfo.InvariantCulture
            )
        );
}
