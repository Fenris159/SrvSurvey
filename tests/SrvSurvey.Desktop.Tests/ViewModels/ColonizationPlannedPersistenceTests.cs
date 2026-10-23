using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task PlanningEditsUpdateNeedsImmediatelyButPersistOnlyAfterEditingSettles()
    {
        var clock = new PlanningSaveClock();
        using ColonizationViewModel model = Create(ReviewPlanningClient(), clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        string path = Path.Combine(directory, "ui.json.plans.json");
        string before = await File.ReadAllTextAsync(path);
        for (int index = 0; index < 20; index++)
        {
            model.SystemEditor.Sites[0].Name = "Port " + index;
        }
        Assert.Equal("Port 19 (Plan)", Assert.Single(model.Projects, row => row.Project.IsPlanned).BuildName);
        Assert.Equal(before, await File.ReadAllTextAsync(path));
        clock.ReleaseAll();
        await model.PlannedSystemsSaveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal("Port 19", Assert.Single(store.LoadPlannedSystems("Test Cmdr")).Sites[0].Name);
    }

    [Theory]
    [InlineData("commander")]
    [InlineData("consent")]
    [InlineData("dispose")]
    public async Task PlanningEditsFlushBeforeSessionChangesAndRejectLateSaves(string change)
    {
        var clock = new PlanningSaveClock();
        using ColonizationViewModel model = Create(ReviewPlanningClient(), clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Retained local edit";
        Task pending = model.PlannedSystemsSaveTask;
        switch (change)
        {
            case "commander":
                await model.SetCommanderAsync("Other Cmdr");
                break;
            case "consent":
                model.IsEnabled = false;
                break;
            case "dispose":
                model.Dispose();
                break;
        }
        string path = Path.Combine(directory, "ui.json.plans.json");
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal("Retained local edit", Assert.Single(store.LoadPlannedSystems("Test Cmdr")).Sites[0].Name);
        string flushed = await File.ReadAllTextAsync(path);
        clock.ReleaseAll();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(flushed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PlanningSaveFailureIsReportedAndLaterEditsCanRetry()
    {
        var clock = new PlanningSaveClock();
        using ColonizationViewModel model = Create(ReviewPlanningClient(), clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "First edit";
        string path = Path.Combine(directory, "ui.json.plans.json");
        File.Delete(path);
        Directory.CreateDirectory(path);
        clock.ReleaseAll();
        await model.PlannedSystemsSaveTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("planned site requirements could not be saved", model.StatusMessage);
        Directory.Delete(path);
        model.SystemEditor.Sites[0].Name = "Retried edit";
        clock.ReleaseAll();
        await model.PlannedSystemsSaveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal("Retried edit", Assert.Single(store.LoadPlannedSystems("Test Cmdr")).Sites[0].Name);
    }

    [Fact]
    public async Task ProjectRefreshImmediatelySavesPendingLocalEdits()
    {
        var clock = new PlanningSaveClock();
        using ColonizationViewModel model = Create(ReviewPlanningClient(), clock.DelayAsync);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Newest local edit";
        Task pending = model.PlannedSystemsSaveTask;
        await model.RefreshAsync();
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal("Newest local edit", Assert.Single(store.LoadPlannedSystems("Test Cmdr")).Sites[0].Name);
        clock.ReleaseAll();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class PlanningSaveClock
    {
        private readonly List<TaskCompletionSource> pending = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken token)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(500), duration);
            Assert.True(token.CanBeCanceled);
            var completion = new TaskCompletionSource();
            pending.Add(completion);
            // An uncooperative delay must not publish a superseded commander or edit.
            return completion.Task;
        }

        public void ReleaseAll()
        {
            foreach (TaskCompletionSource completion in pending)
            {
                completion.TrySetResult();
            }
        }
    }
}
