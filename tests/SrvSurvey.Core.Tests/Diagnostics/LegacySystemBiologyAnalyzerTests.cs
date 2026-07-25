using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class LegacySystemBiologyAnalyzerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-biology-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task AggregatesSpeciesWithoutChangingOriginalSystemFiles()
    {
        var systemDirectory = Path.Combine(
            temporaryDirectory,
            "systems",
            "F123");
        Directory.CreateDirectory(systemDirectory);
        var firstPath = Path.Combine(systemDirectory, "First_1.json");
        await File.WriteAllTextAsync(
            firstPath,
            """
            {
              "future": { "preserved": true },
              "bodies": [
                {
                  "atmosphereComposition": {
                    "CarbonDioxide": 99.0,
                    "SulphurDioxide": 1.0
                  },
                  "organisms": [
                    { "speciesLocalized": "Aleoida Arcus" },
                    { "speciesLocalized": "Aleoida Arcus" },
                    { "genusLocalized": "Unresolved genus" }
                  ]
                },
                {
                  "atmosphereComposition": {},
                  "organisms": [
                    { "speciesLocalized": "Bacterium Cerbrus" }
                  ]
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Second_2.json"),
            """
            {
              "Bodies": [
                {
                  "AtmosphereComposition": {
                    "CarbonDioxide": 100.0
                  },
                  "Organisms": [
                    { "SpeciesLocalized": "Aleoida Arcus" }
                  ]
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "malformed.json"),
            "{\"bodies\":");
        var original = await File.ReadAllBytesAsync(firstPath);
        var analyzer = new LegacySystemBiologyAnalyzer(temporaryDirectory);

        var result = await analyzer.AnalyzeAsync("F123");

        Assert.Equal(3, result.CandidateFileCount);
        Assert.Equal(2, result.ProcessedFileCount);
        Assert.Equal(3, result.BodyCount);
        Assert.Equal(5, result.OrganismCount);
        var aleoida = Assert.Single(
            result.Species,
            species => species.Name == "Aleoida Arcus");
        Assert.Equal(3, aleoida.Count);
        Assert.Collection(
            aleoida.AtmosphereCompositions,
            atmosphere =>
            {
                Assert.Equal(
                    "CarbonDioxide,SulphurDioxide",
                    atmosphere.Components);
                Assert.Equal(2, atmosphere.Count);
            },
            atmosphere =>
            {
                Assert.Equal("CarbonDioxide", atmosphere.Components);
                Assert.Equal(1, atmosphere.Count);
            });
        var bacterium = Assert.Single(
            result.Species,
            species => species.Name == "Bacterium Cerbrus");
        var emptyAtmosphere = Assert.Single(bacterium.AtmosphereCompositions);
        Assert.Equal(string.Empty, emptyAtmosphere.Components);
        Assert.Single(result.Warnings);
        Assert.Contains("malformed.json", result.Warnings[0]);
        Assert.Equal(original, await File.ReadAllBytesAsync(firstPath));
    }

    [Fact]
    public async Task MissingProfileDoesNotCreateFolders()
    {
        var analyzer = new LegacySystemBiologyAnalyzer(temporaryDirectory);

        var result = await analyzer.AnalyzeAsync("F123");

        Assert.Equal(LegacySystemBiologyAnalysisResult.Empty, result);
        Assert.False(Directory.Exists(temporaryDirectory));
    }

    [Fact]
    public async Task FrontierIdCannotEscapeSystemsDirectory()
    {
        var analyzer = new LegacySystemBiologyAnalyzer(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => analyzer.AnalyzeAsync(".."));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
