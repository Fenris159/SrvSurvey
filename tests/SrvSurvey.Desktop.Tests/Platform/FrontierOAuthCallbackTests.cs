using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class FrontierOAuthCallbackTests
{
    [Fact]
    public void ParsesOnlyTheRegisteredExactCallback()
    {
        var callback = FrontierOAuthCallback.Parse(
            "\"srvsurvey://frontier-auth?code=abc%20123&state=state-value\"");

        Assert.NotNull(callback);
        Assert.Equal("abc 123", callback.Code);
        Assert.Equal("state-value", callback.State);
        Assert.Null(FrontierOAuthCallback.Parse(
            "srvsurvey://frontier-auth.evil.example?code=abc&state=state-value"));
        Assert.Null(FrontierOAuthCallback.Parse(
            "srvsurvey://frontier-auth/path?code=abc&state=state-value"));
        Assert.Null(FrontierOAuthCallback.Parse(
            "https://frontier-auth?code=abc&state=state-value"));
    }

    [Fact]
    public void FindsCallbackWithoutExposingOtherArguments()
    {
        var callback = FrontierOAuthCallback.Find(
        [
            "--hide",
            "srvsurvey://frontier-auth?error=access_denied&error_description=No&state=s",
        ]);

        Assert.NotNull(callback);
        Assert.Equal("access_denied", callback.Error);
        Assert.Equal("No", callback.ErrorDescription);
    }

    [Theory]
    [InlineData("same", "same", true)]
    [InlineData("same", "other", false)]
    [InlineData("short", "longer", false)]
    public void StateComparisonIsExact(
        string left,
        string right,
        bool expected)
    {
        Assert.Equal(expected, FrontierOAuthCallback.FixedTimeEquals(left, right));
    }
}
