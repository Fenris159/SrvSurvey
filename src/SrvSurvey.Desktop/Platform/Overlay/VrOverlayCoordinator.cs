using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class VrOverlayCoordinator : IDisposable
{
    private const long DragUpdateIntervalMs = 32;
    private readonly VrOverlayViewModel viewModel;
    private readonly OverlayWindowRegistry registry;
    private readonly IVrOverlayRuntime runtime;
    private readonly Func<string, bool> processDetector;
    private readonly Func<string?> modeProvider;
    private readonly Func<long> clock;
    private readonly OverlayDispatcherTimer timer;
    private readonly OverlayDispatcherTimer interactionTimer;
    private VrOverlayInputRouter? inputRouter;
    private readonly HashSet<string> published = new(StringComparer.Ordinal);
    private readonly VrOverlayCalibrationSession calibrationSession = new();
    private IReadOnlyList<RegisteredOverlayWindow> calibrationRegistrations = [];
    private string? activeDragPlotter;
    private double dragGrabNormX;
    private double dragGrabNormY;
    private long lastDragApplyMs;
    private double pendingDragDepthWheel;
    private bool synchronizing;
    private bool disposed;

    public VrOverlayCoordinator(
        VrOverlayViewModel viewModel,
        OverlayWindowRegistry? registry = null,
        IVrOverlayRuntime? runtime = null,
        Func<string, bool>? processDetector = null,
        Func<string?>? modeProvider = null,
        VrOverlayInputRouter? inputRouter = null,
        Func<long>? clock = null
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        this.runtime =
            runtime
            ?? new VrOverlayRuntimeRouter(
                () => viewModel.SelectedPlatformProfile.Id,
                edvrControllerInput: () => viewModel.EdvrControllerInput,
                isCustomRuntime: () => viewModel.IsCustomRuntime
            );
        this.processDetector = processDetector ?? IsProcessRunning;
        this.modeProvider = modeProvider ?? (() => null);
        this.inputRouter = inputRouter;
        this.clock = clock ?? (() => Environment.TickCount64);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.CalibrationChanged += OnCalibrationChanged;
        viewModel.ConnectionCheckRequested += OnConnectionCheckRequested;
        viewModel.PlaceInFrontRequested += OnPlaceInFrontRequested;
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
            viewModel.SetRuntimeStatus("Enable VR overlays before resetting headset orientation.");
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
        viewModel.PlaceInFrontRequested -= OnPlaceInFrontRequested;
        registry.Changed -= OnRegistryChanged;
        inputRouter?.Dispose();
        inputRouter = null;
        calibrationSession.Dispose();
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
            .Concat(calibrationRegistrations)
            .Where(registration => registration.IsVisible)
            .GroupBy(registration => registration.PlotterName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (VrOverlayPointerEvent pointerEvent in runtime.PollPointerEvents())
        {
            if (pointerEvent.Kind == VrOverlayPointerEventKind.Reset)
            {
                ClearDragState();
                inputRouter?.Reset();
                continue;
            }
            if (registrations.TryGetValue(pointerEvent.PlotterName, out RegisteredOverlayWindow? registration))
            {
                if (TryHandleDragEvent(registration, pointerEvent))
                {
                    continue;
                }
                inputRouter?.Dispatch(registration, pointerEvent);
            }
        }
    }

    private bool TryHandleDragEvent(RegisteredOverlayWindow registration, VrOverlayPointerEvent pointerEvent)
    {
        if (
            !viewModel.IsAdjusting
            || !viewModel.IsDragModeEnabled
            || string.Equals(
                registration.PlotterName,
                VrOverlayCalibrationSession.ControlsPlotterName,
                StringComparison.Ordinal
            )
            || !calibrationSession.Owns(registration)
        )
        {
            return false;
        }

        double width = registration.RenderSource.Bounds.Width;
        double height = registration.RenderSource.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        double normX = pointerEvent.X / width;
        double normY = pointerEvent.Y / height;
        switch (pointerEvent.Kind)
        {
            case VrOverlayPointerEventKind.LeftButtonDown:
                if (!string.Equals(viewModel.SelectedOverlayName, registration.PlotterName, StringComparison.Ordinal))
                {
                    viewModel.SelectedOverlayName = registration.PlotterName;
                }
                activeDragPlotter = registration.PlotterName;
                dragGrabNormX = normX;
                dragGrabNormY = normY;
                lastDragApplyMs = 0;
                pendingDragDepthWheel = 0;
                return true;
            case VrOverlayPointerEventKind.Move
                when string.Equals(activeDragPlotter, registration.PlotterName, StringComparison.Ordinal):
            {
                long now = clock();
                if (lastDragApplyMs > 0 && now - lastDragApplyMs < DragUpdateIntervalMs)
                {
                    return true;
                }

                VrOverlayCalibration? current = viewModel.GetCalibration(registration.PlotterName, modeProvider());
                if (current is null)
                {
                    return true;
                }

                float deltaRight = (float)((normX - dragGrabNormX) * current.Scale);
                float deltaUp = (float)((dragGrabNormY - normY) * current.Scale * (height / width));
                if (MathF.Abs(deltaRight) < 0.005f && MathF.Abs(deltaUp) < 0.005f)
                {
                    return true;
                }

                bool usesLegacyOpenVr = !VrPlatformProfileCatalog.UsesSeatedDefaults(
                    viewModel.SelectedPlatformProfile.Id
                );
                VrOverlayCalibration moved = VrOverlayTransform.ApplyPlaneDrag(
                    current,
                    deltaRight,
                    deltaUp,
                    usesLegacyOpenVr
                );
                lastDragApplyMs = now;
                viewModel.ApplyCalibrationEdit(moved);
                return true;
            }
            case VrOverlayPointerEventKind.Wheel
                when string.Equals(activeDragPlotter, registration.PlotterName, StringComparison.Ordinal):
            {
                if (pointerEvent.WheelY == 0)
                {
                    return true;
                }
                if (pendingDragDepthWheel != 0 && Math.Sign(pointerEvent.WheelY) != Math.Sign(pendingDragDepthWheel))
                {
                    pendingDragDepthWheel = 0;
                }
                pendingDragDepthWheel += pointerEvent.WheelY;
                int steps = (int)Math.Truncate(pendingDragDepthWheel);
                if (steps != 0)
                {
                    pendingDragDepthWheel -= steps;
                    viewModel.PositionZ = Math.Clamp(
                        viewModel.PositionZ + steps * (double)viewModel.PositionIncrement,
                        -1000,
                        1000
                    );
                }
                return true;
            }
            case VrOverlayPointerEventKind.LeftButtonUp
                when string.Equals(activeDragPlotter, registration.PlotterName, StringComparison.Ordinal):
                ClearDragState();
                return true;
            default:
                return true;
        }
    }

    private void ClearDragState()
    {
        activeDragPlotter = null;
        pendingDragDepthWheel = 0;
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

    private void OnPlaceInFrontRequested(object? sender, EventArgs eventArgs)
    {
        if (viewModel.Enabled && runtime.IsInitialized && runtime.TryGetHeadPose(out VrOverlayHeadPose? pose))
        {
            viewModel.PlaceInFront(pose!);
        }
        else
        {
            viewModel.ReportAdjustmentStatus(
                "Connect the headset and wait for a tracked head pose before placing this panel. VDXR requires the matching OpenXR layer."
            );
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(VrOverlayViewModel.EdvrControllerInput) && IsInteractionEnabled)
        {
            ToggleInteraction();
        }
        if (eventArgs.PropertyName == nameof(VrOverlayViewModel.SelectedPlatformProfile))
        {
            published.Clear();
            ShutdownRuntime();
            calibrationSession.Dispose();
            calibrationRegistrations = [];
        }
        if (viewModel.IsChangingRoute)
        {
            return;
        }
        if (
            eventArgs.PropertyName
            is nameof(VrOverlayViewModel.IsAdjusting)
                or nameof(VrOverlayViewModel.SelectedOverlayName)
                or nameof(VrOverlayViewModel.SelectedCategory)
                or nameof(VrOverlayViewModel.IsDragModeEnabled)
        )
        {
            if (!viewModel.IsAdjusting || !viewModel.IsDragModeEnabled)
            {
                ClearDragState();
            }
            inputRouter?.Reset();
        }

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
                    or nameof(VrOverlayViewModel.IsAdjusting)
                    or nameof(VrOverlayViewModel.SelectedCategory)
                    or nameof(VrOverlayViewModel.SelectedOverlayName)
                    or nameof(VrOverlayViewModel.SelectedMode)
        )
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        if (disposed || synchronizing)
        {
            return;
        }

        synchronizing = true;
        try
        {
            SynchronizeCore();
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void SynchronizeCore()
    {
        IReadOnlyList<RegisteredOverlayWindow> registrations = registry.Snapshot();
        string? runtimeMode = modeProvider();
        viewModel.SetCurrentRuntimeMode(runtimeMode);
        if (!TryEnsureVrRuntimeReady())
        {
            calibrationSession.Dispose();
            calibrationRegistrations = [];
            return;
        }

        VrOverlayHeadPose? pose = null;
        if (viewModel.IsAdjusting && inputRouter?.HasCapture != true && activeDragPlotter is null)
        {
            runtime.TryGetHeadPose(out pose);
        }
        calibrationRegistrations = calibrationSession.Refresh(viewModel, pose);
        // Reserve transport capacity for the calibration controls and selected sample.
        int capacity = Math.Max(0, runtime.MaximumOverlays);
        int liveCapacity = Math.Max(0, capacity - calibrationRegistrations.Count);
        registrations = registrations
            .Where(registration =>
                registration.IsVisible
                && !calibrationRegistrations.Any(sample => sample.PlotterName == registration.PlotterName)
                && viewModel.GetCalibration(registration.PlotterName, runtimeMode) is not null
            )
            .Take(liveCapacity)
            .Concat(calibrationRegistrations)
            .Take(capacity)
            .ToArray();

        (HashSet<string>? active, string? lastError) = PublishRegistrations(registrations, runtimeMode);
        RemoveStaleOverlays(active);
        VrRuntimeResult completion = runtime.CompleteFrame();
        if (!completion.Succeeded)
        {
            lastError = completion.Message;
        }
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
                $"{runtime.DisplayName} is active with {active.Count:N0} live overlays.{interactionStatus}"
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
        IReadOnlyList<RegisteredOverlayWindow> registrations,
        string? runtimeMode
    )
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        string? lastError = null;
        foreach (RegisteredOverlayWindow registration in registrations)
        {
            if (TryPublishRegistration(registration, active, runtimeMode, out string? error) && error is not null)
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
                "The selected VR transport is not available",
                probe.Message
            );
            return false;
        }

        if (!probe.HeadsetPresent)
        {
            published.Clear();
            if (runtime.IsInitialized)
            {
                ShutdownRuntime();
            }

            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.WaitingForRuntime,
                "Waiting for the selected VR session",
                probe.Message
            );
            return false;
        }

        if (!runtime.IsInitialized)
        {
            viewModel.SetConnectionStatus(
                VrOverlayConnectionState.Connecting,
                "VR session detected",
                "Connecting SrvSurvey to the selected overlay transport."
            );
            VrRuntimeResult initialization = runtime.Initialize();
            if (!initialization.Succeeded)
            {
                viewModel.SetConnectionStatus(
                    VrOverlayConnectionState.Error,
                    "VR connection failed",
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

    private bool TryPublishRegistration(
        RegisteredOverlayWindow registration,
        HashSet<string> active,
        string? runtimeMode,
        out string? error
    )
    {
        error = null;
        if (!registration.IsVisible)
        {
            return false;
        }

        VrOverlayCalibration? calibration =
            registration.PlotterName == VrOverlayCalibrationSession.ControlsPlotterName
                ? calibrationSession.ControlsCalibration
                : viewModel.GetCalibration(registration.PlotterName, runtimeMode);
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
                calibrationSession.Owns(registration) || registration.PresentationVisual is null
                    ? 1f
                    : (float)registration.Window.Opacity
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
