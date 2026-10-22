using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationNexusReportTests
{
    [Fact]
    public void ReportsSystemNeedsIncludingPlansWithoutBuildsAndCountsCarrierStockOnce()
    {
        ColonizationNexus nexus = Sample();
        var all = ColonizationProjectPreview.CreateNexusReport(nexus, null, DateTimeOffset.UnixEpoch);
        Assert.True(all.IsCombined);
        Assert.Equal(600, all.Remaining);
        Assert.Equal(700, all.MaximumRequired);
        Assert.Equal(100, all.Delivered);
        Assert.Equal(350, all.CarrierDeficit);
        Assert.Single(all.Projects);
        Assert.Equal(100d / 7, all.Progress!.Value, 6);
        ColonizationProject incompleteBuild = nexus.Systems[0].Builds[0] with { MaximumRequired = 0 };
        var authoritative = ColonizationProjectPreview.CreateNexusReport(
            nexus with
            {
                Systems = [nexus.Systems[0] with { Builds = [incompleteBuild] }],
            },
            null,
            DateTimeOffset.UnixEpoch
        );
        Assert.Equal(100d / 3, authoritative.Progress!.Value, 6);
        Assert.Single(all.Carriers);
        Assert.Equal(600, Assert.Single(all.Rows).Need);
        var planned = ColonizationProjectPreview.CreateNexusReport(nexus, "beta", DateTimeOffset.UnixEpoch);
        Assert.Empty(planned.Projects);
        Assert.Equal("Route", planned.Project.BuildName);
        Assert.Equal(400, planned.Remaining);
        Assert.Equal(150, planned.CarrierDeficit);
        Assert.Equal(0, planned.Progress);
        var absent = ColonizationProjectPreview.CreateNexusReport(nexus, "Unknown", DateTimeOffset.UnixEpoch);
        Assert.Equal(0, absent.Remaining);
        Assert.Empty(absent.Carriers);
    }

    [Fact]
    public void PreservesLargeReportedNeedsAndDoesNotInventRequirementsWhenUnavailable()
    {
        ColonizationNexus nexus = Sample() with
        {
            Systems =
            [
                new()
                {
                    Name = "Huge",
                    Needs = new() { ["steel"] = 5_000_000_000L },
                },
            ],
        };
        var report = ColonizationProjectPreview.CreateNexusReport(nexus, null, DateTimeOffset.UnixEpoch);
        Assert.Equal(5_000_000_000L, report.Remaining);
        Assert.Equal(5_000_000_000L, report.MaximumRequired);
        var unknown = ColonizationProjectPreview.CreateNexusReport(
            nexus with
            {
                Systems = [new() { Name = "Unknown" }],
            },
            null,
            DateTimeOffset.UnixEpoch
        );
        Assert.Null(unknown.Progress);
        Assert.Equal(0, unknown.Remaining);
        Assert.Throws<ArgumentNullException>(() =>
            ColonizationProjectPreview.CreateNexusReport(null!, null, DateTimeOffset.UnixEpoch)
        );
    }

    internal static ColonizationNexus Sample()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            DisplayName = "Carrier",
            Cargo = new() { ["steel"] = 250 },
        };
        return new()
        {
            Id = "route",
            Name = "Route",
            Owner = "Fenris",
            FleetCarriers = [carrier, carrier],
            Systems =
            [
                new()
                {
                    SystemAddress = 1,
                    Name = "Alpha",
                    Needs = new() { ["steel"] = 200 },
                    Total = 300,
                    Progress = 100,
                    Builds =
                    [
                        new()
                        {
                            BuildId = "build",
                            MaximumRequired = 300,
                            Commodities = new() { ["steel"] = 100 },
                        },
                    ],
                },
                new()
                {
                    SystemAddress = 2,
                    Name = "Beta",
                    Needs = new() { ["steel"] = 400 },
                    Total = 400,
                    BuildTypes = new() { ["outpost"] = 1 },
                },
            ],
        };
    }
}
