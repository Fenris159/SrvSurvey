using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class GitHubReleaseNotesTests
{
    /// <summary>Verifies that updater excerpts include the introduction and changes while excluding packaging and testing sections.</summary>
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

        string result = GitHubReleaseNotes.ExtractChanges(markdown);

        Assert.Contains("Summary of this release.", result);
        Assert.Contains("- Second change.", result);
        Assert.DoesNotContain("Packaging", result);
        Assert.DoesNotContain("Testing notice", result);
    }

    /// <summary>Verifies that updater excerpts retain grouped features and absolute Markdown links.</summary>
    [Fact]
    public void ExtractChangesPreservesFeatureSubheadingsAndAbsoluteLinks()
    {
        const string markdown = """
            # SrvSurvey-XP

            Changes in [PR #250](https://github.com/Fenris159/SrvSurvey/pull/250).

            ## What's changed

            ### Mining

            - Mission tracking and session yields.

            ### Combat

            - Mission progress and overlay priority.

            ## Update channel

            Hidden package detail.
            """;

        string excerpt = GitHubReleaseNotes.ExtractChanges(markdown);

        Assert.Contains("[PR #250](https://github.com/Fenris159/SrvSurvey/pull/250)", excerpt);
        Assert.Contains("### Mining", excerpt);
        Assert.Contains("- Mission tracking and session yields.", excerpt);
        Assert.Contains("### Combat", excerpt);
        Assert.Contains("- Mission progress and overlay priority.", excerpt);
        Assert.DoesNotContain("## Update channel", excerpt);
        Assert.DoesNotContain("Hidden package detail.", excerpt);
    }

    /// <summary>Verifies that notes without a recognized changes section produce no updater excerpt.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("## Packaging\nNothing to display.")]
    public void ExtractChangesReturnsEmptyWithoutAChangesSection(string? markdown)
    {
        Assert.Empty(GitHubReleaseNotes.ExtractChanges(markdown));
    }

    [Fact]
    public void ExtractChangesExcludesSecondLevelSectionsBeforeChanges()
    {
        const string markdown = """
            # SrvSurvey-XP 2.1.3.0-rc.27

            Summary of this release.

            ## Known issues

            - Do not include this section.

            ## What's changed since rc.26

            - Include this change.

            ## Packaging

            - Do not include this section either.
            """;

        string result = GitHubReleaseNotes.ExtractChanges(markdown);

        Assert.Contains("Summary of this release.", result);
        Assert.Contains("Include this change.", result);
        Assert.DoesNotContain("Known issues", result);
        Assert.DoesNotContain("Do not include", result);
        Assert.DoesNotContain("Packaging", result);
    }
}
