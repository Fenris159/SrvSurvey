using Avalonia.Threading;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
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
    private MiningBarAnalysis? previousAnalysis;
    private MiningDetectionSettings? previousSettings;
    private SystemSurfaceContext? previousContext;

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
        var context = mining.DetectionContext;
        if (!mining.ShouldShow || context != previousContext)
        {
            previousAnalysis = null;
            model.Pause("Waiting for Elite's Rhino cockpit view.");
        }
        if (!CanCapture(model, game)) return;
        var settings = model.Settings;
        var previous = ReferenceEquals(settings, previousSettings) ? previousAnalysis : null;
        var bounds = settings.GetBounds(game.ClientBounds);
        busy = true;
        try
        {
            var result = await Task.Run(() =>
            {
                var pixels = capture.Capture(bounds);
                return MiningBarDetector.Analyze(pixels, settings, previous);
            });
            await ApplyResultAsync(result, model, settings, context, game);
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

    private async Task ApplyResultAsync(MiningBarAnalysis result, MiningDetectionViewModel model,
        MiningDetectionSettings settings, SystemSurfaceContext? context, GameWindowSnapshot game)
    {
        if (disposed || context != mining.DetectionContext || !ReferenceEquals(settings, model.Settings)
            || !(model.Enabled || model.IsCalibrating) || model.IsCalibrating && !model.IsCalibrationTesting) return;
        var current = tracker.GetSnapshot();
        if (!current.IsAvailable || current.ClientBounds != game.ClientBounds || !current.IsVisible
            || !(current.IsForeground || model.IsCalibrating) || !mining.CanDetectRigs)
        {
            model.Pause("Waiting for Elite's Rhino cockpit view.");
            return;
        }
        // Status can change while pixels are being captured on the worker thread.
        if (!model.IsCalibrating && !mining.IsDetectionPositionSteady)
        {
            model.Pause(SurfaceMiningViewModel.DetectionMovementMessage);
            return;
        }
        previousAnalysis = result;
        previousSettings = settings;
        previousContext = context;
        var confirmed = model.Apply(result);
        if (CanApplyTrackers(context, current, model))
            await mining.ApplyDetectedRigsAsync(confirmed, context!, settings);
    }
    private static bool CanApplyTrackers(SystemSurfaceContext? context, GameWindowSnapshot current, MiningDetectionViewModel model) =>
        context is not null && current.IsForeground && model.Enabled && !model.IsCalibrating;

    private bool CanCapture(MiningDetectionViewModel model, GameWindowSnapshot game)
    {
        if (!(model.Enabled || model.IsCalibrating)) return false;
        if (model.IsCalibrating && !model.IsCalibrationTesting) return false;
        if (!game.IsAvailable || !game.IsVisible || (!model.IsCalibrating && !game.IsForeground)
            || !mining.CanDetectRigs)
        {
            model.Pause("Waiting for Elite's Rhino cockpit view.");
            return false;
        }
        if (!capture.IsAvailable)
        {
            model.Pause(capture.UnavailableReason ?? "Screen capture unavailable.");
            return false;
        }
        if (!model.IsCalibrating && !mining.IsDetectionPositionSteady)
        {
            model.Pause(SurfaceMiningViewModel.DetectionMovementMessage);
            return false;
        }
        return true;
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
