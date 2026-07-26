namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayGameWindowTracker : IGameWindowTracker
{
    private readonly IGameWindowTracker inner;
    private readonly Func<bool> keepWhenGameLosesFocus;

    public OverlayGameWindowTracker(
        IGameWindowTracker inner,
        Func<bool> keepWhenGameLosesFocus)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.keepWhenGameLosesFocus = keepWhenGameLosesFocus
            ?? throw new ArgumentNullException(nameof(keepWhenGameLosesFocus));
    }

    public GameWindowSnapshot GetSnapshot()
    {
        var snapshot = inner.GetSnapshot();
        return keepWhenGameLosesFocus()
            && snapshot.IsAvailable
            && snapshot.IsVisible
                ? snapshot with { IsForeground = true }
                : snapshot;
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
