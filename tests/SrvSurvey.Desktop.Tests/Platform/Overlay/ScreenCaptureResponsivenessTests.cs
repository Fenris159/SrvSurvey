using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ScreenCaptureResponsivenessTests
{
    /// <summary>Models the UI timer reading capability while a worker awaits screen-sharing approval.</summary>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task CapabilityChecksDoNotWaitForPendingCapture(bool fallback, bool backoff, bool readReason)
    {
        using var pending = new PendingCapture();
        using IGameScreenCapture capture = Wrap(pending, fallback, backoff);
        Task<CapturedPixelBuffer> frame = Task.Run(() => capture.Capture(new PixelRect(0, 0, 1, 1)));
        Task<bool>? capability = null;
        bool responsive;
        try
        {
            await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            string? expectedReason = fallback ? "No X11 display." : null;
            capability = Task.Run(() => readReason ? capture.UnavailableReason == expectedReason : capture.IsAvailable);
            responsive = await Task.WhenAny(capability, Task.Delay(TimeSpan.FromSeconds(1))) == capability;
        }
        finally
        {
            pending.Release.SetResult();
            await frame;
        }

        Assert.True(await capability!);
        Assert.True(responsive, "The UI capability check waited for the capture that needs UI approval.");
    }

    /// <summary>Models Settings changing a source while the old portal session is still closing.</summary>
    [Fact]
    public async Task SourceReselectionYieldsWhileThePreviousSessionCloses()
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-wayland-reselection-").FullName;
        var closing = new ClosingCapture();
        var viewModel = new WaylandCaptureSettingsViewModel(
            directory,
            isApplicable: true,
            openSourcePicker: () => Task.CompletedTask
        )
        {
            IsEnabled = true,
        };
        using var capture = new GatedGameScreenCapture(() => closing);
        _ = capture.Capture(new PixelRect(0, 0, 1, 1));
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = Task.Run(() => returned.SetResult(viewModel.ChooseCaptureSourceAgainAsync()));
        bool responsive;
        try
        {
            await closing.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            responsive = await Task.WhenAny(returned.Task, Task.Delay(TimeSpan.FromSeconds(1))) == returned.Task;
            Assert.True(WaylandCaptureSourceSelection.IsManualSelectionInProgress);
        }
        finally
        {
            closing.Release.SetResult();
            await command;
            await await returned.Task;
            capture.Dispose();
            GameScreenCapture.WaylandPortalEnabled = false;
            Directory.Delete(directory, recursive: true);
        }

        Assert.True(responsive, "The source-picker command synchronously waited for portal cleanup.");
        Assert.False(WaylandCaptureSourceSelection.IsManualSelectionInProgress);
    }

    private static IGameScreenCapture Wrap(IGameScreenCapture capture, bool fallback, bool backoff)
    {
        if (fallback)
        {
            capture = new FallbackGameScreenCapture(new UnavailableGameScreenCapture("No X11 display."), capture);
        }

        return backoff ? new BackoffGameScreenCapture(capture) : capture;
    }

    private sealed class PendingCapture : IGameScreenCapture
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public CapturedPixelBuffer Capture(PixelRect bounds)
        {
            Started.SetResult();
            Release.Task.GetAwaiter().GetResult();
            return new CapturedPixelBuffer(1, 1, [0, 0, 0, 255]);
        }

        public void Dispose() => Release.TrySetResult();
    }

    private sealed class ClosingCapture : IGameScreenCapture
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public CapturedPixelBuffer Capture(PixelRect bounds) => new(1, 1, [0, 0, 0, 255]);

        public void Dispose()
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
        }
    }
}
