using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationProjectPreviewTests
{
    /// <summary>Checks the user's example against the live Raven table values, including zero-need carrier surplus.</summary>
    [Fact]
    public void MatchesChomskyCargoDeficitTripsAndEffects()
    {
        var preview = new ColonizationProjectPreview(Example(), DateTimeOffset.UnixEpoch);
        Assert.Equal(9922, preview.Remaining);
        Assert.Equal(8261, preview.CarrierDeficit);
        Assert.Equal(1661, preview.ReadyOnCarriers);
        Assert.Equal(0, preview.Delivered);
        Assert.Equal(0, preview.Progress);
        Assert.Equal(13, ColonizationProjectPreview.Trips(preview.Remaining, 794));
        Assert.Equal(25, ColonizationProjectPreview.Trips(preview.Remaining, 400));
        Assert.Equal(11, ColonizationProjectPreview.Trips(preview.CarrierDeficit, 794));
        Assert.Equal(21, ColonizationProjectPreview.Trips(preview.CarrierDeficit, 400));
        ColonizationBuildEffects effects = Assert.IsType<ColonizationBuildEffects>(preview.Effects);
        Assert.Equal("Military Hub", effects.Name);
        Assert.Equal(5, effects.Score);
        Assert.Equal(10, effects.Effects["Security"]);
        Assert.Equal("a military installation", effects.Prerequisite);
        Assert.Equal(1, effects.NeedsCount);
        Assert.Equal(3, effects.GivesTier);
        Assert.Equal(9922, effects.AverageHaul);
        Assert.Equal("No landing pads", effects.Pads);
        Assert.Equal(34, preview.Rows.Single(row => row.Key == "tritium").CarrierDifference);
        Assert.Equal(48, preview.Rows.Single(row => row.Key == "medicaldiagnosticequipment").CarrierDifference);
        Assert.Equal(109, preview.Rows.Single(row => row.Key == "basicmedicines").CarrierQuantities[1]);
    }

    /// <summary>Ensures unavailable carrier stock cannot be mistaken for zero stock or a confirmed deficit.</summary>
    [Fact]
    public void LeavesMissingCarrierStockUnknownAndDeduplicatesCarrierLinks()
    {
        ColonizationProjectPreviewData data = Example();
        data = data with
        {
            Project = data.Project with
            {
                LinkedFleetCarriers = [.. data.Project.LinkedFleetCarriers, data.Project.LinkedFleetCarriers[0]],
            },
            CarrierCargo = null,
            Statistics = null,
        };
        var preview = new ColonizationProjectPreview(data, DateTimeOffset.UnixEpoch);
        Assert.Equal(2, preview.Carriers.Count);
        Assert.Null(preview.CarrierDeficit);
        Assert.Null(preview.ReadyOnCarriers);
        Assert.Null(preview.Statistics);
        Assert.All(preview.Rows, row => Assert.Null(row.CarrierDifference));
        Assert.All(preview.Carriers, carrier => Assert.Null(carrier.TotalCargo));
    }

    /// <summary>Handles legacy placeholders, aliases, unknown commodities, and a build without linked carriers.</summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(20, 50d)]
    [InlineData(5, 0d)]
    public void NormalizesNeedAndClampsProgress(int maximum, double? progress)
    {
        var project = new ColonizationProject
        {
            BuildId = "example",
            BuildType = "unknown-build",
            MaximumRequired = maximum,
            Commodities = new()
            {
                ["STEEL"] = 10,
                [" steel "] = 10,
                ["newcommodity"] = -1,
                [""] = 99,
            },
        };
        var preview = new ColonizationProjectPreview(new(project, [], null), DateTimeOffset.UnixEpoch);
        Assert.Equal(10, preview.Remaining);
        Assert.Equal(10, preview.CarrierDeficit);
        Assert.Equal(progress, preview.Progress);
        Assert.Null(preview.Effects);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("Other", preview.Rows.Single(row => row.Key == "newcommodity").Category);
        Assert.Equal(0, preview.ReadyOnCarriers);
    }

    /// <summary>Checks unavailable capacities, empty loads, and round-up trip boundaries.</summary>
    [Theory]
    [InlineData(null, 100, null)]
    [InlineData(10, 0, null)]
    [InlineData(10, -1, null)]
    [InlineData(0, 100, 0)]
    [InlineData(101, 100, 2)]
    public void CalculatesTripBoundaries(int? quantity, int capacity, int? expected)
    {
        Assert.Equal<long?>(expected, ColonizationProjectPreview.Trips(quantity, capacity));
    }

    /// <summary>Shows layout-specific landing pad counts and reference haul for a starport.</summary>
    [Theory]
    [InlineData("no_truss", "Small: 8, Medium: 11, Large: 5")]
    [InlineData("dodec", "Small: 4, Medium: 11, Large: 5")]
    [InlineData("installation", "No landing pads")]
    public void ShowsReferencePadsForEachLayout(string layout, string pads)
    {
        var preview = new ColonizationProjectPreview(
            new(new ColonizationProject { BuildType = layout }, [], null),
            DateTimeOffset.UnixEpoch
        );
        Assert.Equal(pads, preview.Effects!.Pads);
    }

    /// <summary>Legacy and renamed display keys represent the same stock, including non-build artifacts in carrier totals.</summary>
    [Fact]
    public void DoesNotDoubleCountRenamedCommodityAliases()
    {
        var project = new ColonizationProject
        {
            Commodities = new() { ["microbialfurnaces"] = 25 },
            LinkedFleetCarriers = [new() { MarketId = 1 }],
        };
        var cargo = new Dictionary<string, int>
        {
            ["heliostaticfurnaces"] = 10,
            ["microbial furnaces"] = 10,
            ["ancientrelic"] = 2,
            ["guardian relic"] = 2,
            ["thargoidtitandrivecomponent"] = 1,
            ["Titan drive component"] = 1,
            ["hazardousenvironmentsuits"] = 5,
            ["H.E. Suits"] = 5,
        };
        var preview = new ColonizationProjectPreview(
            new(project, new() { ["1"] = cargo }, null),
            DateTimeOffset.UnixEpoch
        );
        Assert.Equal(18, Assert.Single(preview.Carriers).TotalCargo);
        Assert.Equal(15, preview.CarrierDeficit);
        Assert.Equal(10, preview.ReadyOnCarriers);
        Assert.Equal(-15, preview.Rows.Single(row => row.Key == "heliostaticfurnaces").CarrierDifference);
        Assert.Equal(2, preview.Rows.Count);
    }

    /// <summary>Supplies the public example's material quantities without requiring network access in tests.</summary>
    internal static ColonizationProjectPreviewData Example()
    {
        var needs = new Dictionary<string, int>
        {
            ["aluminium"] = 2414,
            ["basicmedicines"] = 109,
            ["battleweapons"] = 42,
            ["buildingfabricators"] = 333,
            ["ceramiccomposites"] = 375,
            ["cmmcomposite"] = 333,
            ["combatstabilisers"] = 25,
            ["computercomponents"] = 75,
            ["copper"] = 105,
            ["emergencypowercells"] = 63,
            ["evacuationshelter"] = 75,
            ["foodcartridges"] = 125,
            ["fruitandvegetables"] = 42,
            ["microcontrollers"] = 55,
            ["militarygradefabrics"] = 42,
            ["polymers"] = 708,
            ["powergenerators"] = 38,
            ["reactivearmour"] = 42,
            ["steel"] = 3746,
            ["structuralregulators"] = 542,
            ["surfacestabilisers"] = 583,
            ["survivalequipment"] = 50,
        };
        var ready = new Dictionary<string, int>
        {
            ["basic medicines"] = 109,
            ["basicmedicines"] = 109,
            ["battleweapons"] = 42,
            ["ceramiccomposites"] = 375,
            ["cmmcomposite"] = 333,
            ["combatstabilisers"] = 25,
            ["emergencypowercells"] = 63,
            ["evacuationshelter"] = 75,
            ["microcontrollers"] = 55,
            ["reactivearmour"] = 42,
            ["structuralregulators"] = 542,
        };
        var project = new ColonizationProject
        {
            BuildId = "test-build",
            BuildName = "Chomsky Enterprise",
            BuildType = "ares",
            MaximumRequired = 9922,
            RemainingRequired = 9922,
            Commodities = needs,
            Commanders = new() { ["Example Cmdr"] = [] },
            LinkedFleetCarriers =
            [
                new()
                {
                    MarketId = 1,
                    Name = "TZW-N2V",
                    DisplayName = "Carrier one",
                },
                new()
                {
                    MarketId = 2,
                    Name = "N4W-T0Z",
                    DisplayName = "Carrier two",
                },
            ],
        };
        return new(
            project,
            new()
            {
                ["1"] = new()
                {
                    ["tritium"] = 34,
                    ["medicaldiagnosticequipment"] = 48,
                    ["monazite"] = 880,
                },
                ["2"] = ready,
            },
            new()
        );
    }
}
