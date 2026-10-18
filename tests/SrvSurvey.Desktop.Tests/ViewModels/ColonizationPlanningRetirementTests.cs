using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData("delete", false)]
    [InlineData("status", false)]
    [InlineData("building", false)]
    [InlineData("delete", true)]
    public async Task MissingPlannedFocusReturnsToRavenFocusAndPersists(string change, bool explicitlyCleared)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
            if (explicitlyCleared)
            {
                await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
            }
            ColonizationSystemRecord system = client.PlanningSystems!["Sol"];
            if (change == "building")
            {
                client.Workspace = client.Workspace with
                {
                    Projects = client
                        .Workspace.Projects.Append(Project("started", "Test Project", 20) with { SystemName = "Sol" })
                        .ToArray(),
                };
            }
            else
            {
                client.PlanningSystems["Sol"] = system with
                {
                    Sites =
                        change == "delete"
                            ? []
                            : [system.Sites[0] with { Status = ColonizationSystemSiteStatus.Build }],
                };
            }
            await model.RefreshAsync();
            Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
            Assert.Equal(!explicitlyCleared, model.Projects.Single(row => row.Project.BuildId == "active").IsFocused);
        }
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal(explicitlyCleared ? string.Empty : null, store.LoadLocalProjectFocus("Test Cmdr"));
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.Equal(!explicitlyCleared, reopened.Projects.Single(row => row.Project.BuildId == "active").IsFocused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullRefreshRetiresDetachedDeletionWithoutLinkedBuildOrDiscovery(bool failedDiscovery)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        client.Workspace = new([], [], null, []);
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        if (failedDiscovery)
        {
            model.SetCommanderProfile("F123", true, "test-key");
            client.PlanningDiscoveryFailure = new HttpRequestException("offline discovery");
        }
        model.UpdateSystemContext("Sol", null, 42);
        await model.SetCommanderAsync("Test Cmdr");
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.SelectedSite = model.SystemEditor.Sites[0];
        model.SystemEditor.RemoveSiteCommand.Execute(null);
        Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
        model.UpdateSystemContext("Achenar", null, 43);
        await model.CurrentSystemPlanningTask;
        await model.RefreshAsync();
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        Assert.Contains(model.Projects, row => row.BuildName == "Test Project (Plan)");
    }

    [Theory]
    [InlineData("away")]
    [InlineData("return")]
    [InlineData("failure")]
    [InlineData("commander")]
    [InlineData("pending-travel")]
    public async Task PublicationReconcilesDetachedDraftWithoutChangingAnotherCommander(string transition)
    {
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Published Local Plan";
        ColonizationSystemRecord server = client.PlanningSystems!["Sol"];
        ColonizationSystemSite remote = server.Sites[0] with { Id = "remote", Name = "Remote Only Plan" };
        client.PlanningSystems["Sol"] = server with { Sites = [server.Sites[0], remote] };
        await model.SystemEditor.ReviewAsync();
        Assert.True(model.SystemEditor.CanConfirmPublish);
        var response = new TaskCompletionSource<ColonizationSystemRecord>();
        client.SystemPublishTask = response.Task;
        Task publish = model.SystemEditor.ConfirmPublishAsync();
        Assert.Single(client.SystemUpdates);
        var destination = new TaskCompletionSource<ColonizationSystemRecord>();
        Func<string, CancellationToken, Task<ColonizationSystemRecord>> read = client.LoadPlanningSystem!;
        client.LoadPlanningSystem = (name, token) =>
            transition == "pending-travel" && name == "Achenar" ? destination.Task : read(name, token);
        model.UpdateSystemContext("Achenar", null, 43);
        if (transition != "pending-travel")
        {
            await model.CurrentSystemPlanningTask;
        }
        if (transition == "return")
        {
            model.UpdateSystemContext("Sol", null, 42);
            await model.CurrentSystemPlanningTask;
        }
        if (transition == "commander")
        {
            client.PlanningSystems["Sol"] = server with
            {
                Sites = [server.Sites[0] with { Name = "Other Commander Plan" }],
            };
            await model.SetCommanderAsync("Other Cmdr");
        }
        ColonizationSystemRecord published = server with
        {
            Sites = [server.Sites[0] with { Name = "Published Local Plan" }, remote],
        };
        if (transition == "failure")
        {
            response.SetException(new HttpRequestException("publish unavailable"));
        }
        else
        {
            if (transition != "commander")
            {
                client.PlanningSystems["Sol"] = published;
            }
            response.SetResult(published);
        }
        await publish.WaitAsync(TimeSpan.FromSeconds(5));
        if (transition == "pending-travel")
        {
            destination.SetResult(FilterPlanningSystem("Achenar", 43));
            await model.CurrentSystemPlanningTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(model.Projects, row => row.Project.IsPlanned && row.Project.SystemAddress == 43);
        }
        model.UpdateSystemContext("Sol", null, 42);
        await model.CurrentSystemPlanningTask;
        if (transition == "commander")
        {
            Assert.Contains(model.Projects, row => row.BuildName == "Other Commander Plan (Plan)");
            Assert.DoesNotContain(model.Projects, row => row.BuildName == "Published Local Plan (Plan)");
        }
        else
        {
            Assert.Contains(model.Projects, row => row.BuildName == "Published Local Plan (Plan)");
            Assert.Equal(
                transition != "failure",
                model.Projects.Any(row => row.BuildName == "Remote Only Plan (Plan)")
            );
        }
    }
}
