using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationNexusSystemsTests
{
    [Fact]
    public async Task RefreshReloadsAssignmentsAndCargoWhileKeepingTheSelectedSystem()
    {
        ColonizationNexus initial = NexusFixture.Snapshot();
        ColonizationNexus snapshot = initial with { Systems = [initial.Systems[0] with { FleetCarrierIds = [] }] };
        var api = new ColonizationNexusViewModelTests.NexusReader
        {
            Read = (_, _) => Task.FromResult<ColonizationNexus?>(snapshot),
        };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        Assert.Equal(30, Assert.Single(model.Detail!.SelectedScope.Needs).Ready);
        model.Detail.SelectedScope = model.Detail.Systems[0];
        Assert.Equal(30, Assert.Single(model.Detail.SelectedScope.Needs).Ready);
        snapshot = snapshot with
        {
            Systems = [initial.Systems[0]],
            FleetCarriers = [initial.FleetCarriers[0] with { Cargo = new() { ["steel"] = 70 } }],
        };
        model.RefreshCommand.Execute(null);
        await model.ActiveTask;
        Assert.Equal(1, model.Detail.SelectedScope.SystemAddress);
        ColonizationNexusNeedRow need = Assert.Single(model.Detail.SelectedScope.Needs);
        Assert.Equal(70, need.Ready);
        Assert.Equal(30, need.Missing);
        Assert.Single(model.Detail.Carriers[0].SystemLinks);
        model.Detail.SelectedScope = model.Detail.Scopes[0];
        Assert.Equal(70, Assert.Single(model.Detail.SelectedScope.Needs).Ready);
        model.Detail.SelectedScope = model.Detail.Systems[0];
        Assert.Equal(70, Assert.Single(model.Detail.SelectedScope.Needs).Ready);
        Assert.Equal(2, api.Requests.Count(request => request.StartsWith("index:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RemovalRequiresConfirmationPreservesNewSystemsAndNeverDeletesBuildProjects()
    {
        ColonizationNexus original = NexusFixture.Snapshot();
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = original };
        using ColonizationNexusViewModel model = Create(api);
        await model.RefreshAsync();
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(0, api.SystemChanges);
        model.RemoveSystemCommand.Execute(model.Detail!.Systems[0]);
        Assert.True(model.IsSystemRemovalVisible);
        Assert.Contains("Test System", model.SystemRemovalWarning);
        model.CancelSystemRemovalCommand.Execute(null);
        Assert.False(model.IsSystemRemovalVisible);
        Assert.Equal(0, api.SystemChanges);
        model.RemoveSystemCommand.Execute(model.Detail.Systems[0]);
        api.Existing = original with
        {
            Systems = [.. original.Systems, new() { SystemAddress = 4, Name = "New system" }],
        };
        model.ConfirmSystemRemovalCommand.Execute(null);
        await model.ActiveTask;
        Assert.Equal(["Complete System", "Unplanned System", "New system"], api.SavedSystems);
        Assert.Equal(1, api.SystemChanges);
        Assert.Equal(0, api.Configures);
        Assert.Equal(0, api.Deletes);
        Assert.Equal(3, model.Detail.Systems.Count);
        Assert.False(model.IsSystemRemovalVisible);
        Assert.Equal("Test", api.Commander);
        Assert.Equal("key", api.Key);
    }

    [Fact]
    public async Task FinalSystemCannotBeRemovedFromOverviewOrFromAFresherServerSnapshot()
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = source };
        using ColonizationNexusViewModel model = Create(api);
        await model.RefreshAsync();
        ColonizationNexusScope target = model.Detail!.Systems[0];
        model.RequestSystemRemoval(target);
        api.Existing = source with { Systems = [source.Systems[0]] };
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(0, api.SystemChanges);
        Assert.Single(model.Detail.Systems);
        Assert.True(model.IsLastSystem);
        Assert.False(model.CanRemoveSystems);
        Assert.Contains("Delete Nexus", model.StatusMessage);
        Assert.Contains("Delete Nexus", model.SystemRemovalHint);
        model.RequestSystemRemoval(model.Detail.Systems[0]);
        Assert.False(model.IsSystemRemovalVisible);
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(0, api.SystemChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveKeepsTheSnapshotAndProfileChangesDiscardPendingConfirmation(bool invalidData)
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer
        {
            Existing = NexusFixture.Snapshot(),
            SystemsFailure = invalidData ? new InvalidDataException("secret") : new IOException("secret"),
        };
        using ColonizationNexusViewModel model = Create(api);
        await model.RefreshAsync();
        model.RequestSystemRemoval(model.Detail!.Systems[0]);
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(3, model.Detail.Systems.Count);
        Assert.Contains("could not be confirmed", model.StatusMessage);
        Assert.DoesNotContain("secret", model.StatusMessage);
        Assert.False(model.IsBusy);
        model.RequestSystemRemoval(model.Detail.Systems[0]);
        model.SetContext(true, "Other", "other-key");
        Assert.False(model.IsSystemRemovalVisible);
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(1, api.SystemChanges);
    }

    [Fact]
    public async Task InvalidFreshSnapshotDuringRemovalReportsFailureWithoutWriting()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = NexusFixture.Snapshot() };
        using ColonizationNexusViewModel model = Create(api);
        await model.RefreshAsync();
        model.RequestSystemRemoval(model.Detail!.Systems[0]);
        api.PendingNexusRead = Task.FromException<ColonizationNexus?>(new InvalidDataException("secret"));
        await model.ConfirmSystemRemovalAsync();
        Assert.Contains("could not be confirmed", model.StatusMessage);
        Assert.False(model.IsBusy);
        Assert.Equal(0, api.SystemChanges);
        Assert.Equal(3, model.Detail.Systems.Count);
    }

    [Fact]
    public async Task EditorsKeepTheirLastRowAndRefuseEvenAnEmptyListInsertedProgrammatically()
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        var api = new ColonizationNexusEditorViewModelTests.Writer
        {
            Existing = source with { Systems = [source.Systems[0]] },
        };
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Test",
            "key",
            () => true,
            _ => Task.CompletedTask,
            [],
            api.Existing
        );
        Assert.False(model.Systems.Rows[0].RemoveCommand.CanExecute(null));
        model.Systems.Rows[0].RemoveCommand.Execute(null);
        model.Systems.Remove(model.Systems.Rows[0]);
        Assert.Single(model.Systems.Rows);
        model.Systems.Draft = "Beta";
        model.Systems.CommitCommand.Execute(null);
        Assert.True(model.Systems.Rows[0].RemoveCommand.CanExecute(null));
        model.Systems.Rows[0].RemoveCommand.Execute(null);
        Assert.Equal("Beta", Assert.Single(model.Systems.Rows).Name);
        Assert.False(model.Systems.Rows[0].RemoveCommand.CanExecute(null));
        model.Systems.Rows.Clear();
        Assert.False(model.CanSubmit);
        await model.SubmitAsync();
        Assert.Contains("at least one", model.Status);
        Assert.Equal(0, api.Configures);
        Assert.Equal(0, api.Renames);
    }

    [Fact]
    public async Task MissingOrAlreadyRemovedSystemDoesNotPublishAnotherUpdate()
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = source };
        using ColonizationNexusViewModel model = Create(api);
        await model.RefreshAsync();
        model.RequestSystemRemoval(model.Detail!.Systems[0]);
        api.Existing = source with { Systems = source.Systems.Skip(1).ToArray() };
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(0, api.SystemChanges);
        Assert.Equal(2, model.Detail.Systems.Count);
        model.RequestSystemRemoval(model.Detail.Systems[0]);
        api.PendingNexusRead = Task.FromResult<ColonizationNexus?>(null);
        await model.ConfirmSystemRemovalAsync();
        Assert.Equal(0, api.SystemChanges);
        Assert.Contains("no longer available", model.StatusMessage);
        Assert.Equal(2, model.Detail.Systems.Count);
    }

    [Fact]
    public async Task ChangingCommanderWhileReadingLatestSystemsPreventsTheWrite()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = NexusFixture.Snapshot() };
        using ColonizationNexusViewModel model = Create(api);
        await model.RefreshAsync();
        model.RequestSystemRemoval(model.Detail!.Systems[0]);
        var pending = new TaskCompletionSource<ColonizationNexus?>();
        api.PendingNexusRead = pending.Task;
        Task saving = model.ConfirmSystemRemovalAsync();
        Assert.True(model.IsBusy);
        await model.ConfirmSystemRemovalAsync();
        model.SetContext(true, "Other", "other-key");
        pending.SetResult(NexusFixture.Snapshot());
        await saving;
        Assert.Equal(0, api.SystemChanges);
        Assert.Null(model.Detail);
        Assert.False(model.IsBusy);
        Assert.False(model.IsSystemRemovalVisible);
    }

    [Fact]
    public async Task CreationRequiresASystemAndNonmembersCannotRemoveExistingSystems()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = NexusFixture.Snapshot() };
        using var editor = new ColonizationNexusEditorViewModel(
            api,
            "Test",
            "key",
            () => true,
            _ => Task.CompletedTask,
            []
        );
        editor.Name = "New Nexus";
        Assert.False(editor.CanSubmit);
        await editor.SubmitAsync();
        Assert.Equal(0, api.Creates);
        Assert.Contains("at least one", editor.Status);
        using ColonizationNexusViewModel model = Create(api);
        model.SetContext(true, "Stranger", "key");
        await model.RefreshAsync();
        Assert.False(model.CanRemoveSystems);
        model.RequestSystemRemoval(model.Detail!.Systems[0]);
        Assert.False(model.IsSystemRemovalVisible);
        Assert.Equal(0, api.SystemChanges);
    }

    private static ColonizationNexusViewModel Create(ColonizationNexusEditorViewModelTests.Writer api)
    {
        var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        return model;
    }
}
