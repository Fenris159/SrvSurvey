using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class PortalResourceWaitTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Verifies a successful wait transfers disposal responsibility to its caller.</summary>
    [Fact]
    public async Task SuccessfulResourceIsNotDisposedByTheWait()
    {
        using var resource = new OwnedResource();

        OwnedResource result = await PortalResourceWait.WaitAsync(
            Task.FromResult(resource),
            Timeout,
            CancellationToken.None
        );

        Assert.Same(resource, result);
        Assert.False(resource.Disposed.Task.IsCompleted);
    }

    /// <summary>Verifies resource creation can complete after cancellation or timeout without leaking it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateResourceIsDisposed(bool cancel)
    {
        var startup = new TaskCompletionSource<OwnedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        Task<OwnedResource> wait = PortalResourceWait.WaitAsync(startup.Task, Timeout, cancellation.Token);
        if (cancel)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => wait);
        }

        var resource = new OwnedResource();
        startup.SetResult(resource);

        await resource.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, resource.DisposeCalls);
    }

    /// <summary>Verifies a completion race cannot run slow disposal on the cancellation path.</summary>
    [Fact]
    public async Task LateDisposalCannotDelayCancellation()
    {
        var startup = new TaskCompletionSource<OwnedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new OwnedResource { Release = release.Task };
        Task<OwnedResource> wait = PortalResourceWait.WaitAsync(startup.Task, Timeout, cancellation.Token);
        try
        {
            await cancellation.CancelAsync();
            startup.SetResult(resource);

            await resource.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(resource.Disposed.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await resource.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>Verifies failures from late startup or disposal are observed and reported.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateFailureIsReported(bool disposalFailure)
    {
        var startup = new TaskCompletionSource<OwnedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new IOException("Late portal resource failure.");
        Task<OwnedResource> wait = PortalResourceWait.WaitAsync(
            startup.Task,
            Timeout,
            CancellationToken.None,
            exception => reported.TrySetResult(exception)
        );
        await Assert.ThrowsAsync<TimeoutException>(() => wait);
        if (disposalFailure)
        {
            startup.SetResult(new OwnedResource { OnDispose = () => throw error });
        }
        else
        {
            startup.SetException(error);
        }

        Assert.Same(error, await reported.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    /// <summary>Verifies an immediate startup failure is returned to the original caller.</summary>
    [Fact]
    public async Task StartupFailureIsPropagated()
    {
        var failure = new IOException("Portal connection failed.");

        IOException error = await Assert.ThrowsAsync<IOException>(() =>
            PortalResourceWait.WaitAsync(Task.FromException<OwnedResource>(failure), Timeout, CancellationToken.None)
        );

        Assert.Same(failure, error);
    }

    private sealed class OwnedResource : IDisposable
    {
        public TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Release { get; init; } = Task.CompletedTask;
        public Action? OnDispose { get; init; }
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            DisposalStarted.TrySetResult();
            Release.GetAwaiter().GetResult();
            Disposed.TrySetResult();
            OnDispose?.Invoke();
        }
    }
}
