using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ReleaseNotesDialogViewModelTests
{
    [Fact]
    public void CreateFormatsGitHubReleaseIntroductionAndBullets()
    {
        const string markdown = """
            # SrvSurvey-XP 2.1.3.0-rc.26

            This release makes Boxel search clearer.

            ## What's changed since rc.25

            - Adds **Mark Next Empty** and continues
              onto the following target.
            - Displays `10` systems per page.
            """;

        var result = ReleaseNotesDialogViewModel.Create(
            "Fallback title",
            markdown);

        Assert.Equal("SrvSurvey-XP 2.1.3.0-rc.26", result.Title);
        Assert.Equal("This release makes Boxel search clearer.", result.Introduction);
        Assert.Equal("What's changed since rc.25", result.ChangesHeading);
        Assert.Collection(
            result.Changes,
            change => Assert.Equal(
                "Adds Mark Next Empty and continues onto the following target.",
                change.Text),
            change => Assert.Equal("Displays 10 systems per page.", change.Text));
    }

    [Fact]
    public void CreateFallsBackToSingleChangeWithoutChangesHeading()
    {
        const string markdown = "A release note without a changes heading.";

        var result = ReleaseNotesDialogViewModel.Create(
            "Fallback title",
            markdown);

        Assert.Equal("Fallback title", result.Title);
        Assert.Empty(result.Introduction);
        Assert.Equal("What's changed", result.ChangesHeading);
        var change = Assert.Single(result.Changes);
        Assert.Equal(markdown, change.Text);
    }

    [Fact]
    public void CreateDoesNotSliceBackwardsWhenTitleFollowsChangesHeading()
    {
        const string markdown = """
            Intro before changes.

            ## What's changed

            - First change.

            # Late title
            """;

        var result = ReleaseNotesDialogViewModel.Create(
            "Fallback title",
            markdown);

        Assert.Equal("Late title", result.Title);
        Assert.Empty(result.Introduction);
        Assert.Equal("What's changed", result.ChangesHeading);
        var change = Assert.Single(result.Changes);
        Assert.Equal("First change. # Late title", change.Text);
    }
}
