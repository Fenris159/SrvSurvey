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

        string result = GitHubReleaseNotes.ExtractChanges(markdown);

        Assert.Contains("Summary of this release.", result);
        Assert.Contains("- Second change.", result);
        Assert.DoesNotContain("Packaging", result);
        Assert.DoesNotContain("Testing notice", result);
    }

    [Fact]
    public void CurrentReleaseNotesExposeAllFeatureGroupsAndAPublishedGuideLink()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        string markdown = File.ReadAllText(Path.Combine(directory.FullName, "CurrentReleaseNotes.md"));

        string excerpt = GitHubReleaseNotes.ExtractChanges(markdown);

        Assert.Contains("Steam Deck Gaming Mode", excerpt);
        Assert.Contains("Next Jump overlay", excerpt);
        Assert.Contains("Commander Powerplay status", excerpt);
        Assert.Contains("](https://github.com/Fenris159/SrvSurvey/blob/", excerpt);
        Assert.DoesNotContain("## Update channel", excerpt);
    }

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
