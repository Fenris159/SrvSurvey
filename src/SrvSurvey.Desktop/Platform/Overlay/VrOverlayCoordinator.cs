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
    private readonly Queue<VrOverlayPointerEvent> retainedPointerEvents = new();
    private readonly VrOverlayCalibrationSession calibrationSession = new();
    private IReadOnlyList<RegisteredOverlayWindow> calibrationRegistrations = [];
    private string? activeDragPlotter;
    private double dragGrabNormX;
    private double dragGrabNormY;
    private double lastAppliedDragNormX;
    private double lastAppliedDragNormY;
    private long lastDragApplyMs;
    private bool appliedDragInCurrentPollBatch;
    private bool sawDragMoveSinceLastApply;
    private double pendingDragDepthWheel;
    private float lastOpenVrYawOffset;
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
            if (viewModel.IsAdjusting)
            {
                viewModel.ReportAdjustmentStatus(result.Message);
            }

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
            retainedPointerEvents.Clear();
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
        if (result.Succeeded)
        {
            lastOpenVrYawOffset = 0f;
            if (runtime.TryGetHeadPose(out VrOverlayHeadPose? pose) && pose is not null)
            {
                lastOpenVrYawOffset = pose.OpenVrYawOffset;
            }
        }
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
        retainedPointerEvents.Clear();
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
        appliedDragInCurrentPollBatch = false;
        while (retainedPointerEvents.Count > 0)
        {
            DispatchPointerEvent(registrations, retainedPointerEvents.Dequeue());
        }

        appliedDragInCurrentPollBatch = false;
        foreach (VrOverlayPointerEvent pointerEvent in runtime.PollPointerEvents())
        {
            DispatchPointerEvent(registrations, pointerEvent);
        }
    }

    private void DispatchPointerEvent(
        Dictionary<string, RegisteredOverlayWindow> registrations,
        VrOverlayPointerEvent pointerEvent
    )
    {
        if (pointerEvent.Kind == VrOverlayPointerEventKind.Reset)
        {
            ClearDragState();
            inputRouter?.Reset();
            return;
        }

        if (registrations.TryGetValue(pointerEvent.PlotterName, out RegisteredOverlayWindow? registration))
        {
            if (TryHandleDragEvent(registration, pointerEvent))
            {
                return;
            }

            inputRouter?.Dispatch(registration, pointerEvent);
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
                HandleDragMove(registration.PlotterName, normX, normY, width, height, throttle: false);
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
        lastAppliedDragNormX = normX;
        lastAppliedDragNormY = normY;
        lastDragApplyMs = 0;
        appliedDragInCurrentPollBatch = false;
        sawDragMoveSinceLastApply = false;
        pendingDragDepthWheel = 0;
    }

    private void HandleDragMove(
        string plotterName,
        double normX,
        double normY,
        double width,
        double height,
        bool throttle = true
    )
    {
        bool isSameCoordinate =
            Math.Abs(normX - lastAppliedDragNormX) < 1e-6 && Math.Abs(normY - lastAppliedDragNormY) < 1e-6;
        if (throttle)
        {
            // Drop repeated coordinates from the same poll batch while allowing a later poll to hit the same UV on a moved panel.
            if (appliedDragInCurrentPollBatch && isSameCoordinate)
            {
                return;
            }

            sawDragMoveSinceLastApply = true;
        }
        else if (isSameCoordinate && (appliedDragInCurrentPollBatch || !sawDragMoveSinceLastApply))
        {
            // SteamVR reuses the last Move coordinate on LeftButtonUp when the pointer did not move again.
            return;
        }

        long now = clock();
        if (throttle && lastDragApplyMs > 0 && now - lastDragApplyMs < DragUpdateIntervalMs)
        {
            return;
        }

        VrOverlayCalibration? current = viewModel.GetCalibrationForAdjustment(plotterName);
        if (current is null)
        {
            return;
        }

        double originX = appliedDragInCurrentPollBatch ? lastAppliedDragNormX : dragGrabNormX;
        double originY = appliedDragInCurrentPollBatch ? lastAppliedDragNormY : dragGrabNormY;
        float deltaRight = (float)((normX - originX) * current.Scale);
        float deltaUp = (float)((originY - normY) * current.Scale * (height / width));
        if (MathF.Abs(deltaRight) < 0.005f && MathF.Abs(deltaUp) < 0.005f)
        {
            return;
        }

        bool usesLegacyOpenVr = !VrPlatformProfileCatalog.UsesSeatedDefaults(viewModel.SelectedPlatformProfile.Id);
        float openVrYawOffset = ResolveOpenVrYawOffset(usesLegacyOpenVr);
        VrOverlayCalibration moved = VrOverlayTransform.ApplyPlaneDrag(
            current,
            deltaRight,
            deltaUp,
            usesLegacyOpenVr,
            openVrYawOffset
        );
        lastDragApplyMs = now;
        lastAppliedDragNormX = normX;
        lastAppliedDragNormY = normY;
        appliedDragInCurrentPollBatch = true;
        sawDragMoveSinceLastApply = false;
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
        VrOverlayCalibration? current = viewModel.GetCalibrationForAdjustment(plotterName);
        if (current is null)
        {
            return;
        }

        bool usesLegacyOpenVr = !VrPlatformProfileCatalog.UsesSeatedDefaults(viewModel.SelectedPlatformProfile.Id);
        float openVrYawOffset = ResolveOpenVrYawOffset(usesLegacyOpenVr);
        VrOverlayCalibration moved = VrOverlayTransform.ApplyDepthStep(
            current,
            steps * (float)viewModel.PositionIncrement,
            usesLegacyOpenVr,
            openVrYawOffset
        );
        viewModel.ApplyCalibrationEdit(moved);
    }

    private float ResolveOpenVrYawOffset(bool usesLegacyOpenVr)
    {
        if (!usesLegacyOpenVr)
        {
            return 0f;
        }

        if (runtime.TryGetHeadPose(out VrOverlayHeadPose? pose) && pose is not null)
        {
            lastOpenVrYawOffset = pose.OpenVrYawOffset;
        }

        return lastOpenVrYawOffset;
    }

    private void ClearDragState()
    {
        activeDragPlotter = null;
        appliedDragInCurrentPollBatch = false;
        sawDragMoveSinceLastApply = false;
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
        if (viewModel.IsChangingRoute || viewModel.IsApplyingCalibrationBatch)
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

        bool reenableInteraction = PrepareReplacedSurfaceInput(registrations);
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
        if (reenableInteraction && IsInteractionEnabled && runtime.IsInitialized)
        {
            ReenableRuntimeInteractionAfterSurfaceReplacement();
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
            if (viewModel.IsAdjusting && capacity == 1 && !string.IsNullOrWhiteSpace(viewModel.SelectedOverlayName))
            {
                viewModel.ReportAdjustmentStatus(
                    "The active VR runtime layer limit only fits the calibration controls. Turn off controller interaction or free a composition layer to preview the selected overlay."
                );
            }
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

    private bool PrepareReplacedSurfaceInput(IReadOnlyList<RegisteredOverlayWindow> registrations)
    {
        HashSet<string>? replacedPlotters = FindReplacedPlotters(registrations);
        if (replacedPlotters is null)
        {
            return false;
        }

        if (activeDragPlotter is not null && replacedPlotters.Contains(activeDragPlotter))
        {
            ClearDragState();
        }

        foreach (string replaced in replacedPlotters)
        {
            inputRouter?.ResetPlotter(replaced);
            DiscardRetainedPointerEvents(replaced);
        }

        bool hasUnaffectedActiveGesture = activeDragPlotter is not null || inputRouter?.HasCapture == true;
        bool interactionActive = IsInteractionEnabled && runtime.IsInitialized;
        if (interactionActive)
        {
            RetainUnaffectedPendingPointerEvents(replacedPlotters);
        }

        // Retire each replaced surface before publishing its replacement so OpenXR runtimes
        // rotate its overlay ID even when the replacement keeps identical dimensions.
        foreach (string replaced in replacedPlotters)
        {
            runtime.RemoveOverlay(replaced);
        }

        return interactionActive && !hasUnaffectedActiveGesture && retainedPointerEvents.Count == 0;
    }

    private HashSet<string>? FindReplacedPlotters(IReadOnlyList<RegisteredOverlayWindow> registrations)
    {
        HashSet<string>? replacedPlotters = null;
        foreach (RegisteredOverlayWindow registration in registrations)
        {
            if (
                published.TryGetValue(registration.PlotterName, out RegisteredOverlayWindow? prior)
                && !IsSameSurface(prior, registration)
            )
            {
                replacedPlotters ??= new HashSet<string>(StringComparer.Ordinal);
                replacedPlotters.Add(registration.PlotterName);
            }
        }

        return replacedPlotters;
    }

    private void RetainUnaffectedPendingPointerEvents(HashSet<string> replacedPlotters)
    {
        foreach (VrOverlayPointerEvent pendingEvent in runtime.PollPointerEvents())
        {
            if (
                pendingEvent.Kind != VrOverlayPointerEventKind.Reset
                && replacedPlotters.Contains(pendingEvent.PlotterName)
            )
            {
                continue;
            }

            retainedPointerEvents.Enqueue(pendingEvent);
        }
    }

    private void ReenableRuntimeInteractionAfterSurfaceReplacement()
    {
        VrRuntimeResult reenable = runtime.SetInteractionEnabled(true);
        if (!reenable.Succeeded)
        {
            ResetInteractionLocally();
            viewModel.ReportAdjustmentStatus(reenable.Message);
        }
    }

    private void DiscardRetainedPointerEvents(string plotterName)
    {
        int count = retainedPointerEvents.Count;
        for (int index = 0; index < count; index++)
        {
            VrOverlayPointerEvent queued = retainedPointerEvents.Dequeue();
            if (
                queued.Kind == VrOverlayPointerEventKind.Reset
                || !string.Equals(queued.PlotterName, plotterName, StringComparison.Ordinal)
            )
            {
                retainedPointerEvents.Enqueue(queued);
            }
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
            if (string.Equals(activeDragPlotter, removed, StringComparison.Ordinal))
            {
                ClearDragState();
            }

            inputRouter?.ResetPlotter(removed);
            DiscardRetainedPointerEvents(removed);
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
        retainedPointerEvents.Clear();
        ClearDragState();
        inputRouter?.Reset();
    }

    private void ShutdownRuntime()
    {
        ResetInteractionLocally();
        lastOpenVrYawOffset = 0f;
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

        VrOverlayCalibration? calibration = ResolveRegistrationCalibration(registration, runtimeMode);
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

    private VrOverlayCalibration? ResolveRegistrationCalibration(
        RegisteredOverlayWindow registration,
        string? runtimeMode
    )
    {
        if (registration.PlotterName == VrOverlayCalibrationSession.ControlsPlotterName)
        {
            return calibrationSession.ControlsCalibration;
        }

        return calibrationSession.Owns(registration)
            ? viewModel.GetCalibrationForAdjustment(registration.PlotterName)
            : viewModel.GetCalibration(registration.PlotterName, runtimeMode);
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
