using SrvSurvey.Desktop.Platform.Overlay;
using Tmds.DBus;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class ScreenCastPortalRequestTests
{
    private static readonly ObjectPath RequestPath = new("/org/freedesktop/portal/desktop/request/test/source");
    private static readonly TimeSpan MethodTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Verifies a fast portal response is observed before the method returns its handle.</summary>
    [Fact]
    public async Task ResponseIsSubscribedBeforeTheMethodStarts()
    {
        var request = new StubRequest();
        var results = new Dictionary<string, object> { ["restore_token"] = "renewed-source" };

        PortalResponse response = await InvokeAsync(
            request,
            () =>
            {
                request.Respond(0, results);
                return Task.FromResult(RequestPath);
            }
        );

        Assert.Same(results, response.Results);
        Assert.True(request.SubscriptionDisposed);
        Assert.Equal(0, request.CloseCalls);
    }

    /// <summary>Verifies rejected and canceled picker responses remain distinct user-facing failures.</summary>
    [Theory]
    [InlineData(1U, "canceled")]
    [InlineData(2U, "denied")]
    public async Task UnsuccessfulResponseReportsTheReason(uint code, string message)
    {
        var request = new StubRequest();

        NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            InvokeAsync(
                request,
                () =>
                {
                    request.Respond(code, new Dictionary<string, object>());
                    return Task.FromResult(RequestPath);
                }
            )
        );

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.True(request.SubscriptionDisposed);
    }

    /// <summary>Verifies unexpected handles cannot accidentally attach to another pending picker.</summary>
    [Fact]
    public async Task UnexpectedHandleIsRejected()
    {
        var request = new StubRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(request, () => Task.FromResult(new ObjectPath("/another_request")))
        );

        Assert.True(request.SubscriptionDisposed);
    }

    /// <summary>Verifies cancellation reaches each startup wait, including the method's initial reply.</summary>
    [Theory]
    [InlineData("subscription")]
    [InlineData("method")]
    [InlineData("response")]
    public async Task CancellationClosesTheRequestWithoutWaitingForThePortal(string stage)
    {
        var request = new StubRequest();
        if (stage == "subscription")
        {
            request.Subscription = new TaskCompletionSource<IDisposable>().Task;
        }

        using var cancellation = new CancellationTokenSource();
        Task<PortalResponse> pending = InvokeAsync(
            request,
            () => stage == "method" ? new TaskCompletionSource<ObjectPath>().Task : Task.FromResult(RequestPath),
            cancellation.Token
        );

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, request.CloseCalls);
        Assert.Equal(stage != "subscription", request.SubscriptionDisposed);
    }

    /// <summary>Verifies stalled method and subscription calls have a deadline without timing out user input.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StalledPortalMethodTimesOut(bool subscription)
    {
        var request = new StubRequest();
        if (subscription)
        {
            request.Subscription = new TaskCompletionSource<IDisposable>().Task;
        }

        await Assert.ThrowsAsync<TimeoutException>(() =>
            InvokeAsync(request, () => new TaskCompletionSource<ObjectPath>().Task).WaitAsync(TimeSpan.FromSeconds(3))
        );

        Assert.Equal(1, request.CloseCalls);
    }

    /// <summary>Verifies taking longer than a method deadline to choose a source is allowed.</summary>
    [Fact]
    public async Task PickerResponseHasNoMethodDeadline()
    {
        var request = new StubRequest();
        Task<PortalResponse> pending = InvokeAsync(request, () => Task.FromResult(RequestPath));

        await Task.Delay(MethodTimeout + MethodTimeout);
        Assert.False(pending.IsCompleted);
        request.Respond(0, new Dictionary<string, object>());

        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, request.CloseCalls);
    }

    /// <summary>Verifies a lost portal connection is surfaced and releases the response subscription.</summary>
    [Fact]
    public async Task ResponseSubscriptionFailureIsReported()
    {
        var request = new StubRequest();
        var failure = new IOException("Portal disconnected.");

        IOException error = await Assert.ThrowsAsync<IOException>(() =>
            InvokeAsync(
                request,
                () =>
                {
                    request.Fail(failure);
                    return Task.FromResult(RequestPath);
                }
            )
        );

        Assert.Same(failure, error);
        Assert.True(request.SubscriptionDisposed);
    }

    /// <summary>Verifies cleanup cannot prolong cancellation when the request is gone or stops responding.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedRequestCloseDoesNotPreventCancellation(bool disconnected)
    {
        var request = new StubRequest
        {
            CloseResult = disconnected
                ? Task.FromException(new DBusException("org.freedesktop.DBus.Error.UnknownObject", "Closed."))
                : new TaskCompletionSource().Task,
        };
        using var cancellation = new CancellationTokenSource();
        Task<PortalResponse> pending = InvokeAsync(
            request,
            () => new TaskCompletionSource<ObjectPath>().Task,
            cancellation.Token
        );

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, request.CloseCalls);
        Assert.True(request.SubscriptionDisposed);
    }

    private static Task<PortalResponse> InvokeAsync(
        StubRequest request,
        Func<Task<ObjectPath>> invoke,
        CancellationToken cancellationToken = default
    ) => ScreenCastPortalRequest.InvokeAsync(request, RequestPath, invoke, MethodTimeout, cancellationToken);

    private sealed class StubRequest : IRequest, IDisposable
    {
        private Action<(uint Response, IDictionary<string, object> Results)>? response;
        private Action<Exception>? onError;

        public ObjectPath ObjectPath => RequestPath;
        public Task<IDisposable>? Subscription { get; set; }
        public Task CloseResult { get; init; } = Task.CompletedTask;
        public int CloseCalls { get; private set; }
        public bool SubscriptionDisposed { get; private set; }

        public Task<IDisposable> WatchResponseAsync(
            Action<(uint Response, IDictionary<string, object> Results)> handler,
            Action<Exception> onError
        )
        {
            response = handler;
            this.onError = onError;
            return Subscription ?? Task.FromResult<IDisposable>(this);
        }

        public Task CloseAsync()
        {
            CloseCalls++;
            return CloseResult;
        }

        public void Respond(uint code, IDictionary<string, object> results) => response!((code, results));

        public void Fail(Exception error) => onError!(error);

        public void Dispose() => SubscriptionDisposed = true;
    }
}
