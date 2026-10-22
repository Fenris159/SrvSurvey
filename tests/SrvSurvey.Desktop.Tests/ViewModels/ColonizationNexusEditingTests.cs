using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationNexusEditorViewModelTests
{
    [Fact]
    public async Task EditsExistingFieldsPreservesOwnerAndRefreshesWithoutCreatingAgain()
    {
        ColonizationNexus source = ExistingNexus();
        var api = new Writer { Existing = source };
        using var nexus = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        nexus.SetContext(true, "Fenris", "key");
        nexus.SetEditOpener(() => { });
        Assert.False(nexus.EditNexusCommand.CanExecute(null));
        await nexus.RefreshAsync();
        Assert.True(nexus.EditNexusCommand.CanExecute(null));
        using ColonizationNexusEditorViewModel model = nexus.CreateEditor([], edit: true);
        Assert.True(model.IsEditing);
        Assert.True(model.CanEditName);
        Assert.Equal("Edit Nexus", model.Title);
        Assert.Equal("Fenris", model.Owner);
        Assert.Equal("Old notes", model.Notes);
        Assert.False(model.IsPrivate);
        Assert.Equal("Sol", Assert.Single(model.Systems.Rows).Name);
        Assert.Equal("Other", Assert.Single(model.Commanders.Rows).Name);
        Assert.Equal(42, Assert.Single(model.Carriers).MarketId);
        model.Name = "Renamed";
        model.Notes = "New notes";
        model.IsPrivate = true;
        model.Systems.Rows[0].EditCommand.Execute(null);
        model.Systems.Draft = "Alpha";
        model.Systems.CommitCommand.Execute(null);
        model.Commanders.Rows[0].RemoveCommand.Execute(null);
        model.RemoveCarrierCommand.Execute(model.Carriers[0]);
        await model.SubmitAsync();
        Assert.Equal(0, api.Creates);
        Assert.Equal(1, api.Renames);
        Assert.Equal("Renamed", nexus.Detail!.Nexus.Name);
        Assert.Equal("New notes", api.Draft!.Notes);
        Assert.True(api.Draft.IsPrivate);
        Assert.Equal("Alpha", Assert.Single(api.Draft.Systems));
        Assert.Equal("Fenris", Assert.Single(api.Draft.Commanders));
        Assert.Empty(api.Draft.CarrierIds);
        Assert.Equal(2, api.IndexReads);
    }

    [Fact]
    public async Task MemberMayEditButCannotDeleteAndNonmemberCannotOpenEditor()
    {
        ColonizationNexus source = ExistingNexus();
        var api = new Writer { Existing = source };
        using var nexus = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        nexus.SetEditOpener(() => { });
        nexus.SetContext(true, "Other", "key");
        await nexus.RefreshAsync();
        Assert.True(nexus.EditNexusCommand.CanExecute(null));
        using ColonizationNexusEditorViewModel model = nexus.CreateEditor([], true);
        Assert.False(model.CanDelete);
        model.RequestDeletion();
        Assert.False(model.IsDeleteConfirmationVisible);
        await model.ConfirmDeletionAsync();
        Assert.Equal(0, api.Deletes);
        model.Commanders.Rows[0].RemoveCommand.Execute(null);
        await model.SubmitAsync();
        Assert.Equal(0, api.Renames);
        Assert.Equal("Fenris", api.Draft!.Owner);
        Assert.Equal("Fenris", Assert.Single(api.Draft.Commanders));
        nexus.SetContext(true, "Stranger", "different-key");
        await nexus.RefreshAsync();
        Assert.False(nexus.EditNexusCommand.CanExecute(null));
        Assert.Throws<InvalidOperationException>(() => nexus.CreateEditor([], true));
    }

    [Fact]
    public async Task OwnerDeletionRequiresExplicitConfirmationCanBeCancelledAndRefreshesList()
    {
        var api = new Writer { Existing = ExistingNexus() };
        using var nexus = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        nexus.SetContext(true, "Fenris", "key");
        await nexus.RefreshAsync();
        string? deleted = null;
        nexus.NexusDeleted += id => deleted = id;
        using ColonizationNexusEditorViewModel model = nexus.CreateEditor([], true);
        bool closed = false;
        model.CloseRequested += () => closed = true;
        await model.ConfirmDeletionAsync();
        Assert.Equal(0, api.Deletes);
        Assert.True(model.CanDelete);
        model.RequestDeletion();
        Assert.Contains("cannot be undone", model.DeleteWarning);
        Assert.True(model.IsDeleteConfirmationVisible);
        Assert.False(model.CanSubmit);
        model.CancelDeletion();
        Assert.False(model.IsDeleteConfirmationVisible);
        Assert.True(model.CanSubmit);
        Assert.Equal(0, api.Deletes);
        model.RequestDeletion();
        await model.ConfirmDeletionAsync();
        Assert.Equal(1, api.Deletes);
        Assert.Equal("new", deleted);
        Assert.True(closed);
        Assert.Empty(nexus.Nexuses);
        Assert.Null(nexus.Detail);
        Assert.False(model.CanConfirmDeletion);
    }

    [Fact]
    public async Task EditingAndDeletionFailuresRetainExistingIdentityAndExplainFailure()
    {
        var api = new Writer
        {
            Existing = ExistingNexus(),
            ConfigureFailure = new IOException("offline"),
            DeleteFailure = new IOException("offline"),
        };
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => true,
            _ => Task.CompletedTask,
            [],
            ExistingNexus()
        );
        await model.SubmitAsync();
        Assert.Contains("could not be saved", model.Status);
        Assert.Contains("may already be applied", model.Status);
        Assert.Contains("Close this editor, refresh Nexus, and reopen", model.Status);
        Assert.DoesNotContain("retry", model.Status);
        Assert.Equal("Save changes", model.SubmitLabel);
        Assert.True(model.CanEditName);
        Assert.Equal(0, api.Creates);
        model.RequestDeletion();
        await model.ConfirmDeletionAsync();
        Assert.Contains("could not be confirmed", model.Status);
        Assert.True(model.IsDeleteConfirmationVisible);
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("systems")]
    [InlineData("commanders")]
    [InlineData("carriers")]
    [InlineData("notes")]
    [InlineData("name")]
    public async Task StaleEditorCannotReplaceAnotherMembersNewerChanges(string field)
    {
        ColonizationNexus source = ExistingNexus();
        var api = new Writer { Existing = source };
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => true,
            _ => Task.CompletedTask,
            [],
            source
        );
        model.Notes = "My edited notes";
        model.Name = "My edited name";
        api.Existing = field switch
        {
            "privacy" => source with { Open = false },
            "systems" => source with { Systems = [.. source.Systems, new() { Name = "Beta", SystemAddress = 2 }] },
            "commanders" => source with { Commanders = [.. source.Commanders, "New member"] },
            "carriers" => source with { FleetCarriers = [] },
            "notes" => source with { Notes = "Other notes" },
            _ => source with { Name = "Other name" },
        };
        await model.SubmitAsync();
        Assert.Equal(0, api.Renames);
        Assert.Equal(0, api.Configures);
        Assert.Contains("changed on Raven", model.Status);
        Assert.Equal("My edited notes", model.Notes);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task CargoAndProgressRefreshDoesNotConflictWithAnUnchangedEditorSource()
    {
        ColonizationNexus source = ExistingNexus();
        var api = new Writer { Existing = source };
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => true,
            _ => Task.CompletedTask,
            [],
            source
        );
        api.Existing = source with
        {
            Systems = [source.Systems[0] with { Progress = 25 }],
            FleetCarriers = [source.FleetCarriers[0] with { Cargo = new() { ["steel"] = 50 } }],
        };
        model.Notes = "My notes";
        await model.SubmitAsync();
        Assert.Equal(1, api.Configures);
    }

    [Fact]
    public async Task ChangedAccountWhileCheckingLatestEditorSourcePreventsWrites()
    {
        ColonizationNexus source = ExistingNexus();
        var pending = new TaskCompletionSource<ColonizationNexus?>();
        var api = new Writer { Existing = source, PendingNexusRead = pending.Task };
        bool current = true;
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => current,
            _ => Task.CompletedTask,
            [],
            source
        );
        Task saving = model.SubmitAsync();
        Assert.True(model.IsBusy);
        current = false;
        pending.SetResult(source);
        await saving;
        Assert.Equal(0, api.Configures);
        Assert.Equal(0, api.Renames);
        Assert.False(model.IsBusy);
    }

    private static ColonizationNexus ExistingNexus() =>
        new()
        {
            Id = "new",
            Name = "Original",
            Owner = "Fenris",
            Commanders = ["Fenris", "Other"],
            Notes = "Old notes",
            Open = true,
            Systems =
            [
                new()
                {
                    SystemAddress = 1,
                    Name = "Sol",
                    Needs = new() { ["steel"] = 40 },
                },
            ],
            FleetCarriers =
            [
                new()
                {
                    MarketId = 42,
                    Name = "ABC-123",
                    DisplayName = "Carrier",
                },
            ],
        };
}
