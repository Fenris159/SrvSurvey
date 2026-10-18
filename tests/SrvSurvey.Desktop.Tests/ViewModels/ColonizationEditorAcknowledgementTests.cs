using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationSystemEditorViewModelTests
{
    [Theory]
    [InlineData("Renamed Site ", null)]
    [InlineData("Renamed Site", "")]
    public async Task NormalizedFieldsDoNotPreventReviewOrPublication(string name, string? buildId)
    {
        ColonizationSystemRecord original = System();
        var client = new StubClient
        {
            Current = original with { Sites = [original.Sites[0] with { BuildId = buildId }] },
        };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].Name = name;

        await editor.ReviewAsync();

        Assert.True(editor.IsPublishConfirmationPending);
        await editor.ConfirmPublishAsync();
        Assert.Equal(1, client.UpdateCount);
        ColonizationSystemSite published = Assert.Single(client.LastUpdate!.UpdatedSites);
        Assert.Equal("Renamed Site", published.Name);
        Assert.Null(published.BuildId);
        Assert.False(editor.HasLocalChanges);
    }

    [Fact]
    public async Task AlreadyAppliedReviewRetainsEditsMadeDuringTheRequest()
    {
        var client = new StubClient { Current = System() };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].Name = "Accepted Rename";
        client.Current = client.Current with
        {
            Revision = 2,
            Sites = [client.Current.Sites[0] with { Name = "Accepted Rename" }],
        };
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.PendingRead = response.Task;
        Task review = editor.ReviewAsync();
        editor.Sites[0].BodyNumber = 2;
        response.SetResult(client.Current);
        await review.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, editor.Sites[0].BodyNumber);
        Assert.Equal("Accepted Rename", editor.Sites[0].Name);
        Assert.True(editor.HasLocalChanges);
        Assert.False(editor.IsPublishConfirmationPending);
    }

    [Fact]
    public async Task AlreadyAppliedReviewDoesNotReplayHistoricalDocking()
    {
        var client = new StubClient { Current = System() };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        const string json = """
            {"timestamp":"2026-10-18T05:00:00Z","event":"Docked","SystemAddress":42,"StarSystem":"Test System","StationName":"Orbital One","MarketID":500,"StationType":"Coriolis","BodyID":1}
            """;
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? docked, out string? error), error);
        editor.ApplyJournalEvents([docked!]);
        editor.Sites[0].Status = ColonizationSystemSiteStatus.Demolish;
        client.Current = editor.PlanningSnapshot! with { Revision = 2 };
        await editor.ReviewAsync();
        Assert.Equal(ColonizationSystemSiteStatus.Demolish, editor.Sites[0].Status);
        Assert.False(editor.HasLocalChanges);
        Assert.Equal(2, editor.PlanningSnapshot.Revision);
        Assert.False(editor.IsPublishConfirmationPending);
    }
}

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData("remove")]
    [InlineData("add")]
    [InlineData("journal")]
    public async Task FirstUnsavedMutationNeverReplacesTheAcceptedRevision(string mutation)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationSystemRecord accepted = client.PlanningSystems!["Sol"] with { Revision = 2 };
        client.PlanningSystems["Sol"] = accepted;
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            model.UpdateSystemContext("Sol", null, 42);
            await model.CurrentSystemPlanningTask;
            await model.SystemEditor.LoadAsync();
            if (mutation == "remove")
            {
                model.SystemEditor.SelectedSite = model.SystemEditor.Sites[0];
                model.SystemEditor.RemoveSelectedSite();
            }
            else if (mutation == "add")
            {
                model.SystemEditor.NewSiteName = "Unpublished Plan";
                model.SystemEditor.AddSite();
            }
            else
            {
                model.SystemEditor.ApplyJournalEvents([
                    Event(
                        "Docked",
                        "\"SystemAddress\":42,\"StarSystem\":\"Sol\",\"StationName\":\"Test Project\",\"MarketID\":500,\"StationType\":\"StationAsteroid\",\"BodyID\":1"
                    ),
                ]);
            }
            Assert.True(model.SystemEditor.HasLocalChanges);
            model.UpdateSystemContext("Achenar", null, 43);
            await model.CurrentSystemPlanningTask;
            client.PlanningSystems["Sol"] = accepted with
            {
                Revision = 1,
                Sites = [accepted.Sites[0] with { Name = "Older Plan" }],
            };
            await model.RefreshAsync();
            Assert.Equal("Test Project (Plan)", Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName);
        }
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        ColonizationSystemRecord saved = Assert.Single(store.LoadPlannedSystems("Test Cmdr"));
        Assert.Equal(2, saved.Revision);
        Assert.Equal("Test Project", Assert.Single(saved.Sites).Name);
    }

    [Theory]
    [InlineData("null", "active")]
    [InlineData("null", "travel")]
    [InlineData("null", "commander")]
    [InlineData("[null]", "active")]
    [InlineData("[null]", "travel")]
    [InlineData("[null]", "commander")]
    public async Task MalformedSiteCollectionsCannotEscapePublication(string sites, string transition)
    {
        (
            ColonizationViewModel model,
            StubRavenColonialClient _,
            ColonizationSystemRecord _,
            TaskCompletionSource<ColonizationSystemRecord> response,
            Task publish
        ) = await StartDelayedPlanningPublicationAsync(transition == "travel");
        using (model)
        {
            int notifications = 0;
            model.SystemEditor.SystemPublished += (_, _) => notifications++;
            if (transition == "commander")
            {
                await model.SetCommanderAsync("Other Cmdr");
            }
            ColonizationSystemRecord malformed = JsonSerializer.Deserialize<ColonizationSystemRecord>(
                "{\"id64\":42,\"name\":\"Sol\",\"sites\":" + sites + "}"
            )!;
            response.SetResult(malformed);
            await publish.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, notifications);
            Assert.False(model.SystemEditor.IsBusy);
            if (transition == "commander")
            {
                Assert.Equal("Other Cmdr", model.CommanderName);
            }
            else
            {
                model.UpdateSystemContext("Sol", null, 42);
                await model.CurrentSystemPlanningTask;
                Assert.Contains(model.Projects, row => row.BuildName == "Local Draft (Plan)");
            }
        }
    }
}
