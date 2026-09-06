using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MiningDetectionCoordinator : IDisposable
{
    private readonly SurfaceMiningViewModel mining;
    private readonly IGameWindowTracker tracker;
    private readonly IGameScreenCapture capture;
    private readonly DispatcherTimer timer;
    private bool busy;
    private bool disposed;

    public MiningDetectionCoordinator(SurfaceMiningViewModel mining, IGameWindowTracker tracker,
        IGameScreenCapture? capture = null)
    {
        this.mining = mining;
        this.tracker = tracker;
        this.capture = capture ?? GameScreenCapture.CreateCurrent();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += OnTick;
        timer.Start();
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        await SynchronizeAsync();
    }

    internal async Task SynchronizeAsync()
    {
        if (busy || disposed) return;
        var model = mining.Detection;
        var game = tracker.GetSnapshot();
        if (!(model.Enabled || model.IsCalibrating)) return;
        if (model.IsCalibrating && !model.IsCalibrationTesting) return;
        if (!game.IsAvailable || !game.IsVisible || (!model.IsCalibrating && !game.IsForeground)
            || !mining.CanDetectRigs)
        {
            model.Pause("Waiting for Elite's Rhino cockpit view.");
            return;
        }
        if (!capture.IsAvailable)
        {
            model.Pause(capture.UnavailableReason ?? "Screen capture unavailable.");
            return;
        }
        var settings = model.Settings;
        var learnReference = model.ReferenceRequested;
        if (learnReference && !game.IsForeground)
        {
            model.Pause("Return focus to Elite, stay stationary, and look forward to learn the HUD labels.");
            return;
        }
        if (!learnReference && settings.LabelTemplates is null)
        {
            model.Pause("Open Mining in the overlay editor, align all six circles, then select Test and save calibration.");
            return;
        }
        var bounds = settings.GetBounds(game.ClientBounds);
        busy = true;
        try
        {
            var result = await Task.Run(() =>
            {
                var pixels = capture.Capture(bounds);
                return learnReference
                    ? (Reference: MiningBarDetector.CaptureReference(pixels, settings), Analysis: MiningBarAnalysis.Unknown())
                    : (Reference: (byte[][]?)null, Analysis: MiningBarDetector.Analyze(pixels, settings));
            });
            if (!disposed && ReferenceEquals(settings, model.Settings)
                && (model.Enabled || model.IsCalibrating)
                && (!model.IsCalibrating || model.IsCalibrationTesting))
            {
                var current = tracker.GetSnapshot();
                if (current.IsAvailable && current.ClientBounds == game.ClientBounds && current.IsVisible
                    && (current.IsForeground || model.IsCalibrating && !learnReference) && mining.CanDetectRigs)
                {
                    if (result.Reference is not null) model.ApplyReference(result.Reference);
                    else model.Apply(result.Analysis);
                }
                else model.Pause("Waiting for Elite's Rhino cockpit view.");
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            if (!disposed) model.Pause("Rig detection paused: " + e.Message);
        }
        finally
        {
            busy = false;
            if (disposed) capture.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
        tracker.Dispose();
        if (!busy) capture.Dispose();
    }
}
