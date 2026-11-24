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
    private readonly Dictionary<string, RegisteredOverlayWindow> published = new(StringComparer.Ordinal);
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
            ClearDragState();
            inputRouter?.Reset();
        }

        Synchronize();
        viewModel.SetRuntimeStatus(result.Message);
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
        ClearDragState();
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
        if (!CanHandleDragEvent(registration))
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
        bool isActiveTarget = string.Equals(activeDragPlotter, registration.PlotterName, StringComparison.Ordinal);
        switch (pointerEvent.Kind)
        {
            case VrOverlayPointerEventKind.LeftButtonDown:
                BeginDrag(registration.PlotterName, normX, normY);
                return true;
            case VrOverlayPointerEventKind.Move when isActiveTarget:
                HandleDragMove(registration.PlotterName, normX, normY, width, height);
                return true;
            case VrOverlayPointerEventKind.Wheel when isActiveTarget:
                HandleDragDepth(registration.PlotterName, pointerEvent.WheelY);
                return true;
            case VrOverlayPointerEventKind.LeftButtonUp when isActiveTarget:
                ClearDragState();
                return true;
            default:
                return true;
        }
    }

    private bool CanHandleDragEvent(RegisteredOverlayWindow registration)
    {
        return viewModel.IsAdjusting
            && viewModel.IsDragModeEnabled
            && !string.Equals(
                registration.PlotterName,
                VrOverlayCalibrationSession.ControlsPlotterName,
                StringComparison.Ordinal
            )
            && calibrationSession.Owns(registration);
    }

    private void BeginDrag(string plotterName, double normX, double normY)
    {
        if (!string.Equals(viewModel.SelectedOverlayName, plotterName, StringComparison.Ordinal))
        {
            viewModel.SelectedOverlayName = plotterName;
        }

        activeDragPlotter = plotterName;
        dragGrabNormX = normX;
        dragGrabNormY = normY;
        lastDragApplyMs = 0;
        pendingDragDepthWheel = 0;
    }

    private void HandleDragMove(string plotterName, double normX, double normY, double width, double height)
    {
        long now = clock();
        if (lastDragApplyMs > 0 && now - lastDragApplyMs < DragUpdateIntervalMs)
        {
            return;
        }

        VrOverlayCalibration? current = viewModel.GetCalibration(plotterName, modeProvider());
        if (current is null)
        {
            return;
        }

        float deltaRight = (float)((normX - dragGrabNormX) * current.Scale);
        float deltaUp = (float)((dragGrabNormY - normY) * current.Scale * (height / width));
        if (MathF.Abs(deltaRight) < 0.005f && MathF.Abs(deltaUp) < 0.005f)
        {
            return;
        }

        bool usesLegacyOpenVr = !VrPlatformProfileCatalog.UsesSeatedDefaults(viewModel.SelectedPlatformProfile.Id);
        VrOverlayCalibration moved = VrOverlayTransform.ApplyPlaneDrag(current, deltaRight, deltaUp, usesLegacyOpenVr);
        lastDragApplyMs = now;
        viewModel.ApplyCalibrationEdit(moved);
    }

    private void HandleDragDepth(string plotterName, double wheelY)
    {
        if (Math.Abs(wheelY) < 0.0001)
        {
            return;
        }

        if (Math.Abs(pendingDragDepthWheel) > 0.0001 && Math.Sign(wheelY) != Math.Sign(pendingDragDepthWheel))
        {
            pendingDragDepthWheel = 0;
        }

        pendingDragDepthWheel += wheelY;
        int steps = (int)Math.Truncate(pendingDragDepthWheel);
        if (steps == 0)
        {
            return;
        }

        pendingDragDepthWheel -= steps;
        VrOverlayCalibration? current = viewModel.GetCalibration(plotterName, modeProvider());
        if (current is null)
        {
            return;
        }

        bool usesLegacyOpenVr = !VrPlatformProfileCatalog.UsesSeatedDefaults(viewModel.SelectedPlatformProfile.Id);
        VrOverlayCalibration moved = VrOverlayTransform.ApplyDepthStep(
            current,
            steps * (float)viewModel.PositionIncrement,
            usesLegacyOpenVr
        );
        viewModel.ApplyCalibrationEdit(moved);
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
            if (
                !viewModel.IsAdjusting
                || !viewModel.IsDragModeEnabled
                || eventArgs.PropertyName
                    is nameof(VrOverlayViewModel.SelectedOverlayName)
                        or nameof(VrOverlayViewModel.SelectedCategory)
            )
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
        int capacity = Math.Max(0, runtime.MaximumOverlays);
        calibrationRegistrations = calibrationSession.Refresh(viewModel, pose, capacity);
        // Reserve transport capacity for the calibration controls and selected sample.
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

        InvalidateReplacedSurfaceInput(registrations);
        (Dictionary<string, RegisteredOverlayWindow> active, string? lastError) = PublishRegistrations(
            registrations,
            runtimeMode
        );
        RemoveStaleOverlays(active);
        VrRuntimeResult completion = runtime.CompleteFrame();
        if (!completion.Succeeded)
        {
            lastError = completion.Message;
        }
        published.Clear();
        foreach ((string name, RegisteredOverlayWindow registration) in active)
        {
            published[name] = registration;
        }
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

    private void InvalidateReplacedSurfaceInput(IReadOnlyList<RegisteredOverlayWindow> registrations)
    {
        bool surfaceReplaced = registrations.Any(registration =>
            published.TryGetValue(registration.PlotterName, out RegisteredOverlayWindow? prior)
            && !IsSameSurface(prior, registration)
        );
        if (!surfaceReplaced)
        {
            return;
        }

        ClearDragState();
        inputRouter?.Reset();
        if (IsInteractionEnabled && runtime.IsInitialized)
        {
            _ = runtime.PollPointerEvents();
            _ = runtime.SetInteractionEnabled(true);
        }
    }

    private static bool IsSameSurface(RegisteredOverlayWindow prior, RegisteredOverlayWindow current) =>
        ReferenceEquals(prior.Window, current.Window) && ReferenceEquals(prior.RenderSource, current.RenderSource);

    private (Dictionary<string, RegisteredOverlayWindow> Active, string? LastError) PublishRegistrations(
        IReadOnlyList<RegisteredOverlayWindow> registrations,
        string? runtimeMode
    )
    {
        var active = new Dictionary<string, RegisteredOverlayWindow>(StringComparer.Ordinal);
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

    private void RemoveStaleOverlays(Dictionary<string, RegisteredOverlayWindow> active)
    {
        foreach (string removed in published.Keys.Where(name => !active.ContainsKey(name)).ToArray())
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
            ResetInteractionLocally();
            if (runtime.IsInitialized)
            {
                runtime.Shutdown();
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

    private void ResetInteractionLocally()
    {
        IsInteractionEnabled = false;
        interactionTimer.Stop();
        ClearDragState();
        inputRouter?.Reset();
    }

    private void ShutdownRuntime()
    {
        ResetInteractionLocally();
        runtime.Shutdown();
    }

    private bool TryPublishRegistration(
        RegisteredOverlayWindow registration,
        Dictionary<string, RegisteredOverlayWindow> active,
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
                active[registration.PlotterName] = registration;
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
