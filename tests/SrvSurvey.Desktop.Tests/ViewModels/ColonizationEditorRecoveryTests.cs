using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationProjectEditorViewModelTests
{
    /// <summary>Does not dereference an invalid dock or resurrect a form after an outstanding preparation request.</summary>
    [Fact]
    public async Task PreparationIgnoresUndockedContext()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<ColonizationSystemSite>>();
        var client = new StubRavenColonialClient { PendingSites = gate.Task };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        Task prepare = editor.PrepareAsync();
        await Task.Yield();
        editor.UpdateContext(ColonizationProjectEditorContext.Unavailable);
        gate.SetResult([]);
        await prepare;
        Assert.False(editor.IsPrepared);
        Assert.False(editor.IsBusy);
    }

    /// <summary>Reports remote creation as successful even if local cleanup fails afterward.</summary>
    [Fact]
    public async Task CleanupFailureDoesNotClaimCreationFailed()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(
            client,
            _ => Task.FromException(new HttpRequestException("cleanup unavailable"))
        );
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.ProjectName = "Port";
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        editor.BodyName = "Test A 1";
        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();
        Assert.True(editor.HasCreatedProject);
        Assert.Contains("Created", editor.StatusMessage);
        Assert.Contains("cleanup", editor.StatusMessage);
        Assert.DoesNotContain("not created", editor.StatusMessage);
    }
}

public sealed partial class ColonizationSystemEditorViewModelTests
{
    /// <summary>Discards an old system load after a context change, including an away-and-back transition.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadDiscardsObsoleteContext(bool returnToOriginal)
    {
        var gate = new TaskCompletionSource<ColonizationSystemRecord>();
        var client = new StubClient { PendingRead = gate.Task };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        Task load = editor.LoadAsync();
        editor.UpdateContext(Context() with { SystemAddress = 43, SystemName = "Other" });
        if (returnToOriginal)
        {
            editor.UpdateContext(Context());
        }
        gate.SetResult(System());
        await load;
        Assert.False(editor.IsLoaded);
        Assert.False(editor.CanEdit);
    }

    /// <summary>Reconciles remote name and market repairs made after the user reviewed a body edit.</summary>
    [Fact]
    public async Task ConfirmationPreservesChangesAfterReview()
    {
        var client = new StubClient { Current = System() };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].BodyNumber = 2;
        await editor.ReviewAsync();
        client.Current = client.Current with
        {
            Revision = 2,
            Sites = [client.Current.Sites[0] with { Name = "Renamed", MarketId = 4300000123 }],
        };
        await editor.ConfirmPublishAsync();
        ColonizationSystemSite submitted = Assert.Single(client.LastUpdate!.UpdatedSites);
        Assert.Equal("Renamed", submitted.Name);
        Assert.Equal(4300000123, submitted.MarketId);
        Assert.Equal(2, submitted.BodyNumber);
        Assert.Equal(3, client.SystemReadCount);
    }

    /// <summary>Requires another review when a conflicting edit appears between review and confirmation.</summary>
    [Fact]
    public async Task ConfirmationRefusesNewConflict()
    {
        var client = new StubClient { Current = System() };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].BodyNumber = 2;
        await editor.ReviewAsync();
        client.Current = client.Current with { Sites = [client.Current.Sites[0] with { BodyNumber = 3 }] };
        await editor.ConfirmPublishAsync();
        Assert.Equal(0, client.UpdateCount);
        Assert.True(editor.HasConflicts);
        Assert.False(editor.IsPublishConfirmationPending);
    }

    /// <summary>Rejects obsolete results from body import, review, and publication.</summary>
    [Theory]
    [InlineData("import")]
    [InlineData("review")]
    [InlineData("publish")]
    public async Task DiscardsObsoleteEditorOperation(string operation)
    {
        var client = new StubClient
        {
            Current = System() with { Bodies = operation == "import" ? null : System().Bodies },
        };
        ColonizationSystemEditorViewModel editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        var gate = new TaskCompletionSource<ColonizationSystemRecord>();
        Task pending;
        if (operation == "import")
        {
            editor.RequestBodyImport();
            client.PendingImport = gate.Task;
            pending = editor.ConfirmBodyImportAsync();
        }
        else
        {
            editor.Sites[0].BodyNumber = 2;
            if (operation == "review")
            {
                client.PendingRead = gate.Task;
                pending = editor.ReviewAsync();
            }
            else
            {
                await editor.ReviewAsync();
                client.PendingUpdate = gate.Task;
                pending = editor.ConfirmPublishAsync();
            }
        }
        editor.UpdateContext(Context() with { SystemAddress = 43, SystemName = "Other" });
        gate.SetResult(System());
        await pending;
        Assert.False(editor.IsLoaded);
        Assert.False(editor.IsPublishConfirmationPending);
    }
}
