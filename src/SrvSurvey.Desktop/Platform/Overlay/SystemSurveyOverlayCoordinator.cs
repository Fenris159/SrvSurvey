using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class SystemSurveyOverlayCoordinator : IDisposable
{
    private const int DefaultMargin = 20;

    private readonly SystemSurveyViewModel survey;
    private readonly SurfaceSurveyViewModel surfaceSurvey;
    private readonly SystemSurveyOverlayViewModel viewModel;
    private readonly SurfaceSurveyOverlayViewModel surfaceViewModel;
    private readonly PriorScansOverlayViewModel priorScansViewModel;
    private readonly IGameScreenCapture gameScreenCapture;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayWindowRegistry windowRegistry;
    private readonly CachingCanonnSystemPoiClient canonnSystemPoiClient;
    private readonly Func<string?> commanderNameProvider;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "An in-flight Canonn refresh may release this gate after disposal begins."
    )]
    private readonly SemaphoreSlim canonnRefreshLock = new(1, 1);
    private readonly SemaphoreSlim fssCaptureLock = new(1, 1);
    private readonly CancellationTokenSource disposalCancellation = new();
    private readonly string? fssDiagnosticDirectory;
    private readonly HostedOverlayWindow bodyInfoWindow;
    private readonly HostedOverlayWindow fssWindow;
    private readonly HostedOverlayWindow lastFssBodyWindow;
    private readonly HostedOverlayWindow statusWindow;
    private readonly HostedOverlayWindow flightWarningWindow;
    private readonly HostedOverlayWindow biologyWindow;
    private readonly HostedOverlayWindow biologyStatusWindow;
    private readonly HostedOverlayWindow priorScansWindow;
    private readonly HostedOverlayWindow surfaceWindow;
    private readonly HostedOverlayWindow miniTrackWindow;
    private readonly HostedOverlayWindow[] hostedWindows;
    private bool isSuppressed;
    private string? canonnLoadedKey;
    private string? canonnFailedKey;
    private DateTimeOffset canonnRetryAfter;
    private long? fssDiagnosticRevision;
    private bool disposed;

    public SystemSurveyOverlayCoordinator(
        SystemSurveyViewModel survey,
        SurfaceSurveyViewModel surfaceSurvey,
        OverlayPresentationSession presentationSession,
        SystemSurveyOverlayCoordinatorOptions? options = null
    )
    {
        options ??= new SystemSurveyOverlayCoordinatorOptions();
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.surfaceSurvey = surfaceSurvey ?? throw new ArgumentNullException(nameof(surfaceSurvey));
        ArgumentNullException.ThrowIfNull(presentationSession);
        this.gameScreenCapture =
            options.GameScreenCapture
            ?? GameScreenCapture.CreateCurrent(
                enableWaylandPortalFallback: true,
                capturePurpose: "FSS tuning detection",
                waylandFeature: WaylandCaptureFeatures.FssTuning
            );
        this.fssDiagnosticDirectory = string.IsNullOrWhiteSpace(options.FssDiagnosticDirectory)
            ? null
            : options.FssDiagnosticDirectory;
        this.overlayLayout = presentationSession.OverlayLayout;
        this.windowRegistry = presentationSession.WindowRegistry;
        this.commanderNameProvider = options.CommanderNameProvider ?? (() => null);
        this.canonnSystemPoiClient = new CachingCanonnSystemPoiClient(
            options.CanonnSystemPoiClient ?? new CanonnSystemPoiClient()
        );
        bodyInfoWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotBodyInfo",
                CreateBodyInfoWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.TopLeft(gameBounds, windowSize, DefaultMargin)
            ) with
            {
                Tick = OnTick,
            }
        );
        fssWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotFSSInfo",
                CreateFssWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.TopLeft(gameBounds, windowSize, DefaultMargin)
            )
        );
        lastFssBodyWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotFSS",
                CreateLastFssBodyWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize, DefaultMargin)
            )
        );
        statusWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotSysStatus",
                CreateStatusWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.BottomLeft(gameBounds, windowSize, DefaultMargin)
            )
        );
        flightWarningWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotFlightWarning",
                CreateFlightWarningWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize, DefaultMargin)
            )
        );
        biologyWindow = presentationSession.HostPassiveWindow(
            CreateDefinition("PlotBioSystem", CreateBiologyWindow, PlaceBiologyWindow)
        );
        biologyStatusWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotBioStatus",
                CreateBiologyStatusWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize, DefaultMargin)
            )
        );
        priorScansWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotPriorScans",
                CreatePriorScansWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.BottomRight(gameBounds, windowSize, DefaultMargin)
            )
        );
        surfaceWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotGrounded",
                CreateSurfaceWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.BottomCenter(gameBounds, windowSize, DefaultMargin)
            ) with
            {
                ConfigureWindow = ApplySurfaceWindowSize,
                Placement = PlaceSurfaceWindow,
            }
        );
        miniTrackWindow = presentationSession.HostPassiveWindow(
            CreateDefinition(
                "PlotMiniTrack",
                CreateMiniTrackWindow,
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize, 8)
            )
        );
        hostedWindows =
        [
            bodyInfoWindow,
            fssWindow,
            lastFssBodyWindow,
            statusWindow,
            flightWarningWindow,
            biologyWindow,
            biologyStatusWindow,
            priorScansWindow,
            surfaceWindow,
            miniTrackWindow,
        ];
        OverlayPlatformCapabilities capabilities = bodyInfoWindow.Capabilities;
        viewModel = new SystemSurveyOverlayViewModel(survey, capabilities);
        surfaceViewModel = new SurfaceSurveyOverlayViewModel(surfaceSurvey, capabilities);
        priorScansViewModel = new PriorScansOverlayViewModel(
            survey,
            this.canonnSystemPoiClient,
            options.ExobiologyCatalog ?? ExobiologyReferenceCatalog.LoadEmbedded(),
            this.commanderNameProvider,
            capabilities,
            () => surfaceSurvey.CurrentSurface
        );
        foreach (HostedOverlayWindow hosted in hostedWindows)
        {
            hosted.VisibilityChanged += OnHostedVisibilityChanged;
        }

        survey.PropertyChanged += OnSurveyPropertyChanged;
        surfaceSurvey.PropertyChanged += OnSurfaceSurveyPropertyChanged;
        priorScansViewModel.PropertyChanged += OnPriorScansPropertyChanged;
        ApplyFssCaptureCapabilityStatus();
        SynchronizeIntent();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => hostedWindows.Any(hosted => hosted.IsVisible);

    public bool IsFssVisible => fssWindow.IsVisible;

    public bool IsLastFssBodyVisible => lastFssBodyWindow.IsVisible;

    public bool IsBodyInfoVisible => bodyInfoWindow.IsVisible;

    public bool IsFlightWarningVisible => flightWarningWindow.IsVisible;

    public bool IsBiologyVisible => biologyWindow.IsVisible;

    public bool IsBiologyStatusVisible => biologyStatusWindow.IsVisible;

    public bool IsPriorScansVisible => priorScansWindow.IsVisible;

    public bool IsMiniTrackVisible => miniTrackWindow.IsVisible;

    public bool IsSurfaceVisible => surfaceWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

    public bool AdjustSurfaceZoom(bool zoomIn)
    {
        if (disposed || !surfaceWindow.IsVisible)
        {
            return false;
        }

        surfaceSurvey.AdjustRadarScale(zoomIn);
        return true;
    }

    public bool ResetSurfaceZoom()
    {
        if (disposed || !surfaceWindow.IsVisible)
        {
            return false;
        }

        surfaceSurvey.ResetRadarScale();
        return true;
    }

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeIntent();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        disposalCancellation.Cancel();
        survey.PropertyChanged -= OnSurveyPropertyChanged;
        surfaceSurvey.PropertyChanged -= OnSurfaceSurveyPropertyChanged;
        priorScansViewModel.PropertyChanged -= OnPriorScansPropertyChanged;
        foreach (HostedOverlayWindow hosted in hostedWindows)
        {
            hosted.VisibilityChanged -= OnHostedVisibilityChanged;
            hosted.Dispose();
        }

        surfaceViewModel.Dispose();
        priorScansViewModel.Dispose();
        DisposeFssCapture();
    }

    private void DisposeFssCapture()
    {
        if (!fssCaptureLock.Wait(0, CancellationToken.None))
        {
            _ = DisposeFssCaptureWhenIdleAsync();
            return;
        }

        try
        {
            gameScreenCapture.Dispose();
        }
        finally
        {
            fssCaptureLock.Release();
            fssCaptureLock.Dispose();
            disposalCancellation.Dispose();
        }
    }

    private async Task DisposeFssCaptureWhenIdleAsync()
    {
        await fssCaptureLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            gameScreenCapture.Dispose();
        }
        finally
        {
            fssCaptureLock.Release();
            fssCaptureLock.Dispose();
            disposalCancellation.Dispose();
        }
    }

    private void OnTick()
    {
        survey.RefreshTransientState();
        _ = RefreshBiologyCanonnAsync();
        _ = priorScansViewModel.RefreshAsync();
        SynchronizeIntent();
        _ = RefreshFssTuningAsync();
    }

    private async Task RefreshFssTuningAsync()
    {
        FssTuningCaptureRequest? request = survey.CreateFssTuningCaptureRequest();
        if (!CanCaptureFssTuning(request))
        {
            return;
        }

        if (!gameScreenCapture.IsAvailable)
        {
            ApplyFssCaptureCapabilityStatus();
            return;
        }

        if (!await fssCaptureLock.WaitAsync(0, CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await CaptureAndApplyFssTuningAsync(request!).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (disposalCancellation.IsCancellationRequested)
        {
            // Disposal intentionally cancels pending capture work.
        }
        catch (Exception exception)
            when (exception
                    is Win32Exception
                        or ExternalException
                        or IOException
                        or InvalidDataException
                        or InvalidOperationException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            if (!disposed)
            {
                survey.UpdateFssTuningDetectorStatus(
                    "FSS tuning capture is temporarily unavailable: " + exception.Message
                );
            }
        }
        finally
        {
            fssCaptureLock.Release();
        }
    }

    private bool CanCaptureFssTuning(FssTuningCaptureRequest? request)
    {
        GameWindowSnapshot gameWindow = lastFssBodyWindow.GameWindow;
        return !disposed
            && request is not null
            && lastFssBodyWindow.IsVisible
            && gameWindow.IsAvailable
            && gameWindow.IsVisible
            && gameWindow.IsForeground;
    }

    private async Task CaptureAndApplyFssTuningAsync(FssTuningCaptureRequest request)
    {
        PixelRect gameBounds = lastFssBodyWindow.GameWindow.ClientBounds;
        (CapturedPixelBuffer Pixels, FssTuningAnalysis Analysis) captureResult = await Task.Run(
                () =>
                {
                    CapturedPixelBuffer pixels = FssTuningScreenCapture.Capture(gameScreenCapture, gameBounds);
                    FssTuningAnalysis analysis = FssTuningDetector.Analyze(pixels, request.Settings, request.State);
                    return (Pixels: pixels, Analysis: analysis);
                },
                disposalCancellation.Token
            )
            .ConfigureAwait(true);
        if (disposed)
        {
            return;
        }

        await MaybeSaveFssDiagnosticAsync(request, captureResult).ConfigureAwait(true);
        survey.ApplyFssTuningAnalysis(request.Revision, captureResult.Analysis);
    }

    private async Task MaybeSaveFssDiagnosticAsync(
        FssTuningCaptureRequest request,
        (CapturedPixelBuffer Pixels, FssTuningAnalysis Analysis) captureResult
    )
    {
        bool shouldSaveDiagnostic =
            captureResult.Analysis.Failure is not null
            && request.Settings.SaveDiagnosticImages
            && fssDiagnosticDirectory is not null
            && fssDiagnosticRevision != request.Revision;
        if (!shouldSaveDiagnostic)
        {
            survey.UpdateFssTuningDetectorStatus(null);
            return;
        }

        fssDiagnosticRevision = request.Revision;
        try
        {
            _ = await Task.Run(
                    () =>
                        FssTuningDiagnosticWriter.Save(fssDiagnosticDirectory!, captureResult.Pixels, request.Revision),
                    disposalCancellation.Token
                )
                .ConfigureAwait(true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            survey.UpdateFssTuningDetectorStatus(
                "FSS tuning detection is active, but its diagnostic " + "image could not be saved: " + exception.Message
            );
        }
    }

    private void ApplyFssCaptureCapabilityStatus()
    {
        survey.UpdateFssTuningDetectorStatus(
            gameScreenCapture.IsAvailable ? null : gameScreenCapture.UnavailableReason
        );
    }

    private async Task RefreshBiologyCanonnAsync()
    {
        if (!TryCreateCanonnContext(out string? systemName, out string? commanderName))
        {
            if (canonnLoadedKey is not null)
            {
                canonnLoadedKey = null;
                survey.UpdateCanonnSystemPoi(null);
            }

            return;
        }

        string key = systemName + "\n" + commanderName;
        if (
            string.Equals(canonnLoadedKey, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonnFailedKey, key, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow < canonnRetryAfter
            || !await canonnRefreshLock.WaitAsync(0, CancellationToken.None).ConfigureAwait(true)
        )
        {
            return;
        }

        try
        {
            if (!TryCreateCanonnContext(out systemName, out commanderName))
            {
                return;
            }

            key = systemName + "\n" + commanderName;
            if (string.Equals(canonnLoadedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CanonnSystemPoiResult result = await canonnSystemPoiClient
                .GetAsync(systemName, commanderName, disposalCancellation.Token)
                .ConfigureAwait(true);
            if (
                disposed
                || !TryCreateCanonnContext(out string? currentSystem, out string? currentCommander)
                || !string.Equals(key, currentSystem + "\n" + currentCommander, StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            canonnLoadedKey = key;
            canonnFailedKey = null;
            canonnRetryAfter = default;
            survey.UpdateCanonnSystemPoi(result);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or System.Text.Json.JsonException
                        or TaskCanceledException
                        or IOException
                        or InvalidOperationException
            )
        {
            if (!disposed)
            {
                canonnFailedKey = key;
                canonnRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
        finally
        {
            canonnRefreshLock.Release();
        }
    }

    private bool TryCreateCanonnContext(out string systemName, out string commanderName)
    {
        systemName = survey.Snapshot.SystemName?.Trim() ?? string.Empty;
        commanderName = commanderNameProvider()?.Trim() ?? string.Empty;
        return !disposed && survey.UseExternalData && survey.AutoShowPriorScans && systemName.Length > 0;
    }

    private void OnPriorScansPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(PriorScansOverlayViewModel.ShouldShow))
        {
            SynchronizeIntent();
        }

        // Only SurfaceMarkers is final for PlotGrounded rings; Species/RadarTargets
        // notify earlier in the same recalculation and would re-apply a stale list.
        if (eventArgs.PropertyName == nameof(PriorScansOverlayViewModel.SurfaceMarkers))
        {
            surfaceSurvey.SetPriorScanSurfaceMarkers(priorScansViewModel.SurfaceMarkers);
        }
    }

    private void OnSurfaceSurveyPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(SurfaceSurveyViewModel.ShouldShow)
                or nameof(SurfaceSurveyViewModel.ShouldShowMiniTrack)
                or nameof(SurfaceSurveyViewModel.RadarSize)
        )
        {
            SynchronizeIntent();
        }
    }

    private void OnSurveyPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SystemSurveyViewModel.FssTuningDetectorEnabled))
        {
            fssDiagnosticRevision = null;
            ApplyFssCaptureCapabilityStatus();
        }

        if (
            eventArgs.PropertyName
            is nameof(SystemSurveyViewModel.ShouldShowFssInfo)
                or nameof(SystemSurveyViewModel.ShouldShowLastFssBody)
                or nameof(SystemSurveyViewModel.ShouldShowBodyInfo)
                or nameof(SystemSurveyViewModel.ShouldShowFlightWarning)
                or nameof(SystemSurveyViewModel.ShouldShowBioSystem)
                or nameof(SystemSurveyViewModel.ShouldShowBioStatus)
                or nameof(SystemSurveyViewModel.ShouldLoadPriorScans)
                or nameof(SystemSurveyViewModel.ShouldShowSystemStatus)
                or nameof(SystemSurveyViewModel.IsFssInfoForced)
                or nameof(SystemSurveyViewModel.IsBodyInfoForced)
        )
        {
            SynchronizeIntent();
        }
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        bool ready = !isSuppressed;
        bodyInfoWindow.Reconcile(ready && survey.ShouldShowBodyInfo && windowRegistry.ShouldHost("PlotBodyInfo"));
        fssWindow.Reconcile(ready && survey.ShouldShowFssInfo && windowRegistry.ShouldHost("PlotFSSInfo"));
        lastFssBodyWindow.Reconcile(ready && survey.ShouldShowLastFssBody);
        statusWindow.Reconcile(ready && survey.ShouldShowSystemStatus);
        flightWarningWindow.Reconcile(ready && survey.ShouldShowFlightWarning);
        biologyWindow.Reconcile(ready && survey.ShouldShowBioSystem && windowRegistry.ShouldHost("PlotBioSystem"));
        biologyStatusWindow.Reconcile(
            ready && survey.ShouldShowBioStatus && windowRegistry.ShouldHost("PlotBioStatus")
        );
        priorScansWindow.Reconcile(
            ready && priorScansViewModel.ShouldShow && windowRegistry.ShouldHost("PlotPriorScans")
        );
        surfaceWindow.Reconcile(ready && surfaceSurvey.ShouldShow && windowRegistry.ShouldHost("PlotGrounded"));
        miniTrackWindow.Reconcile(ready && surfaceSurvey.ShouldShowMiniTrack);
    }

    private PassiveOverlayWindowDefinition CreateDefinition(
        string plotterName,
        Func<Window> createWindow,
        Func<PixelRect, PixelSize, PixelPoint> fallbackPlacement
    )
    {
        return new PassiveOverlayWindowDefinition(
            plotterName,
            _ => createWindow(),
            fallbackPlacement,
            ApplyPreparation
        );
    }

    private BodyInformationOverlayWindow CreateBodyInfoWindow() => new(viewModel);

    private FssInfoOverlayWindow CreateFssWindow() => new(viewModel);

    private LastFssBodyOverlayWindow CreateLastFssBodyWindow() => new(viewModel);

    private SystemStatusOverlayWindow CreateStatusWindow() => new(viewModel);

    private FlightWarningOverlayWindow CreateFlightWarningWindow() => new(viewModel);

    private BiologySurveyOverlayWindow CreateBiologyWindow() => new(viewModel);

    private BiologyStatusOverlayWindow CreateBiologyStatusWindow() => new(viewModel);

    private PriorScansOverlayWindow CreatePriorScansWindow() => new(priorScansViewModel);

    private SurfaceSurveyOverlayWindow CreateSurfaceWindow() => new(surfaceViewModel);

    private MiniTrackOverlayWindow CreateMiniTrackWindow() => new(surfaceViewModel);

    private void ApplyPreparation(OverlayPreparationResult preparation)
    {
        viewModel.ApplyPreparation(preparation);
        priorScansViewModel.ApplyPreparation(preparation);
        surfaceViewModel.ApplyPreparation(preparation);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private PixelPoint PlaceSurfaceWindow(HostedOverlayPlacement placement)
    {
        placement.SetBaseSize(surfaceViewModel.WindowWidth, surfaceViewModel.WindowHeight);
        return placement.GetPosition(placement.PrepareSize());
    }

    private void ApplySurfaceWindowSize(Window window)
    {
        ApplySurfaceWindowSize(window, overlayLayout, surfaceViewModel);
    }

    internal static void ApplySurfaceWindowSize(
        Window window,
        LegacyOverlayLayout layout,
        SurfaceSurveyOverlayViewModel viewModel
    )
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(viewModel);
        OverlayThemeResources.SetBaseSize(window, layout, viewModel.WindowWidth, viewModel.WindowHeight);
    }

    private PixelPoint PlaceBiologyWindow(PixelRect bounds, PixelSize size)
    {
        int statusOffset =
            statusWindow.CurrentWindow is not { } status || status.Bounds.Height <= 0
                ? 0
                : Math.Max(0, bounds.Bottom - status.Position.Y) + 12;
        return new PixelPoint(
            bounds.X + DefaultMargin,
            Math.Max(bounds.Y + DefaultMargin, bounds.Bottom - size.Height - DefaultMargin - statusOffset)
        );
    }
}

public sealed class SystemSurveyOverlayCoordinatorOptions
{
    public Func<string?>? CommanderNameProvider { get; init; }

    public ICanonnSystemPoiClient? CanonnSystemPoiClient { get; init; }

    public ExobiologyReferenceCatalog? ExobiologyCatalog { get; init; }

    public IGameScreenCapture? GameScreenCapture { get; init; }

    public string? FssDiagnosticDirectory { get; init; }
}
