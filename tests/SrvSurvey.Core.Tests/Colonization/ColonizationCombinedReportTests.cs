using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationCombinedReportTests
{
    /// <summary>Demand and delivery progress are weighted across projects, but shared physical cargo counts only once.</summary>
    [Fact]
    public void AggregatesRequirementsWithoutMultiplyingSharedCarrierStock()
    {
        ColonizationProjectPreviewData first = Build("first", 400, 200, 100);
        ColonizationProjectPreviewData second = Build("second", 1000, 300, 150);
        second = second with
        {
            Project = second.Project with
            {
                Commodities = new()
                {
                    [" steel "] = 300,
                    ["STEEL"] = 300,
                    ["copper"] = 10,
                },
                Commanders = new() { ["CMDR"] = ["copper"] },
                LinkedFleetCarriers = [.. second.Project.LinkedFleetCarriers, new() { MarketId = 2, Name = "Second" }],
            },
            CarrierCargo = new()
            {
                ["1"] = new() { ["steel"] = 150 },
                ["2"] = new() { ["steel"] = 50, ["copper"] = 20 },
            },
        };
        var report = ColonizationProjectPreview.CreateCombined([first, second, first], DateTimeOffset.UnixEpoch);
        Assert.True(report.IsCombined);
        Assert.Equal(2, report.Projects.Count);
        Assert.Equal(1400, report.MaximumRequired);
        Assert.Equal(510, report.Remaining);
        Assert.Equal(890, report.Delivered);
        Assert.Equal(100d * 890 / 1400, report.Progress);
        Assert.Equal(300, report.CarrierDeficit);
        Assert.Equal(210, report.ReadyOnCarriers);
        Assert.Equal(2, report.Carriers.Count);
        Assert.Equal(500, report.Rows.Single(row => row.Key == "steel").Need);
        Assert.Equal(150, report.Carriers.Single(carrier => carrier.MarketId == 1).TotalCargo);
        Assert.Equal(2, Assert.Single(report.Commanders).Value.Count);
        Assert.Equal(4, ColonizationProjectPreview.Trips(report.Remaining, 128));
    }

    /// <summary>Missing optional history is unknown, while a shared carrier can recover stock from another linked project's response.</summary>
    [Fact]
    public void PreservesUnknownSectionsAndUsesAvailableSharedStock()
    {
        ColonizationProjectPreviewData first = Build("first", 400, 200, 100);
        ColonizationProjectPreviewData second = Build("second", 0, 200, 150) with
        {
            CarrierCargo = null,
            Statistics = null,
        };
        var report = ColonizationProjectPreview.CreateCombined([first, second], DateTimeOffset.UnixEpoch);
        Assert.Equal(300, report.CarrierDeficit);
        Assert.Null(report.Statistics);
        Assert.Null(report.Progress);
        second = second with { Project = second.Project with { LinkedFleetCarriers = [new() { MarketId = 2 }] } };
        report = ColonizationProjectPreview.CreateCombined([first, second], DateTimeOffset.UnixEpoch);
        Assert.Null(report.CarrierDeficit);
        Assert.Null(report.ReadyOnCarriers);
        Assert.Throws<ArgumentException>(() => ColonizationProjectPreview.CreateCombined([], DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentNullException>(() =>
            ColonizationProjectPreview.CreateCombined(null!, DateTimeOffset.UnixEpoch)
        );
    }

    /// <summary>Large aggregate requirements and delivery histories retain 64-bit precision, merging commander identities and hourly buckets.</summary>
    [Fact]
    public void CombinesHistoriesAndLargeTotals()
    {
        ColonizationProjectPreviewData first = Build("first", int.MaxValue, int.MaxValue, 0);
        ColonizationProjectPreviewData second = Build("second", int.MaxValue, int.MaxValue, 0) with
        {
            Statistics = new()
            {
                TotalCargo = 50,
                TotalDeliveries = int.MaxValue,
                Start = DateTimeOffset.UnixEpoch.AddHours(-1),
                End = DateTimeOffset.UnixEpoch.AddHours(1),
                Cmdrs = new() { ["CMDR"] = 50 },
                Stats =
                [
                    new()
                    {
                        Time = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)),
                        Cmdrs = new() { ["CMDR"] = 50 },
                    },
                ],
            },
        };
        var report = ColonizationProjectPreview.CreateCombined([first, second], DateTimeOffset.UnixEpoch);
        Assert.Equal(2L * int.MaxValue, report.Remaining);
        Assert.Equal(2L * int.MaxValue, report.MaximumRequired);
        Assert.Equal(0, report.Progress);
        ColonizationProjectStatistics history = Assert.IsType<ColonizationProjectStatistics>(report.Statistics);
        Assert.Equal(80, history.TotalCargo);
        Assert.Equal((long)int.MaxValue + 2, history.TotalDeliveries);
        Assert.Equal(80, Assert.Single(history.Cmdrs).Value);
        Assert.Equal(80, Assert.Single(history.Stats).Total);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(-1), history.Start);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(1), history.End);
    }

    /// <summary>Effects are combined within their own system; unknown builds and per-project prerequisites remain explicit.</summary>
    [Fact]
    public void ExportsCompactReportWithCombinedEffectsAndLinkedCommanders()
    {
        ColonizationProjectPreviewData first = Build("first", 400, 200, 100);
        ColonizationProjectPreviewData second = Build("second", 1000, 300, 150);
        ColonizationProjectPreviewData unknown = Build("unknown", 100, 10, 0) with
        {
            Project = new()
            {
                BuildId = "unknown",
                BuildName = "Unknown build",
                SystemName = "Elsewhere",
                BuildType = "unknown",
            },
        };
        var report = ColonizationProjectPreview.CreateCombined([first, second, unknown], DateTimeOffset.UnixEpoch);
        var effects = ColonizationProjectCsvExporter.CombinedEffectDetails(report).ToDictionary();
        Assert.Equal("10", effects["Example · System score"]);
        Assert.Equal("+20", effects["Example · Security"]);
        Assert.Equal("2", effects["Example · Needs Tier 2 points"]);
        Assert.Contains("first: a military installation", effects["Example · Requires"]);
        Assert.Equal("Unknown", effects["Elsewhere · System score"]);
        Assert.Equal("Unknown build", effects["Elsewhere · Reference data missing"]);
        Assert.Equal("Small: 0, Medium: 0, Large: 0", effects["Example · Landing pads"]);
        string csv = ColonizationProjectCsvExporter.Write(report, 128);
        Assert.StartsWith("\"Combined report\"\r\n\"Field\",\"Value\"", csv);
        Assert.Contains("\"Build projects\"\r\n\"Project\",\"Build type\",\"System\",\"Build ID\"", csv);
        Assert.Contains("\"Linked commanders\"\r\n\"Commander\",\"Commodity assignments\"", csv);
        Assert.Contains("\"Cmdr\",\"steel\"", csv);
        Assert.Contains("\"Example · Security\",\"'+20\"", csv);
        Assert.Contains("\"Tracked cargo delivered\",\"90\"", csv);
        Assert.Equal(
            "Raven-combined-build-report-19700101-000000.csv",
            ColonizationProjectCsvExporter.SuggestedFileName(report)
        );
        Assert.DoesNotContain("Project details", csv);
    }

    /// <summary>Sums layout-specific pads and resolves readable project types in combined report cards.</summary>
    [Fact]
    public void AddsLayoutSpecificLandingPadsAcrossBuilds()
    {
        ColonizationProjectPreviewData first = Build("first", 400, 200, 100);
        first = first with { Project = first.Project with { BuildType = "no_truss" } };
        ColonizationProjectPreviewData second = first with { Project = first.Project with { BuildId = "second" } };
        var report = ColonizationProjectPreview.CreateCombined([first, second], DateTimeOffset.UnixEpoch);
        ColonizationPreviewSystemEffects system = Assert.Single(
            ColonizationProjectCsvExporter.SystemEffectGroups(report)
        );
        Assert.Contains(
            system.Fields,
            field => field.Key == "Landing pads" && field.Value == "Small: 16, Medium: 22, Large: 10"
        );
        Assert.Equal(
            "Example · Military Hub",
            Assert
                .Single(
                    ColonizationProjectCsvExporter.BuildDetails(
                        new(Build("third", 400, 200, 100), DateTimeOffset.UnixEpoch)
                    )
                )
                .Value
        );
    }

    /// <summary>Creates independent projects sharing one carrier and one commander.</summary>
    private static ColonizationProjectPreviewData Build(string id, int maximum, int need, int stock) =>
        new(
            new ColonizationProject
            {
                BuildId = id,
                BuildName = id,
                BuildType = "ares",
                SystemName = "Example",
                MaximumRequired = maximum,
                Commodities = new() { ["steel"] = need },
                Commanders = new() { ["Cmdr"] = ["steel"] },
                LinkedFleetCarriers =
                [
                    new()
                    {
                        MarketId = 1,
                        Name = "ABC-123",
                        DisplayName = "Shared",
                    },
                ],
            },
            new() { ["1"] = new() { ["steel"] = stock } },
            new()
            {
                TotalCargo = 30,
                TotalDeliveries = 2,
                Cmdrs = new() { ["Cmdr"] = 30 },
                Stats =
                [
                    new()
                    {
                        Time = DateTimeOffset.UnixEpoch,
                        Cmdrs = new() { ["Cmdr"] = 30 },
                    },
                ],
            }
        );
}
