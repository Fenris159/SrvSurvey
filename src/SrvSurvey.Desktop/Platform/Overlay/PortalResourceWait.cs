namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Retains ownership of a portal resource even if its startup outlives the caller's wait.</summary>
internal static class PortalResourceWait
{
    /// <summary>Returns the resource or arranges background disposal of a late successful result.</summary>
    public static async Task<T> WaitAsync<T>(
        Task<T> startup,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<Exception>? reportFailure = null
    )
        where T : IDisposable
    {
        try
        {
            return await startup.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            // Queue cleanup so even a resource that just completed cannot block the cancellation path in Dispose.
            _ = Task.Run(() => DisposeLateResourceAsync(startup, reportFailure), CancellationToken.None);
            throw;
        }
    }

    /// <summary>Observes late startup failures and disposes late resources without using the canceled token.</summary>
    private static async Task DisposeLateResourceAsync<T>(Task<T> startup, Action<Exception>? reportFailure)
        where T : IDisposable
    {
        try
        {
            using T resource = await startup.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (startup.IsCanceled)
        {
            // The underlying startup operation honored cancellation and produced no resource.
        }
        catch (Exception exception)
        {
            reportFailure?.Invoke(exception);
        }
    }
}
