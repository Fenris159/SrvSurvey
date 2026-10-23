using Tmds.DBus;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Bounds portal method calls while allowing the user time to answer the source picker.</summary>
internal static class ScreenCastPortalRequest
{
    /// <summary>Subscribes before invoking a request and closes it when startup is canceled or stalls.</summary>
    public static async Task<PortalResponse> InvokeAsync(
        IRequest request,
        ObjectPath expectedPath,
        Func<Task<ObjectPath>> invoke,
        TimeSpan methodTimeout,
        CancellationToken cancellationToken,
        Action<Exception>? reportLateFailure = null
    )
    {
        var completion = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using IDisposable subscription = await PortalResourceWait
                .WaitAsync(
                    request.WatchResponseAsync(
                        response => completion.TrySetResult(new PortalResponse(response.Response, response.Results)),
                        exception => completion.TrySetException(exception)
                    ),
                    methodTimeout,
                    cancellationToken,
                    reportLateFailure
                )
                .ConfigureAwait(false);
            ObjectPath returnedPath = await invoke().WaitAsync(methodTimeout, cancellationToken).ConfigureAwait(false);
            if (returnedPath != expectedPath)
            {
                throw new InvalidOperationException("The desktop portal returned an unexpected request handle.");
            }

            PortalResponse result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return result.ResponseCode switch
            {
                0 => result,
                1 => throw new NotSupportedException("Wayland screen sharing was canceled."),
                _ => throw new NotSupportedException("The Wayland desktop denied screen sharing."),
            };
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            await CloseAsync(request, methodTimeout).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Bounds cleanup even when the portal cannot acknowledge closing the request.</summary>
    private static async Task CloseAsync(IRequest request, TimeSpan methodTimeout)
    {
        try
        {
            await request.CloseAsync().WaitAsync(methodTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusException or TimeoutException or IOException)
        {
            // A closed or unresponsive portal must not prevent capture cancellation.
        }
    }
}

internal sealed record PortalResponse(uint ResponseCode, IDictionary<string, object> Results);
