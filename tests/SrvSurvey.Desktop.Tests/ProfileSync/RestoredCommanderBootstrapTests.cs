using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class RestoredCommanderBootstrapTests
{
    [Theory]
    [InlineData(false, 0, 0, "1", "20.0 ly")]
    [InlineData(true, 10, 100, "10", "100.0 ly")]
    [InlineData(true, 0, 0, "0", "0.0 ly")]
    public async Task RestoredPreferencesRecoverMissingTotalsWithoutReplayingExistingTotals(
        bool hasLocalTotals,
        int localJumps,
        double localDistance,
        string expectedJumps,
        string expectedDistance
    )
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        source.Write(
            "data",
            "F123-live.json",
            """{"fid":"F123","commander":"Backup Pilot","countJumps":900,"autoResetExplorationValue":true}"""
        );
        if (hasLocalTotals)
        {
            await new CommanderProfileStore(target.Paths.DataDirectory).SaveExplorationAsync(
                "F123",
                "Backup Pilot",
                true,
                new ExplorationSnapshot(0, localDistance, localJumps, 0, 0, 0)
            );
        }
        target.Store.Apply(source.Store.Capture());
        string journals = target.Path("config", "journals");
        Directory.CreateDirectory(journals);
        var bootstrap = new JournalMonitorUpdate(
            Path.Combine(journals, "Journal.2026-11-10T120000.01.log"),
            [
                Entry("""{"timestamp":"2026-11-10T12:00:00Z","event":"Fileheader","Odyssey":true}"""),
                Entry(
                    """{"timestamp":"2026-11-10T12:00:01Z","event":"Commander","Name":"Backup Pilot","FID":"F123"}"""
                ),
                Entry(
                    """{"timestamp":"2026-11-10T12:00:02Z","event":"LoadGame","Commander":"Backup Pilot","FID":"F123","Odyssey":true,"Ship":"Python","ShipID":7}"""
                ),
                Entry("""{"timestamp":"2026-11-10T12:00:03Z","event":"StartJump","JumpType":"Hyperspace"}"""),
                Entry(
                    """{"timestamp":"2026-11-10T12:00:04Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":10477373803,"StarPos":[0,0,0],"JumpDist":20}"""
                ),
            ],
            null,
            null,
            null,
            null,
            [],
            IsBootstrapRead: true
        );
        for (int start = 0; start < 2; start++)
        {
            await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
                journals,
                builder => builder.WithAppDataPaths(target.Paths)
            );
            await model.ApplyMonitorUpdateAsync(bootstrap, true);
            Assert.Equal(expectedJumps, model.ExplorationJumps);
            Assert.Equal(expectedDistance, model.ExplorationDistance);
        }
        JsonNode saved = JsonNode.Parse(await File.ReadAllTextAsync(target.Path("data", "F123-live.json")))!;
        Assert.True(saved["autoResetExplorationValue"]!.GetValue<bool>());
    }

    private static JournalEventEnvelope Entry(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }
}
