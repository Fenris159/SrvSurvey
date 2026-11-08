using System.Globalization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class MiningItemNameConverterTests
{
    [Theory]
    [InlineData("platinum", "Platinum")]
    [InlineData("lowtemperaturediamond", "Low Temperature Diamonds")]
    [InlineData("iron", "Iron")]
    [InlineData(" unlisted alloy ", "Unlisted alloy")]
    [InlineData("NaCl", "NaCl")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TableNamesUseCatalogSpellingOrCapitalizeTheirFirstLetter(string? name, string expected) =>
        Assert.Equal(expected, Display(name));

    [Theory]
    [InlineData("Limpets", "Limpets")]
    [InlineData(null, "Drones")]
    [InlineData(" ", "Drones")]
    public void CargoPrefersItsLocalizedNameAndPreservesItsJournalIdentifier(string? localized, string expected)
    {
        var item = new CargoItem("drones", localized, 12, 0);
        Assert.Equal(expected, Display(item));
        Assert.Equal("drones", item.Name);
    }

    [Fact]
    public void ProspectingTimelineCapitalizesEveryMineralAndFormatsItsPercentageForTheUser()
    {
        var prospect = new MiningProspect(
            DateTimeOffset.UnixEpoch,
            [new MiningMaterial("platinum", 42), new MiningMaterial("osmium", 11.5)],
            "",
            "High"
        );
        Assert.Equal("Platinum 42.0% · Osmium 11.5%", Display(prospect));
        Assert.Equal(
            "Platinum 42,0% · Osmium 11,5%",
            MiningItemNameConverter.Instance.Convert(
                prospect,
                typeof(string),
                null,
                CultureInfo.GetCultureInfo("fr-FR")
            )
        );
        Assert.Equal("osmium", prospect.Materials[1].Name);
    }

    private static object Display(object? value) =>
        MiningItemNameConverter.Instance.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
}
