using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationProjectEditorViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedProjectCanCancelAndStartFreshWithoutPublishing(bool reviewFirst)
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.ProjectName = "Discard this";
        editor.Notes = "Discard these notes";
        if (reviewFirst)
        {
            editor.BodyName = "Sol 36";
            await editor.ReviewAsync();
            Assert.True(editor.IsConfirmationPending);
        }
        Assert.True(editor.PrepareCommand.CanExecute(null));
        Assert.Equal("Cancel", editor.PrepareButtonText);
        await editor.PrepareAsync();
        Assert.False(editor.IsPrepared);
        Assert.False(editor.IsConfirmationPending);
        Assert.Empty(editor.ProjectName);
        Assert.Empty(editor.Notes);
        Assert.Equal("Prepare new project", editor.PrepareButtonText);
        Assert.Equal(0, client.CreateCount);
        await editor.PrepareAsync();
        Assert.True(editor.IsPrepared);
        Assert.NotEqual("Discard this", editor.ProjectName);
        Assert.Empty(editor.Notes);
    }
}
