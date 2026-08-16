using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class GitHubReleaseNotesTests
{
    [Fact]
    public void ExtractChangesIncludesIntroductionAndFirstChangesSectionOnly()
    {
        const string markdown = """
            # SrvSurvey-XP 2.1.3.0-rc.26

            Summary of this release.

            ## What's changed since rc.25

            - First change.
            - Second change.

            ## Packaging

            - Hidden package detail.

            ## Testing notice

            Hidden notice.
            """;

        var result = GitHubReleaseNotes.ExtractChanges(markdown);

        Assert.Contains("Summary of this release.", result);
        Assert.Contains("- Second change.", result);
        Assert.DoesNotContain("Packaging", result);
        Assert.DoesNotContain("Testing notice", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("## Packaging\nNothing to display.")]
    public void ExtractChangesReturnsEmptyWithoutAChangesSection(string? markdown)
    {
        Assert.Empty(GitHubReleaseNotes.ExtractChanges(markdown));
    }
}
