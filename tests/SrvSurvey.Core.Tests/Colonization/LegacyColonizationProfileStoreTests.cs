using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class LegacyColonizationProfileStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-colony-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LegacyProjectSelectionsAndFleetCarrierCargoAreLoadedReadOnly()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-colony.json");
        const string json =
            """
            {
              "fid": "F123",
              "cmdr": "Test Cmdr",
              "primaryBuildId": "build-1",
              "hiddenIDs": ["build-2", "BUILD-2", ""],
              "projects": [
                {
                  "buildId": "build-1",
                  "buildType": "no_truss",
                  "buildName": "Primary port",
                  "systemName": "Test System",
                  "maxNeed": 1000,
                  "sumNeed": 250,
                  "commodities": {"steel": 250},
                  "linkedFC": [
                    {"marketId": 42, "name": "ABC-123", "assign": ["steel"]}
                  ]
                },
                {"buildId": "build-2", "buildName": "Hidden port"},
                {"buildName": "Broken cache row"}
              ],
              "linkedFCs": {
                "42": {
                  "marketId": 42,
                  "name": "ABC-123",
                  "displayName": "Carrier",
                  "cargo": {"steel": 80}
                }
              }
            }
            """;
        await File.WriteAllTextAsync(path, json);

        var result = await new LegacyColonizationProfileStore(
            temporaryDirectory).LoadAsync("F123");

        Assert.True(result.Exists);
        Assert.Null(result.Error);
        var snapshot = Assert.IsType<LegacyColonizationProfileSnapshot>(
            result.Snapshot);
        Assert.Equal("Test Cmdr", snapshot.CommanderName);
        Assert.Equal("build-1", snapshot.PrimaryProjectId);
        Assert.Equal(["build-2"], snapshot.HiddenProjectIds);
        Assert.Equal(2, snapshot.Projects.Count);
        Assert.Equal(250, snapshot.Projects[0].RemainingRequired);
        Assert.Equal(
            ["steel"],
            snapshot.Projects[0].LinkedFleetCarriers[0].AssignedCommodities);
        Assert.Equal(80, Assert.Single(snapshot.FleetCarriers).Cargo["steel"]);
        Assert.Single(result.Warnings);
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task MalformedProfileIsReportedWithoutChangingTheFile()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-colony.json");
        const string malformed = "{\"projects\":[";
        await File.WriteAllTextAsync(path, malformed);

        var result = await new LegacyColonizationProfileStore(
            temporaryDirectory).LoadAsync("F123");

        Assert.True(result.Exists);
        Assert.Null(result.Snapshot);
        Assert.NotNull(result.Error);
        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
