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
}
