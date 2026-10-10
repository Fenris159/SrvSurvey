namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Yields pointer routing to Steam requests and restores it only while live interaction is requested.</summary>
internal sealed class GamescopePointerInputLease(Func<bool> canAcquire, Func<bool, bool> apply, Action maintainStack)
{
    private bool requested;

    public bool IsActive { get; private set; }

    public bool Start()
    {
        requested = true;
        Refresh();
        if (!IsActive)
        {
            Stop();
        }
        return IsActive;
    }

    public void Refresh()
    {
        if (!requested)
        {
            return;
        }
        bool acquire = canAcquire();
        if (acquire != IsActive)
        {
            if (!apply(acquire))
            {
                Stop();
                return;
            }
            IsActive = acquire;
        }
        if (IsActive)
        {
            maintainStack();
        }
    }

    public void Stop()
    {
        requested = false;
        _ = apply(false);
        IsActive = false;
    }
}
