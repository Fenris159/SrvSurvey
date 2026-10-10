using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GamescopePointerInputLeaseTests
{
    [Fact]
    public void YieldsToSteamThenReacquiresAndRestoresBeforeStop()
    {
        bool steamRequestsInput = false;
        var transitions = new List<bool>();
        int stacking = 0;
        var lease = new GamescopePointerInputLease(
            () => !steamRequestsInput,
            active =>
            {
                transitions.Add(active);
                return true;
            },
            () => stacking++
        );
        lease.Refresh();
        Assert.Empty(transitions);
        Assert.True(lease.Start());
        lease.Refresh();
        Assert.Equal(2, stacking);
        Assert.Equal([true], transitions);
        steamRequestsInput = true;
        lease.Refresh();
        lease.Refresh();
        Assert.False(lease.IsActive);
        Assert.Equal([true, false], transitions);
        Assert.Equal(2, stacking);
        steamRequestsInput = false;
        lease.Refresh();
        Assert.True(lease.IsActive);
        Assert.Equal([true, false, true], transitions);
        lease.Stop();
        lease.Refresh();
        Assert.False(lease.IsActive);
        Assert.Equal([true, false, true, false], transitions);
    }

    [Fact]
    public void RefusesInitialSteamRequestAndRestoresAfterNativeFailure()
    {
        var transitions = new List<bool>();
        bool canAcquire = false;
        bool nativeSucceeds = true;
        var lease = new GamescopePointerInputLease(
            () => canAcquire,
            active =>
            {
                transitions.Add(active);
                return nativeSucceeds;
            },
            () => { }
        );
        Assert.False(lease.Start());
        Assert.Equal([false], transitions);
        canAcquire = true;
        lease.Refresh();
        Assert.False(lease.IsActive);
        Assert.True(lease.Start());
        nativeSucceeds = false;
        canAcquire = false;
        lease.Refresh();
        Assert.False(lease.IsActive);
        Assert.Equal([false, true, false, false], transitions);
        canAcquire = true;
        lease.Refresh();
        Assert.False(lease.IsActive);
        Assert.False(lease.Start());
        Assert.Equal([false, true, false, false, true, false, false], transitions);
    }
}
