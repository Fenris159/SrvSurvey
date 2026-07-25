using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class SystemSurveyOverlayCoordinator : IDisposable
{
    private readonly SystemSurveyViewModel survey;
    private readonly SurfaceSurveyViewModel surfaceSurvey;
    private readonly SystemSurveyOverlayViewModel viewModel;
    private readonly SurfaceSurveyOverlayViewModel surfaceViewModel;
    private readonly PriorScansOverlayViewModel priorScansViewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly ICanonnSystemPoiClient canonnSystemPoiClient;
    private readonly Func<string?> commanderNameProvider;
    private readonly SemaphoreSlim canonnRefreshLock = new(1, 1);
    private readonly CancellationTokenSource disposalCancellation = new();
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private BiologySurveyOverlayWindow? biologyWindow;
    private BiologyStatusOverlayWindow? biologyStatusWindow;
    private BodyInformationOverlayWindow? bodyInfoWindow;
    private FlightWarningOverlayWindow? flightWarningWindow;
    private FssInfoOverlayWindow? fssWindow;
    private LastFssBodyOverlayWindow? lastFssBodyWindow;
    private MiniTrackOverlayWindow? miniTrackWindow;
    private PriorScansOverlayWindow? priorScansWindow;
    private SurfaceSurveyOverlayWindow? surfaceWindow;
    private SystemStatusOverlayWindow? statusWindow;
    private bool isSuppressed;
    private bool isBiologyObscured;
    private bool isBiologyStatusObscured;
    private bool isBodyInfoObscured;
    private bool isFssObscured;
    private bool isPriorScansObscured;
    private bool isSurfaceObscured;
    private string? canonnLoadedKey;
    private string? canonnFailedKey;
    private DateTimeOffset canonnRetryAfter;
    private bool disposed;

    public SystemSurveyOverlayCoordinator(
        SystemSurveyViewModel survey,
        SurfaceSurveyViewModel surfaceSurvey,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        Func<string?>? commanderNameProvider = null,
        ICanonnSystemPoiClient? canonnSystemPoiClient = null,
        ExobiologyReferenceCatalog? exobiologyCatalog = null,
        LegacyOverlayLayout? overlayLayout = null)
    {
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.surfaceSurvey = surfaceSurvey
            ?? throw new ArgumentNullException(nameof(surfaceSurvey));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        this.commanderNameProvider = commanderNameProvider ?? (() => null);
        this.canonnSystemPoiClient = new CachingCanonnSystemPoiClient(
            canonnSystemPoiClient ?? new CanonnSystemPoiClient());
        viewModel = new SystemSurveyOverlayViewModel(
            survey,
            platform.Capabilities);
        surfaceViewModel = new SurfaceSurveyOverlayViewModel(
            surfaceSurvey,
            platform.Capabilities);
        priorScansViewModel = new PriorScansOverlayViewModel(
            survey,
            this.canonnSystemPoiClient,
            exobiologyCatalog ?? ExobiologyReferenceCatalog.LoadEmbedded(),
            this.commanderNameProvider,
            platform.Capabilities);
        survey.PropertyChanged += OnSurveyPropertyChanged;
        surfaceSurvey.PropertyChanged += OnSurfaceSurveyPropertyChanged;
        priorScansViewModel.PropertyChanged +=
            OnPriorScansPropertyChanged;
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindows();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => biologyWindow is not null
        || biologyStatusWindow is not null
        || bodyInfoWindow is not null
        || flightWarningWindow is not null
        || fssWindow is not null
        || lastFssBodyWindow is not null
        || miniTrackWindow is not null
        || priorScansWindow is not null
        || surfaceWindow is not null
        || statusWindow is not null;

    public bool IsFssVisible => fssWindow is not null;

    public bool IsLastFssBodyVisible => lastFssBodyWindow is not null;

    public bool IsBodyInfoVisible => bodyInfoWindow is not null;

    public bool IsFlightWarningVisible => flightWarningWindow is not null;

    public bool IsBiologyVisible => biologyWindow is not null;

    public bool IsBiologyStatusVisible => biologyStatusWindow is not null;

    public bool IsPriorScansVisible => priorScansWindow is not null;

    public bool IsMiniTrackVisible => miniTrackWindow is not null;

    public bool IsSurfaceVisible => surfaceWindow is not null;

    public bool IsSuppressed => isSuppressed;

    public bool AdjustSurfaceZoom(bool zoomIn)
    {
        if (disposed || surfaceWindow is null)
        {
            return false;
        }

        surfaceSurvey.AdjustRadarScale(zoomIn);
        return true;
    }

    public bool ResetSurfaceZoom()
    {
        if (disposed || surfaceWindow is null)
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
        SynchronizeWindows();
    }

    public void SetFssObscured(bool value)
    {
        if (disposed || value == isFssObscured)
        {
            return;
        }

        isFssObscured = value;
        SynchronizeWindows();
    }

    public void SetBodyInfoObscured(bool value)
    {
        if (disposed || value == isBodyInfoObscured)
        {
            return;
        }

        isBodyInfoObscured = value;
        SynchronizeWindows();
    }

    public void SetBiologyObscured(bool value)
    {
        if (disposed || value == isBiologyObscured)
        {
            return;
        }

        isBiologyObscured = value;
        SynchronizeWindows();
    }

    public void SetBiologyStatusObscured(bool value)
    {
        if (disposed || value == isBiologyStatusObscured)
        {
            return;
        }

        isBiologyStatusObscured = value;
        SynchronizeWindows();
    }

    public void SetPriorScansObscured(bool value)
    {
        if (disposed || value == isPriorScansObscured)
        {
            return;
        }

        isPriorScansObscured = value;
        SynchronizeWindows();
    }

    public void SetSurfaceObscured(bool value)
    {
        if (disposed || value == isSurfaceObscured)
        {
            return;
        }

        isSurfaceObscured = value;
        SynchronizeWindows();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        disposalCancellation.Cancel();
        timer.Stop();
        timer.Tick -= OnTimerTick;
        survey.PropertyChanged -= OnSurveyPropertyChanged;
        surfaceSurvey.PropertyChanged -= OnSurfaceSurveyPropertyChanged;
        priorScansViewModel.PropertyChanged -=
            OnPriorScansPropertyChanged;
        CloseBiologyWindow();
        CloseBiologyStatusWindow();
        CloseBodyInfoWindow();
        CloseFlightWarningWindow();
        CloseFssWindow();
        CloseLastFssBodyWindow();
        CloseMiniTrackWindow();
        ClosePriorScansWindow();
        CloseSurfaceWindow();
        CloseStatusWindow();
        surfaceViewModel.Dispose();
        priorScansViewModel.Dispose();
        disposalCancellation.Dispose();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        survey.RefreshTransientState();
        _ = RefreshBiologyCanonnAsync();
        _ = priorScansViewModel.RefreshAsync();
        SynchronizeWindows();
    }

    private async Task RefreshBiologyCanonnAsync()
    {
        if (!TryCreateCanonnContext(out var systemName, out var commanderName))
        {
            if (canonnLoadedKey is not null)
            {
                canonnLoadedKey = null;
                survey.UpdateCanonnSystemPoi(null);
            }

            return;
        }

        var key = systemName + "\n" + commanderName;
        if (string.Equals(canonnLoadedKey, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonnFailedKey, key, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow < canonnRetryAfter
            || !await canonnRefreshLock.WaitAsync(0).ConfigureAwait(true))
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
            if (string.Equals(
                canonnLoadedKey,
                key,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var result = await canonnSystemPoiClient.GetAsync(
                systemName,
                commanderName,
                disposalCancellation.Token).ConfigureAwait(true);
            if (disposed
                || !TryCreateCanonnContext(
                    out var currentSystem,
                    out var currentCommander)
                || !string.Equals(
                    key,
                    currentSystem + "\n" + currentCommander,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            canonnLoadedKey = key;
            canonnFailedKey = null;
            canonnRetryAfter = default;
            survey.UpdateCanonnSystemPoi(result);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or System.Text.Json.JsonException
                or TaskCanceledException
                or IOException
                or InvalidOperationException)
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

    private bool TryCreateCanonnContext(
        out string systemName,
        out string commanderName)
    {
        systemName = survey.Snapshot.SystemName?.Trim() ?? string.Empty;
        commanderName = commanderNameProvider()?.Trim() ?? string.Empty;
        return !disposed
            && survey.UseExternalData
            && survey.AutoShowPriorScans
            && systemName.Length > 0;
    }

    private void OnPriorScansPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(
                PriorScansOverlayViewModel.ShouldShow))
        {
            SynchronizeWindows();
        }
    }

    private void OnSurfaceSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SurfaceSurveyViewModel.ShouldShow)
            or nameof(SurfaceSurveyViewModel.ShouldShowMiniTrack)
            or nameof(SurfaceSurveyViewModel.RadarSize))
        {
            SynchronizeWindows();
        }
    }

    private void OnSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.ShouldShowFssInfo)
            or nameof(SystemSurveyViewModel.ShouldShowLastFssBody)
            or nameof(SystemSurveyViewModel.ShouldShowBodyInfo)
            or nameof(SystemSurveyViewModel.ShouldShowFlightWarning)
            or nameof(SystemSurveyViewModel.ShouldShowBioSystem)
            or nameof(SystemSurveyViewModel.ShouldShowBioStatus)
            or nameof(SystemSurveyViewModel.ShouldLoadPriorScans)
            or nameof(SystemSurveyViewModel.ShouldShowSystemStatus)
            or nameof(SystemSurveyViewModel.IsFssInfoForced)
            or nameof(SystemSurveyViewModel.IsBodyInfoForced))
        {
            SynchronizeWindows();
        }
    }

    private void SynchronizeWindows()
    {
        if (disposed)
        {
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        var platformReady = !isSuppressed
            && platform.Capabilities.SupportsPassiveOverlay
            && platform.Capabilities.SupportsClickThrough
            && platform.Capabilities.SupportsGameWindowTracking
            && gameWindow.IsAvailable
            && gameWindow.IsVisible
            && gameWindow.IsForeground;
        var showFss = platformReady
            && survey.ShouldShowFssInfo
            && (!isFssObscured || survey.IsFssInfoForced);
        var showLastFssBody = platformReady
            && survey.ShouldShowLastFssBody;
        var showBodyInfo = platformReady
            && survey.ShouldShowBodyInfo
            && (!isBodyInfoObscured || survey.IsBodyInfoForced);
        var showStatus = platformReady && survey.ShouldShowSystemStatus;
        var showFlightWarning = platformReady
            && survey.ShouldShowFlightWarning;
        var showBiology = platformReady
            && survey.ShouldShowBioSystem
            && !isBiologyObscured;
        var showBiologyStatus = platformReady
            && survey.ShouldShowBioStatus
            && !isBiologyStatusObscured;
        var showPriorScans = platformReady
            && priorScansViewModel.ShouldShow
            && !isPriorScansObscured;
        var showSurface = platformReady
            && surfaceSurvey.ShouldShow
            && !isSurfaceObscured;
        var showMiniTrack = platformReady
            && surfaceSurvey.ShouldShowMiniTrack;

        SynchronizeBodyInfoWindow(showBodyInfo);
        SynchronizeFssWindow(showFss);
        SynchronizeLastFssBodyWindow(showLastFssBody);
        SynchronizeStatusWindow(showStatus);
        SynchronizeFlightWarningWindow(showFlightWarning);
        SynchronizeBiologyWindow(showBiology);
        SynchronizeBiologyStatusWindow(showBiologyStatus);
        SynchronizePriorScansWindow(showPriorScans);
        SynchronizeSurfaceWindow(showSurface);
        SynchronizeMiniTrackWindow(showMiniTrack);
    }

    private void SynchronizeMiniTrackWindow(bool show)
    {
        if (!show)
        {
            CloseMiniTrackWindow();
            return;
        }

        if (miniTrackWindow is not null)
        {
            PositionMiniTrack(miniTrackWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new MiniTrackOverlayWindow(surfaceViewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotMiniTrack");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionMiniTrack,
            CloseMiniTrackWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(miniTrackWindow, overlay))
            {
                miniTrackWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        miniTrackWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeFlightWarningWindow(bool show)
    {
        if (!show)
        {
            CloseFlightWarningWindow();
            return;
        }

        if (flightWarningWindow is not null)
        {
            PositionTopCenter(flightWarningWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new FlightWarningOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotFlightWarning");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopCenter,
            CloseFlightWarningWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(flightWarningWindow, overlay))
            {
                flightWarningWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        flightWarningWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeSurfaceWindow(bool show)
    {
        if (!show)
        {
            CloseSurfaceWindow();
            return;
        }

        if (surfaceWindow is not null)
        {
            PositionBottomCenter(surfaceWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new SurfaceSurveyOverlayWindow(surfaceViewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotGrounded");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionBottomCenter,
            CloseSurfaceWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(surfaceWindow, overlay))
            {
                surfaceWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        surfaceWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizePriorScansWindow(bool show)
    {
        if (!show)
        {
            ClosePriorScansWindow();
            return;
        }

        if (priorScansWindow is not null)
        {
            PositionBottomRight(priorScansWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new PriorScansOverlayWindow(priorScansViewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotPriorScans");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionBottomRight,
            ClosePriorScansWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(priorScansWindow, overlay))
            {
                priorScansWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        priorScansWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeBiologyStatusWindow(bool show)
    {
        if (!show)
        {
            CloseBiologyStatusWindow();
            return;
        }

        if (biologyStatusWindow is not null)
        {
            PositionTopCenter(biologyStatusWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new BiologyStatusOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotBioStatus");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopCenter,
            CloseBiologyStatusWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(biologyStatusWindow, overlay))
            {
                biologyStatusWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        biologyStatusWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeBiologyWindow(bool show)
    {
        if (!show)
        {
            CloseBiologyWindow();
            return;
        }

        if (biologyWindow is not null)
        {
            PositionBiologyWindow(biologyWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new BiologySurveyOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotBioSystem");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionBiologyWindow,
            CloseBiologyWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(biologyWindow, overlay))
            {
                biologyWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        biologyWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeBodyInfoWindow(bool show)
    {
        if (!show)
        {
            CloseBodyInfoWindow();
            return;
        }

        if (bodyInfoWindow is not null)
        {
            PositionTopLeft(bodyInfoWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new BodyInformationOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotBodyInfo");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopLeft,
            CloseBodyInfoWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(bodyInfoWindow, overlay))
            {
                bodyInfoWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        bodyInfoWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeLastFssBodyWindow(bool show)
    {
        if (!show)
        {
            CloseLastFssBodyWindow();
            return;
        }

        if (lastFssBodyWindow is not null)
        {
            PositionTopCenter(lastFssBodyWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new LastFssBodyOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotFSS");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopCenter,
            CloseLastFssBodyWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(lastFssBodyWindow, overlay))
            {
                lastFssBodyWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        lastFssBodyWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeFssWindow(bool show)
    {
        if (!show)
        {
            CloseFssWindow();
            return;
        }

        if (fssWindow is not null)
        {
            PositionTopLeft(fssWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new FssInfoOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotFSSInfo");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopLeft,
            CloseFssWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(fssWindow, overlay))
            {
                fssWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        fssWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeStatusWindow(bool show)
    {
        if (!show)
        {
            CloseStatusWindow();
            return;
        }

        if (statusWindow is not null)
        {
            PositionBottomLeft(statusWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new SystemStatusOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotSysStatus");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionBottomLeft,
            CloseStatusWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(statusWindow, overlay))
            {
                statusWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        statusWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareWindow(
        Window window,
        Action<Window, PixelRect> position,
        Action close)
    {
        position(window, gameWindow.ClientBounds);
        var preparation = platform.PreparePassiveWindow(window);
        viewModel.ApplyPreparation(preparation);
        priorScansViewModel.ApplyPreparation(preparation);
        surfaceViewModel.ApplyPreparation(preparation);
        if (!preparation.IsClickThrough)
        {
            isSuppressed = true;
            close();
        }
    }

    private void PositionTopLeft(Window window, PixelRect gameBounds)
    {
        var plotterName = window switch
        {
            BodyInformationOverlayWindow => "PlotBodyInfo",
            FssInfoOverlayWindow => "PlotFSSInfo",
            _ => throw new InvalidOperationException(
                $"No legacy top-left layout maps to {window.GetType().Name}."),
        };
        PositionWindow(
            window,
            gameBounds,
            plotterName,
            OverlayWindowPlacement.TopLeft);
    }

    private void PositionTopCenter(Window window, PixelRect gameBounds)
    {
        var plotterName = window switch
        {
            FlightWarningOverlayWindow => "PlotFlightWarning",
            BiologyStatusOverlayWindow => "PlotBioStatus",
            LastFssBodyOverlayWindow => "PlotFSS",
            _ => throw new InvalidOperationException(
                $"No legacy top-center layout maps to {window.GetType().Name}."),
        };
        PositionWindow(
            window,
            gameBounds,
            plotterName,
            OverlayWindowPlacement.TopCenter);
    }

    private void PositionMiniTrack(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotMiniTrack",
            OverlayWindowPlacement.TopRight,
            margin: 8);
    }

    private void PositionBottomLeft(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotSysStatus",
            OverlayWindowPlacement.BottomLeft);
    }

    private void PositionBottomRight(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotPriorScans",
            OverlayWindowPlacement.BottomRight);
    }

    private void PositionBottomCenter(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotGrounded",
            OverlayWindowPlacement.BottomCenter);
    }

    private void PositionBiologyWindow(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotBioSystem",
            (bounds, size, margin) =>
            {
                var statusOffset = statusWindow is null
                    || statusWindow.Bounds.Height <= 0
                    ? 0
                    : Math.Max(0, bounds.Bottom - statusWindow.Position.Y) + 12;
                return new PixelPoint(
                    bounds.X + margin,
                    Math.Max(
                        bounds.Y + margin,
                        bounds.Bottom - size.Height - margin - statusOffset));
            });
    }

    private void PositionWindow(
        Window window,
        PixelRect gameBounds,
        string plotterName,
        Func<PixelRect, PixelSize, int, PixelPoint> calculate,
        int margin = 20)
    {
        OverlayThemeResources.ApplyOpacity(window, overlayLayout, plotterName);
        var screen = window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var width = (int)Math.Ceiling(window.Width * screen.Scaling);
        var logicalHeight = window.Bounds.Height > 0
            ? window.Bounds.Height
            : window.MinHeight;
        var height = (int)Math.Ceiling(logicalHeight * screen.Scaling);
        var size = new PixelSize(width, Math.Max(height, 1));
        var position = overlayLayout.GetPosition(plotterName, gameBounds, size)
            ?? calculate(gameBounds, size, margin);
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void CloseFssWindow()
    {
        var overlay = fssWindow;
        if (overlay is null)
        {
            return;
        }

        fssWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseFlightWarningWindow()
    {
        var overlay = flightWarningWindow;
        if (overlay is null)
        {
            return;
        }

        flightWarningWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMiniTrackWindow()
    {
        var overlay = miniTrackWindow;
        if (overlay is null)
        {
            return;
        }

        miniTrackWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseBiologyWindow()
    {
        var overlay = biologyWindow;
        if (overlay is null)
        {
            return;
        }

        biologyWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseBiologyStatusWindow()
    {
        var overlay = biologyStatusWindow;
        if (overlay is null)
        {
            return;
        }

        biologyStatusWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseBodyInfoWindow()
    {
        var overlay = bodyInfoWindow;
        if (overlay is null)
        {
            return;
        }

        bodyInfoWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseLastFssBodyWindow()
    {
        var overlay = lastFssBodyWindow;
        if (overlay is null)
        {
            return;
        }

        lastFssBodyWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseStatusWindow()
    {
        var overlay = statusWindow;
        if (overlay is null)
        {
            return;
        }

        statusWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClosePriorScansWindow()
    {
        var overlay = priorScansWindow;
        if (overlay is null)
        {
            return;
        }

        priorScansWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseSurfaceWindow()
    {
        var overlay = surfaceWindow;
        if (overlay is null)
        {
            return;
        }

        surfaceWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
