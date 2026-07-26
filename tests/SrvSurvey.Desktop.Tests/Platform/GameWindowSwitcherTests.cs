using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GameWindowSwitcherTests
{
    [Fact]
    public void CurrentPreservesForegroundOrPreviousCommanderWindow()
    {
        nint[] windows = [11, 22, 33];

        Assert.Equal((nint)22, GameWindowCycle.SelectCurrent(windows, 22, 11));
        Assert.Equal((nint)33, GameWindowCycle.SelectCurrent(windows, 999, 33));
        Assert.Equal((nint)11, GameWindowCycle.SelectCurrent(windows, 999, 0));
        Assert.Equal(nint.Zero, GameWindowCycle.SelectCurrent([], 999, 0));
    }

    [Fact]
    public void CycleMovesAfterForegroundAndWraps()
    {
        nint[] windows = [11, 22, 33];

        Assert.Equal((nint)33, GameWindowCycle.SelectNext(windows, 22, 0));
        Assert.Equal((nint)11, GameWindowCycle.SelectNext(windows, 33, 0));
    }

    [Fact]
    public void CycleUsesPreviousWindowWhenApplicationHasFocus()
    {
        nint[] windows = [11, 22, 33];

        Assert.Equal((nint)33, GameWindowCycle.SelectNext(windows, 999, 22));
        Assert.Equal((nint)11, GameWindowCycle.SelectNext(windows, 999, 0));
        Assert.Equal(nint.Zero, GameWindowCycle.SelectNext([], 999, 0));
    }
}
