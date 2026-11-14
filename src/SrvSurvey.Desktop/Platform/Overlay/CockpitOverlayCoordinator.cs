using System.ComponentModel;
using System.Net.Sockets;
using System.Numerics;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Connects live cockpit transforms to the shared desktop host without affecting journal processing.</summary>
public sealed class CockpitOverlayCoordinator : IDisposable
{
    private readonly CockpitOverlayViewModel viewModel;
    private readonly ICockpitProjectionHost presentation;
    private readonly Func<bool> vrEnabled;
    private readonly IHostedOverlayTimer timer;
    private readonly Func<CockpitCameraClient> createClient;
    private CockpitCameraClient? client;
    private CockpitCameraFrame? currentFrame;
    private Matrix4x4? inverseNeutral;
    private bool disposed;

    /// <summary>Observes explicit opt-in; a disabled feature opens no camera socket and schedules no polling.</summary>
    public CockpitOverlayCoordinator(
        CockpitOverlayViewModel viewModel,
        OverlayPresentationSession presentation,
        Func<bool> vrEnabled
    )
        : this(viewModel, presentation, vrEnabled, () => new CockpitCameraClient(), new CameraRefreshTimer()) { }

    internal CockpitOverlayCoordinator(
        CockpitOverlayViewModel viewModel,
        ICockpitProjectionHost presentation,
        Func<bool> vrEnabled,
        Func<CockpitCameraClient> createClient,
        IHostedOverlayTimer timer
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        this.vrEnabled = vrEnabled ?? throw new ArgumentNullException(nameof(vrEnabled));
        this.createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
        this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
        timer.Tick += OnTick;
        viewModel.PropertyChanged += OnPreferenceChanged;
        viewModel.CalibrationRequested += OnCalibrationRequested;
        Synchronize();
    }

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CockpitOverlayViewModel.Enabled))
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        if (!viewModel.Enabled || viewModel.RestartRequired || !presentation.SupportsCockpitPanels)
        {
            Stop();
            if (viewModel.Enabled && !viewModel.RestartRequired)
            {
                viewModel.StatusMessage = "Cockpit panels need a supported shared desktop overlay host.";
            }
            return;
        }
        try
        {
            client ??= createClient();
            timer.Start();
        }
        catch (SocketException exception)
        {
            Stop();
            viewModel.StatusMessage =
                $"The local camera connection is unavailable ({exception.SocketErrorCode}). Desktop positions remain active.";
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (vrEnabled())
        {
            ResetCalibration();
            viewModel.StatusMessage = "Headset overlays are active; desktop cockpit tracking is paused.";
            return;
        }
        CockpitCameraFrame? frame = client?.Poll();
        if (frame is null)
        {
            ResetCalibration();
            viewModel.StatusMessage = "Waiting for the live cockpit camera bridge. Desktop positions remain active.";
            return;
        }
        if (currentFrame is { } previous && frame.Sequence < previous.Sequence)
        {
            ResetCalibration();
        }
        currentFrame = frame;
        viewModel.CanCalibrate = true;
        if (inverseNeutral is { } neutral)
        {
            presentation.SetCockpitProjection(neutral * frame.Transform, viewModel.Resolve, viewModel.CalibratedLens);
            viewModel.StatusMessage = "Live cockpit camera connected. Panels follow normal freelook.";
        }
        else
        {
            viewModel.StatusMessage = "Live camera connected. Center freelook, then select Calibrate centered view.";
        }
    }

    private void OnCalibrationRequested(object? sender, EventArgs e)
    {
        if (
            client?.Poll() is not { } frame
            || !Matrix4x4.Invert(frame.Transform, out Matrix4x4 inverse)
            || CockpitProjectionLens.FromRigidProjection(frame.Transform) is not { } lens
        )
        {
            viewModel.StatusMessage = "A current cockpit camera sample is needed before calibration.";
            return;
        }
        currentFrame = frame;
        inverseNeutral = inverse;
        viewModel.CalibratedLens = lens;
        presentation.SetCockpitProjection(Matrix4x4.Identity, viewModel.Resolve, lens);
        viewModel.StatusMessage = "Centered view calibrated. Use the position editor to adjust each panel's angle.";
    }

    private void ResetCalibration()
    {
        currentFrame = null;
        viewModel.CanCalibrate = false;
        inverseNeutral = null;
        viewModel.CalibratedLens = null;
        presentation.SetCockpitProjection(null, null);
    }

    private void Stop()
    {
        timer.Stop();
        client?.Dispose();
        client = null;
        ResetCalibration();
    }

    /// <summary>Releases native opt-in and restores the ordinary desktop layout before presentation is disposed.</summary>
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            viewModel.PropertyChanged -= OnPreferenceChanged;
            viewModel.CalibrationRequested -= OnCalibrationRequested;
            timer.Tick -= OnTick;
            Stop();
            timer.Dispose();
        }
    }

    /// <summary>Uses a dedicated camera cadence rather than the shared low-frequency panel-data scheduler.</summary>
    private sealed class CameraRefreshTimer : IHostedOverlayTimer
    {
        private readonly DispatcherTimer dispatcher = new() { Interval = TimeSpan.FromMilliseconds(16) };
        public event EventHandler? Tick
        {
            add => dispatcher.Tick += value;
            remove => dispatcher.Tick -= value;
        }

        public void Start() => dispatcher.Start();

        public void Stop() => dispatcher.Stop();

        public void Dispose() => dispatcher.Stop();
    }
}

/// <summary>The native desktop presentation boundary used by live camera tracking.</summary>
internal interface ICockpitProjectionHost
{
    bool SupportsCockpitPanels { get; }
    void SetCockpitProjection(
        Matrix4x4? cameraChange,
        Func<string, CockpitPanelPose>? poseProvider,
        CockpitProjectionLens? lens = null
    );
}
