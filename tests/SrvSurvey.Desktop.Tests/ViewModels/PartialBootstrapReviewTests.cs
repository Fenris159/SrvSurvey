using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class PartialBootstrapReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialStartupRetainsProfileTotalsAndStatusBeforeAcceptingLiveProgress(bool existingProfile)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-partial-bootstrap-{Guid.NewGuid():N}");
        string journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        var store = new CommanderProfileStore(paths.DataDirectory);
        if (existingProfile)
        {
            await store.SaveExplorationAsync("F123", "Test", true, new ExplorationSnapshot(0, 100, 10, 0, 0, 0));
        }
        try
        {
            await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
                journals,
                builder => builder.WithAppDataPaths(paths)
            );
            var first = new JournalMonitorUpdate(
                Path.Combine(journals, "Journal.2026-11-04T050000.01.log"),
                [
                    Entry("{\"timestamp\":\"2026-11-04T05:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}"),
                    Entry(
                        "{\"timestamp\":\"2026-11-04T05:00:01Z\",\"event\":\"Commander\",\"Name\":\"Test\",\"FID\":\"F123\"}"
                    ),
                ],
                new EliteStatus { GuiFocus = GuiFocus.GalaxyMap },
                null,
                null,
                null,
                ["Continuation temporarily unavailable"],
                IsBootstrapRead: true
            );
            await model.ApplyMonitorUpdateAsync(first, true);
            Assert.Equal("GalaxyMap", model.CurrentVrOverlayMode);
            JournalEventEnvelope[] jumps =
            [
                Entry("{\"timestamp\":\"2026-11-04T05:01:00Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}"),
                Entry(
                    "{\"timestamp\":\"2026-11-04T05:01:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":42,\"StarPos\":[0,0,0],\"JumpDist\":20}"
                ),
            ];
            await model.ApplyMonitorUpdateAsync(first with { JournalEvents = jumps, Status = null, Errors = [] }, true);
            Assert.Equal(existingProfile ? "10" : "1", model.ExplorationJumps);
            Assert.Equal(existingProfile ? "100.0 ly" : "20.0 ly", model.ExplorationDistance);
            Assert.Equal("GalaxyMap", model.CurrentVrOverlayMode);
            JournalMonitorUpdate live = first with
            {
                IsBootstrapRead = false,
                Status = null,
                Errors = [],
                JournalEvents =
                [
                    Entry(
                        "{\"timestamp\":\"2026-11-04T05:02:00Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}"
                    ),
                    Entry(
                        "{\"timestamp\":\"2026-11-04T05:02:01Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Lave\",\"SystemAddress\":43,\"StarPos\":[10,0,0],\"JumpDist\":10}"
                    ),
                ],
            };
            await model.ApplyMonitorUpdateAsync(live, true);
            CommanderProfileLoadResult stored = await store.LoadAsync("F123", true);
            Assert.Equal(existingProfile ? 11 : 2, stored.Data!.Exploration.JumpCount);
            Assert.Equal(existingProfile ? 110 : 30, stored.Data.Exploration.DistanceTravelled);
            string changedSource = Path.Combine(root, "another-journal-source");
            Directory.CreateDirectory(changedSource);
            await model.ApplyMonitorUpdateAsync(
                first with
                {
                    JournalPath = Path.Combine(changedSource, "Journal.2026-11-04T050000.01.log"),
                    JournalEvents = [],
                    Status = null,
                    Errors = [],
                },
                true
            );
            Assert.Null(model.CurrentVrOverlayMode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static JournalEventEnvelope Entry(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }
}
