using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MeritSystemRowViewModelTests
{
    [Fact]
    public void RowsUsePowerStateAndStationArtwork()
    {
        var row = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem(
                "Paesia",
                12.3,
                "",
                "",
                "",
                250000,
                [
                    new PowerplayMeritRing("Paesia 2 A Ring", "Platinum"),
                    new PowerplayMeritRing("Paesia 2 B Ring", "Painite"),
                ],
                [
                    Station("A", "Coriolis Starport"),
                    Station("B", "Orbis Starport"),
                    Station("C", "Ocellus Starport"),
                    Station("D", "Asteroid base"),
                    Station("E", "Settlement"),
                    Station("F", "Planetary Outpost"),
                    Station("G", "Surface Port"),
                    Station("H", "Outpost"),
                ]
            )
        );

        Assert.Equal("No controlling Power", row.Power);
        Assert.Equal("Unknown", row.StateText);
        Assert.Equal("", row.StateIcon);
        Assert.Contains("ly", row.Distance, StringComparison.Ordinal);
        Assert.Equal("Planet", row.Rings[0].Icon);
        Assert.Equal("", row.Rings[1].Icon);
        Assert.Equal(
            ["Coriolis", "Orbis", "Ocellus", "Asteroid", "Settlement", "SurfacePort", "SurfacePort", "Outpost"],
            row.Stations.Select(station => station.Icon)
        );

        var known = MeritSystemRowViewModel.From(
            new PowerplayMeritSystem("Sol", null, "Archon Delaine", "Exploited", "", 1, [], [])
        );
        Assert.Equal("", known.Distance);
        Assert.Equal("Archon Delaine", known.Power);
        Assert.Equal("Exploited", known.StateText);
    }

    [Fact]
    public void ChipBoxRejectsUnknownNamesAndRestoresAny()
    {
        var chips = new MiningChipBoxViewModel("Mineral / metal", ["Any", "Platinum", "Osmium"], "Any");
        chips.Query = "plat";
        Assert.Contains("Platinum", chips.Suggestions);
        chips.Query = "plat";
        chips.Add("Missing");
        Assert.Equal("Any", Assert.Single(chips.Selected));
        chips.Add("Platinum");
        chips.Add("Platinum");
        chips.Add("Any");
        Assert.Equal("Any", Assert.Single(chips.Selected));
        chips.Remove("Any");
        Assert.Equal("Any", Assert.Single(chips.Selected));
    }

    private static PowerplayMeritStation Station(string name, string type) => new(name, type, "L", 1, 1, name);
}
