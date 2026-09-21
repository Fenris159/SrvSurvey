using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
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
    private readonly OverlayDispatcherTimer interactionTimer;
    private VrOverlayInputRouter? inputRouter;
    private readonly HashSet<string> published = new(StringComparer.Ordinal);
    private bool disposed;

    public VrOverlayCoordinator(
        VrOverlayViewModel viewModel,
        OverlayWindowRegistry? registry = null,
        IOpenVrRuntime? runtime = null,
        Func<string, bool>? processDetector = null,
        Func<string?>? modeProvider = null,
        VrOverlayInputRouter? inputRouter = null
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        this.runtime = runtime ?? new OpenVrRuntime();
        this.processDetector = processDetector ?? IsProcessRunning;
        this.modeProvider = modeProvider ?? (() => null);
        this.inputRouter = inputRouter;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.CalibrationChanged += OnCalibrationChanged;
        viewModel.ConnectionCheckRequested += OnConnectionCheckRequested;
        this.registry.Changed += OnRegistryChanged;
        timer = new OverlayDispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += OnTimerTick;
        timer.Start();
        interactionTimer = new OverlayDispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        interactionTimer.Tick += OnInteractionTimerTick;
        Synchronize();
    }

    public bool IsInteractionEnabled { get; private set; }

    public bool ToggleInteraction()
    {
        if (disposed || !viewModel.Enabled || !runtime.IsInitialized)
        {
            viewModel.SetRuntimeStatus("Enable and connect VR overlays before turning on controller interaction.");
            return false;
        }

        bool enabled = !IsInteractionEnabled;
        VrRuntimeResult result = runtime.SetInteractionEnabled(enabled);
        viewModel.SetRuntimeStatus(result.Message);
        if (!result.Succeeded)
        {
            return false;
        }

        IsInteractionEnabled = enabled;
        if (enabled)
        {
            inputRouter ??= new VrOverlayInputRouter();
            interactionTimer.Start();
        }
        else
        {
            interactionTimer.Stop();
            inputRouter?.Reset();
        }

        return true;
    }

    public bool ResetOrientation()
    {
        if (disposed || !viewModel.Enabled)
        {
            viewModel.SetRuntimeStatus("Enable OpenVR overlays before resetting headset orientation.");
            return false;
        }

        VrRuntimeResult result = runtime.ResetOrientation();
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
        interactionTimer.Stop();
        interactionTimer.Tick -= OnInteractionTimerTick;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.CalibrationChanged -= OnCalibrationChanged;
        viewModel.ConnectionCheckRequested -= OnConnectionCheckRequested;
        registry.Changed -= OnRegistryChanged;
        inputRouter?.Dispose();
        inputRouter = null;
        runtime.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnInteractionTimerTick(object? sender, EventArgs eventArgs)
    {
        PollInteractionEvents();
    }

    internal void PollInteractionEvents()
    {
        if (disposed || !IsInteractionEnabled)
        {
            return;
        }

        var registrations = registry
            .Snapshot()
            .Where(registration => registration.IsVisible)
            .ToDictionary(registration => registration.PlotterName, StringComparer.Ordinal);
        foreach (VrOverlayPointerEvent pointerEvent in runtime.PollPointerEvents())
        {
            if (registrations.TryGetValue(pointerEvent.PlotterName, out RegisteredOverlayWindow? registration))
            {
                inputRouter?.Dispatch(registration, pointerEvent);
            }
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnCalibrationChanged(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnConnectionCheckRequested(object? sender, EventArgs eventArgs)
    {
        Synchronize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            (eventArgs.PropertyName == nameof(VrOverlayViewModel.Enabled) && !viewModel.Enabled)
            || eventArgs.PropertyName
                is nameof(VrOverlayViewModel.Scale)
                    or nameof(VrOverlayViewModel.PositionX)
                    or nameof(VrOverlayViewModel.PositionY)
                    or nameof(VrOverlayViewModel.PositionZ)
                    or nameof(VrOverlayViewModel.RotationPitch)
                    or nameof(VrOverlayViewModel.RotationYaw)
                    or nameof(VrOverlayViewModel.RotationRoll)
        )
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

        IReadOnlyList<RegisteredOverlayWindow> registrations = registry.Snapshot();
        viewModel.SetCurrentRuntimeMode(modeProvider());
        if (!TryEnsureVrRuntimeReady())
        {
            return;
        }

        (HashSet<string>? active, string? lastError) = PublishRegistrations(registrations);
        RemoveStaleOverlays(active);
        published.Clear();
        published.UnionWith(active);
        if (lastError is null)
        {
            string interactionStatus = IsInteractionEnabled
                ? " Controller-pointer interaction is on; use its shortcut again to restore click-through."
                : string.Empty;
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.Ready,
                $"Connected through {viewModel.SelectedPlatformProfile.DisplayName}",
                $"SteamVR/OpenVR is active with {active.Count:N0} live overlays.{interactionStatus}"
            );
        }
        else
        {
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.Error,
                "The VR runtime rejected an overlay",
                lastError
            );
        }
    }

    private (HashSet<string> Active, string? LastError) PublishRegistrations(
        IReadOnlyList<RegisteredOverlayWindow> registrations
    )
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        string? lastError = null;
        foreach (RegisteredOverlayWindow registration in registrations)
        {
            if (TryPublishRegistration(registration, active, out string? error) && error is not null)
            {
                lastError = error;
            }
        }

        return (active, lastError);
    }

    private void RemoveStaleOverlays(HashSet<string> active)
    {
        foreach (string? removed in published.Except(active).ToArray())
        {
            runtime.RemoveOverlay(removed);
        }
    }

    private bool TryEnsureVrRuntimeReady()
    {
        if (!viewModel.Enabled)
        {
            published.Clear();
            ShutdownRuntime();
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.Disabled,
                "VR overlays are off",
                "Choose a connection route, complete its pairing steps, then enable VR overlays."
            );
            return false;
        }

        if (viewModel.IsCustomRuntime && !processDetector(viewModel.RuntimeProcessName))
        {
            published.Clear();
            ShutdownRuntime();
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.WaitingForRuntime,
                $"Waiting for {viewModel.SelectedPlatformProfile.DisplayName}",
                $"Start the OpenVR runtime process '{viewModel.RuntimeProcessName}'."
            );
            return false;
        }

        VrRuntimeProbe probe = runtime.Probe();
        if (!probe.RuntimeAvailable)
        {
            published.Clear();
            ShutdownRuntime();
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.WaitingForRuntime,
                "SteamVR/OpenVR is not available",
                probe.Message
            );
            return false;
        }

        if (!probe.HeadsetPresent)
        {
            published.Clear();
            ShutdownRuntime();
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.WaitingForRuntime,
                "Connect or wake the headset",
                probe.Message
            );
            return false;
        }

        if (!runtime.IsInitialized)
        {
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.Connecting,
                "SteamVR and headset detected",
                "Connecting SrvSurvey to the OpenVR overlay compositor."
            );
            VrRuntimeResult initialization = runtime.Initialize();
            if (!initialization.Succeeded)
            {
                viewModel.SetConnectionStatus(
                    VrOverlayConnectionState.Error,
                    "OpenVR connection failed",
                    initialization.Message
                );
                return false;
            }
        }

        return true;
    }

    private void ShutdownRuntime()
    {
        IsInteractionEnabled = false;
        interactionTimer.Stop();
        inputRouter?.Reset();
        runtime.Shutdown();
    }

    private bool TryPublishRegistration(RegisteredOverlayWindow registration, HashSet<string> active, out string? error)
    {
        error = null;
        if (!registration.IsVisible)
        {
            return false;
        }

        VrOverlayCalibration? calibration = viewModel.GetCalibration(registration.PlotterName, modeProvider());
        if (calibration is null)
        {
            return false;
        }

        try
        {
            Visual renderSource = registration.RenderSource;
            VrOverlayFrame frame = VrOverlayFrameRenderer.Render(
                renderSource,
                renderSource.Bounds.Size,
                registration.Window.RenderScaling
            );
            VrRuntimeResult result = runtime.PublishOverlay(
                registration.PlotterName,
                frame,
                calibration,
                (float)registration.Window.Opacity
            );
            if (result.Succeeded)
            {
                active.Add(registration.PlotterName);
                return false;
            }

            error = result.Message;
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            error = $"Could not render {registration.PlotterName} for VR: " + exception.Message;
            return true;
        }
    }

    private static bool IsProcessRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        try
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
