using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    public async Task PublicationAndRefreshKeepTheNewestAuthoritativeRevision(
        bool refreshNewer,
        bool publishFirst,
        bool deleted,
        bool travel
    )
    {
        (
            ColonizationViewModel model,
            StubRavenColonialClient client,
            ColonizationSystemRecord server,
            TaskCompletionSource<ColonizationSystemRecord> response,
            Task publish
        ) = await StartDelayedPlanningPublicationAsync(travel);
        using (model)
        {
            client.Workspace = client.Workspace with
            {
                Projects = client
                    .Workspace.Projects.Append(Project("remote-live", "Remote", 100) with { SystemName = "Achenar" })
                    .ToArray(),
            };
            ColonizationSystemRecord published = server with
            {
                Revision = 2,
                Sites = [server.Sites[0] with { Name = "Local Draft" }],
            };
            ColonizationSystemRecord refreshed = server with
            {
                Revision = refreshNewer ? 3 : 1,
                Sites = deleted ? [] : [server.Sites[0] with { Name = "Refreshed Plan" }],
            };
            var waiting = new TaskCompletionSource();
            var finish = new TaskCompletionSource<ColonizationSystemRecord>();
            client.LoadPlanningSystem = (name, _) =>
            {
                if (name == "Achenar")
                {
                    waiting.TrySetResult();
                    return finish.Task;
                }
                return Task.FromResult(refreshed);
            };
            Task refresh = model.RefreshAsync();
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (publishFirst)
            {
                response.SetResult(published);
                await publish.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finish.SetResult(client.PlanningSystems!["Achenar"]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            if (!publishFirst)
            {
                response.SetResult(published);
                await publish.WaitAsync(TimeSpan.FromSeconds(5));
            }
            if (deleted)
            {
                Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
            }
            else
            {
                Assert.Equal(
                    (refreshNewer ? "Refreshed Plan" : "Local Draft") + " (Plan)",
                    Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName
                );
            }
        }
    }

    [Theory]
    [InlineData("id", false)]
    [InlineData("id", true)]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("identity", false)]
    [InlineData("identity", true)]
    public async Task InvalidPublicationNeverNotifiesOrPoisonsPlanningCache(string invalid, bool travel)
    {
        (
            ColonizationViewModel model,
            StubRavenColonialClient _,
            ColonizationSystemRecord server,
            TaskCompletionSource<ColonizationSystemRecord> response,
            Task publish
        ) = await StartDelayedPlanningPublicationAsync(travel);
        using (model)
        {
            int notifications = 0;
            model.SystemEditor.SystemPublished += (_, _) => notifications++;
            ColonizationSystemSite first = server.Sites[0] with { Name = "Invalid First" };
            ColonizationSystemRecord malformed = server with
            {
                Revision = 2,
                SystemAddress = invalid == "identity" ? 0 : server.SystemAddress,
                Sites =
                [
                    first,
                    first with
                    {
                        Id = invalid == "id" ? first.Id : "different",
                        Name = invalid == "name" ? first.Name : "Invalid Second",
                    },
                ],
            };
            response.SetResult(malformed);
            await publish.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, notifications);
            model.UpdateSystemContext("Sol", null, 42);
            await model.CurrentSystemPlanningTask;
            Assert.Equal("Local Draft (Plan)", Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName);
        }
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.DoesNotContain(
            store.LoadPlannedSystems("Test Cmdr").SelectMany(system => system.Sites),
            site => site.Name.StartsWith("Invalid", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ReturningToCommanderRejectsPublicationFromItsPreviousSession()
    {
        (
            ColonizationViewModel model,
            StubRavenColonialClient client,
            ColonizationSystemRecord server,
            TaskCompletionSource<ColonizationSystemRecord> response,
            Task publish
        ) = await StartDelayedPlanningPublicationAsync();
        using (model)
        {
            client.PlanningSystems!["Sol"] = server with
            {
                Revision = 3,
                Sites = [server.Sites[0] with { Name = "New Session Plan" }],
            };
            await model.SetCommanderAsync("Other Cmdr");
            await model.SetCommanderAsync("Test Cmdr");
            Assert.Contains(model.Projects, row => row.BuildName == "New Session Plan (Plan)");
            response.SetResult(
                server with
                {
                    Revision = 10,
                    Sites = [server.Sites[0] with { Name = "Obsolete Session Plan" }],
                }
            );
            await publish.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(
                "New Session Plan (Plan)",
                Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName
            );
        }
    }

    [Fact]
    public async Task PublicationRetiresSubmittedEditFromAnOlderFullRefresh()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        var workspace = new TaskCompletionSource<ColonizationCommanderProjects>();
        client.LoadWorkspace = _ => workspace.Task;
        Task refresh = model.RefreshAsync();
        model.SystemEditor.Sites[0].Name = "Submitted Draft";
        await model.SystemEditor.ReviewAsync();
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.SystemPublishTask = response.Task;
        Task publish = model.SystemEditor.ConfirmPublishAsync();
        model.UpdateSystemContext("Achenar", null, 43);
        ColonizationSystemRecord server = client.PlanningSystems!["Sol"];
        ColonizationSystemRecord newest = server with
        {
            Revision = 3,
            Sites = [server.Sites[0] with { Name = "Newest Server Plan" }],
        };
        client.PlanningSystems["Sol"] = newest;
        var waiting = new TaskCompletionSource();
        var finish = new TaskCompletionSource<ColonizationSystemRecord>();
        client.LoadPlanningSystem = (name, _) =>
        {
            if (name == "Achenar")
            {
                waiting.TrySetResult();
                return finish.Task;
            }
            return Task.FromResult(newest);
        };
        workspace.SetResult(client.Workspace);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        response.SetResult(server with { Revision = 2, Sites = [server.Sites[0] with { Name = "Submitted Draft" }] });
        await publish.WaitAsync(TimeSpan.FromSeconds(5));
        finish.SetResult(client.PlanningSystems["Achenar"]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            "Newest Server Plan (Plan)",
            Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName
        );
    }

    [Fact]
    public async Task DiscoveryFallbackCannotResurrectAPublishedDeletion()
    {
        (
            ColonizationViewModel model,
            StubRavenColonialClient client,
            ColonizationSystemRecord server,
            TaskCompletionSource<ColonizationSystemRecord> response,
            Task publish
        ) = await StartDelayedPlanningPublicationAsync();
        using (model)
        {
            client.DiscoveredPlanningSystems = [server with { Revision = 1 }];
            var systemEntered = new TaskCompletionSource();
            var systemResponse = new TaskCompletionSource<ColonizationSystemRecord>();
            var waiting = new TaskCompletionSource();
            var finish = new TaskCompletionSource<ColonizationSystemRecord>();
            client.LoadPlanningSystem = (name, _) =>
            {
                if (name == "Sol")
                {
                    systemEntered.TrySetResult();
                    return systemResponse.Task;
                }
                waiting.TrySetResult();
                return finish.Task;
            };
            Task refresh = model.RefreshAsync();
            await systemEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            response.SetResult(server with { Revision = 2, Sites = [] });
            await publish.WaitAsync(TimeSpan.FromSeconds(5));
            systemResponse.SetException(new HttpRequestException("offline system"));
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            finish.SetResult(client.PlanningSystems!["Achenar"]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
        }
    }

    [Fact]
    public async Task AlreadyAppliedReviewKeepsTheAuthoritativeRevision()
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Already Applied";
        ColonizationSystemRecord server = client.PlanningSystems!["Sol"];
        client.PlanningSystems["Sol"] = server with
        {
            Revision = 3,
            Sites = [server.Sites[0] with { Name = "Already Applied" }],
        };
        await model.SystemEditor.ReviewAsync();
        Assert.False(model.SystemEditor.HasLocalChanges);
        Assert.Equal(3, model.SystemEditor.PlanningSnapshot!.Revision);
    }

    private async Task<(
        ColonizationViewModel Model,
        StubRavenColonialClient Client,
        ColonizationSystemRecord Server,
        TaskCompletionSource<ColonizationSystemRecord> Response,
        Task Publish
    )> StartDelayedPlanningPublicationAsync(bool travel = true)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Local Draft";
        await model.SystemEditor.ReviewAsync();
        Assert.True(model.SystemEditor.CanConfirmPublish);
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.SystemPublishTask = response.Task;
        Task publish = model.SystemEditor.ConfirmPublishAsync();
        Assert.Single(client.SystemUpdates);
        if (travel)
        {
            model.UpdateSystemContext("Achenar", null, 43);
            await model.CurrentSystemPlanningTask;
        }
        return (model, client, client.PlanningSystems!["Sol"], response, publish);
    }
}
