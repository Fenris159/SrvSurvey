using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class VrOverlayCoordinator : IDisposable
{
    private readonly VrOverlayViewModel viewModel;
    private readonly OverlayWindowRegistry registry;
    private readonly IOpenVrRuntime runtime;
    private readonly Func<string, bool> processDetector;
    private readonly Func<string?> modeProvider;
    private readonly OverlayDispatcherTimer timer;
    private readonly HashSet<string> published = new(StringComparer.Ordinal);
    private bool disposed;

    public VrOverlayCoordinator(
        VrOverlayViewModel viewModel,
        OverlayWindowRegistry? registry = null,
        IOpenVrRuntime? runtime = null,
        Func<string, bool>? processDetector = null,
        Func<string?>? modeProvider = null)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        this.runtime = runtime ?? new OpenVrRuntime();
        this.processDetector = processDetector ?? IsProcessRunning;
        this.modeProvider = modeProvider ?? (() => null);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.CalibrationChanged += OnCalibrationChanged;
        this.registry.Changed += OnRegistryChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        Synchronize();
    }

    public bool ResetOrientation()
    {
        if (disposed || !viewModel.Enabled)
        {
            viewModel.SetRuntimeStatus(
                "Enable OpenVR overlays before resetting headset orientation.");
            return false;
        }

        var result = runtime.ResetOrientation();
        viewModel.SetRuntimeStatus(result.Message);
        return result.Succeeded;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.CalibrationChanged -= OnCalibrationChanged;
        registry.Changed -= OnRegistryChanged;
        runtime.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnCalibrationChanged(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(VrOverlayViewModel.Enabled)
            or nameof(VrOverlayViewModel.RuntimeProcessName)
            or nameof(VrOverlayViewModel.Scale)
            or nameof(VrOverlayViewModel.PositionX)
            or nameof(VrOverlayViewModel.PositionY)
            or nameof(VrOverlayViewModel.PositionZ)
            or nameof(VrOverlayViewModel.RotationPitch)
            or nameof(VrOverlayViewModel.RotationYaw)
            or nameof(VrOverlayViewModel.RotationRoll))
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        if (disposed)
        {
            return;
        }

        var registrations = registry.Snapshot();
        viewModel.SetCurrentRuntimeMode(modeProvider());
        if (!viewModel.Enabled)
        {
            published.Clear();
            runtime.Shutdown();
            return;
        }

        if (!processDetector(viewModel.RuntimeProcessName))
        {
            published.Clear();
            runtime.Shutdown();
            viewModel.SetRuntimeStatus(
                $"Waiting for VR process '{viewModel.RuntimeProcessName}'.");
            return;
        }

        if (!runtime.IsInitialized)
        {
            var initialization = runtime.Initialize();
            if (!initialization.Succeeded)
            {
                viewModel.SetRuntimeStatus(initialization.Message);
                return;
            }
        }

        var active = new HashSet<string>(StringComparer.Ordinal);
        string? lastError = null;
        foreach (var registration in registrations)
        {
            if (!registration.IsVisible)
            {
                continue;
            }

            var calibration = viewModel.GetCalibration(
                registration.PlotterName,
                modeProvider());
            if (calibration is null)
            {
                continue;
            }

            try
            {
                var renderSource = registration.RenderSource;
                var frame = VrOverlayFrameRenderer.Render(
                    renderSource,
                    renderSource.Bounds.Size,
                    registration.Window.RenderScaling);
                var result = runtime.PublishOverlay(
                    registration.PlotterName,
                    frame,
                    calibration,
                    (float)registration.Window.Opacity);
                if (result.Succeeded)
                {
                    active.Add(registration.PlotterName);
                }
                else
                {
                    lastError = result.Message;
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidDataException
                    or InvalidOperationException
                    or OverflowException)
            {
                lastError = $"Could not render {registration.PlotterName} for VR: "
                    + exception.Message;
            }
        }

        foreach (var removed in published.Except(active).ToArray())
        {
            runtime.RemoveOverlay(removed);
        }

        published.Clear();
        published.UnionWith(active);
        viewModel.SetRuntimeStatus(lastError
            ?? $"OpenVR is active with {active.Count:N0} live overlays.");
    }

    private static bool IsProcessRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
