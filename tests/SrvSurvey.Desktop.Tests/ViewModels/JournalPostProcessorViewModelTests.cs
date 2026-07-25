using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalPostProcessorViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-post-processor-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task AnalyzesSelectedCommanderWithoutChangingData()
    {
        var viewModel = CreateViewModel(out var dataDirectory);
        var originalProfile = await File.ReadAllBytesAsync(
            Path.Combine(dataDirectory, "F123-live.json"));
        await viewModel.RefreshCommandersAsync();
        viewModel.SetBeginningOfTime();

        await viewModel.AnalyzeAsync();

        Assert.Equal("Drew (F123)", viewModel.SelectedCommander!.DisplayName);
        Assert.Equal("1", viewModel.Statistics.Single(
            statistic => statistic.Name == "Jumps").Value);
        Assert.Equal("12", viewModel.Statistics.Single(
            statistic => statistic.Name == "Cargo bought").Value);
        Assert.Contains("Analyzed 1 matching journal", viewModel.StatusMessage);
        Assert.Equal(
            originalProfile,
            await File.ReadAllBytesAsync(
                Path.Combine(dataDirectory, "F123-live.json")));
        Assert.False(File.Exists(Path.Combine(dataDirectory, "F123-codex.json")));
    }

    [Fact]
    public async Task CodexMergeRequiresConfirmationAndPreservesProfile()
    {
        var viewModel = CreateViewModel(out var dataDirectory);
        await viewModel.RefreshCommandersAsync();
        var profilePath = Path.Combine(dataDirectory, "F123-live.json");
        var originalProfile = await File.ReadAllBytesAsync(profilePath);

        await viewModel.RebuildCodexAsync();

        Assert.False(File.Exists(Path.Combine(dataDirectory, "F123-codex.json")));
        Assert.Contains("confirm", viewModel.StatusMessage);

        viewModel.CodexRebuildConfirmed = true;
        await viewModel.RebuildCodexAsync();

        Assert.False(viewModel.CodexRebuildConfirmed);
        Assert.True(File.Exists(Path.Combine(dataDirectory, "F123-codex.json")));
        Assert.Single(Directory.GetFiles(dataDirectory, "F123-codex-*.json"));
        Assert.Contains("merged 2 earlier", viewModel.StatusMessage);
        Assert.Equal(originalProfile, await File.ReadAllBytesAsync(profilePath));
    }

    [Fact]
    public async Task AnalyzesSystemBiologyWithoutChangingCopiedFiles()
    {
        var viewModel = CreateViewModel(out var dataDirectory);
        var systemDirectory = Path.Combine(dataDirectory, "systems", "F123");
        Directory.CreateDirectory(systemDirectory);
        var systemPath = Path.Combine(systemDirectory, "Sol_42.json");
        await File.WriteAllTextAsync(
            systemPath,
            """
            {
              "future": 42,
              "bodies": [
                {
                  "atmosphereComposition": { "CarbonDioxide": 100 },
                  "organisms": [
                    { "speciesLocalized": "Aleoida Arcus" }
                  ]
                }
              ]
            }
            """);
        var original = await File.ReadAllBytesAsync(systemPath);
        await viewModel.RefreshCommandersAsync();

        await viewModel.AnalyzeSystemsAsync();

        var species = Assert.Single(viewModel.SystemSpecies);
        Assert.Equal("Aleoida Arcus", species.Name);
        Assert.Equal("1 observation(s)", species.CountText);
        Assert.Equal("CarbonDioxide x1", species.AtmosphereSummary);
        Assert.Contains("1 bodies, and 1 organisms", viewModel.SystemAnalysisSummary);
        Assert.Equal(original, await File.ReadAllBytesAsync(systemPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private JournalPostProcessorViewModel CreateViewModel(
        out string dataDirectory)
    {
        var journalDirectory = Path.Combine(temporaryDirectory, "journals");
        dataDirectory = Path.Combine(temporaryDirectory, "data");
        Directory.CreateDirectory(journalDirectory);
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(
            Path.Combine(dataDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"future\":42}");
        File.WriteAllText(
            Path.Combine(
                journalDirectory,
                "Journal.2026-07-20T120000.01.log"),
            """
            {"timestamp":"2026-07-20T12:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-07-20T12:01:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-20T12:02:00Z","event":"FSDJump","JumpDist":12.4}
            {"timestamp":"2026-07-20T12:03:00Z","event":"MarketBuy","Count":12}
            {"timestamp":"2026-07-20T12:04:00Z","event":"CodexEntry","EntryID":2310101,"SystemAddress":42,"BodyID":3}
            {"timestamp":"2026-07-20T12:05:00Z","event":"Shutdown"}

            """);
        var store = new CommanderCodexStore(dataDirectory);
        return new JournalPostProcessorViewModel(
            new CommanderProfileCatalog(dataDirectory),
            new JournalHistoryAnalyzer(journalDirectory),
            new LegacySystemBiologyAnalyzer(dataDirectory),
            new CommanderCodexJournalImporter(journalDirectory, store));
    }
}
