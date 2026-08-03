using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Fact]
    public void StableVersionSortsAfterReleaseCandidatesWithTheSameCore()
    {
        var stable = ReleaseVersion.Parse("2.1.4.0");
        var first = ReleaseVersion.Parse("2.1.4.0-rc.1");
        var tenth = ReleaseVersion.Parse("2.1.4.0-rc.10");

        Assert.True(stable > tenth);
        Assert.True(tenth > first);
        Assert.False(stable.IsPrerelease);
        Assert.True(first.IsPrerelease);
    }

    [Theory]
    [InlineData("2.1")]
    [InlineData("2.1.4.0-rc.01")]
    [InlineData("2.1.4.0-")]
    [InlineData("xp-v2.1.4.0-rc.1")]
    public void TryParseRejectsInvalidReleaseVersions(string value)
    {
        Assert.False(ReleaseVersion.TryParse(value, out _));
    }

    [Fact]
    public void BuildMetadataDoesNotChangeReleaseIdentity()
    {
        var version = ReleaseVersion.Parse("2.1.4.0-rc.3+f1e389ba");

        Assert.Equal("2.1.4.0-rc.3", version.ToString());
        Assert.Equal(ReleaseVersion.Parse("2.1.4.0-rc.3"), version);
    }
}
