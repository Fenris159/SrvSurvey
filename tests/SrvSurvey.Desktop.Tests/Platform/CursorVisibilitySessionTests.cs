using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class CursorVisibilitySessionTests
{
    [Fact]
    public void DisposeBalancesEveryAdjustmentAndIsIdempotent()
    {
        var requests = new List<bool>();
        var incrementCount = 0;
        int ShowCursor(bool show)
        {
            requests.Add(show);
            return show
                ? ++incrementCount - 3
                : 0;
        }

        var session = CursorVisibilitySession.Begin(ShowCursor);

        Assert.Equal([true, true, true], requests);

        session.Dispose();
        session.Dispose();

        Assert.Equal(
            [true, true, true, false, false, false],
            requests);
    }

    [Fact]
    public void BeginStopsAtTheSafetyLimitWhenTheCounterStaysNegative()
    {
        var increments = 0;
        var decrements = 0;
        int ShowCursor(bool show)
        {
            if (show)
            {
                increments++;
                return -1;
            }

            decrements++;
            return 0;
        }

        using (CursorVisibilitySession.Begin(ShowCursor))
        {
            Assert.Equal(64, increments);
            Assert.Equal(0, decrements);
        }

        Assert.Equal(64, decrements);
    }

    [Fact]
    public void BeginRejectsAMissingCursorCallback()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CursorVisibilitySession.Begin(null!));
    }
}
