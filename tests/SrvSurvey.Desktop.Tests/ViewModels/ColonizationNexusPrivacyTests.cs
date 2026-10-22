using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationNexusPrivacyTests
{
    [Fact]
    public async Task TogglesPrivacyInBothDirectionsWithoutRewritingMembershipOrLosingHubSelection()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = NexusFixture.Snapshot() };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        Assert.False(model.TogglePrivacyCommand.CanExecute(null));
        await model.TogglePrivacyAsync();
        model.SetContext(true, "Test", "test-key");
        await model.RefreshAsync();
        model.Detail!.SelectedSystem = model.Detail.Systems[1];
        model.Detail.SelectedScope = model.Detail.Scopes[0];
        Assert.True(model.IsPublic);
        Assert.Contains("entire Nexus private", model.PrivacyHint);
        model.TogglePrivacyCommand.Execute(null);
        await model.ActiveTask;
        Assert.False(model.IsPublic);
        Assert.False(model.SelectedNexus!.Open);
        Assert.Contains("entire Nexus public", model.PrivacyHint);
        Assert.Equal(2, model.Detail.SelectedSystem.SystemAddress);
        Assert.Equal(0, model.Detail.SelectedScope.SystemAddress);
        Assert.Contains("now private", model.StatusMessage);
        await model.TogglePrivacyAsync();
        Assert.True(model.IsPublic);
        Assert.True(model.SelectedNexus.Open);
        Assert.Contains("now public", model.StatusMessage);
        Assert.Equal(2, api.PrivacyChanges);
        Assert.Equal(0, api.Configures);
        Assert.Equal("Test", api.Commander);
        Assert.Equal("test-key", api.Key);
    }

    [Fact]
    public async Task FailuresKeepConfirmedStatusAndPreventNonmembersFromChangingPrivacy()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer
        {
            Existing = NexusFixture.Snapshot(),
            PrivacyFailure = new IOException("secret must not be shown"),
        };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        await model.TogglePrivacyAsync();
        Assert.True(model.IsPublic);
        Assert.Contains("could not be confirmed", model.StatusMessage);
        Assert.DoesNotContain("secret", model.StatusMessage);
        Assert.False(model.IsBusy);
        model.SetContext(true, "Stranger", "other-key");
        await model.RefreshAsync();
        Assert.False(model.TogglePrivacyCommand.CanExecute(null));
        await model.TogglePrivacyAsync();
        Assert.Equal(1, api.PrivacyChanges);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingSaveCannotOverwriteAChangedProfileAndCannotBeSubmittedTwice(bool changeProfile)
    {
        var pending = new TaskCompletionSource<ColonizationNexus>();
        var api = new ColonizationNexusEditorViewModelTests.Writer
        {
            Existing = NexusFixture.Snapshot(),
            PendingPrivacy = pending.Task,
        };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        using var cancel = new CancellationTokenSource();
        Task saving = model.TogglePrivacyAsync(cancel.Token);
        Assert.True(model.IsBusy);
        Assert.False(model.TogglePrivacyCommand.CanExecute(null));
        await model.TogglePrivacyAsync();
        Assert.Equal(1, api.PrivacyChanges);
        if (changeProfile)
        {
            model.SetContext(true, "Other", "other-key");
        }
        else
        {
            await cancel.CancelAsync();
        }
        pending.SetResult(NexusFixture.Snapshot() with { Open = false });
        await saving;
        Assert.False(model.IsBusy);
        if (changeProfile)
        {
            Assert.Null(model.Detail);
        }
        else
        {
            Assert.True(model.IsPublic);
        }
    }
}
