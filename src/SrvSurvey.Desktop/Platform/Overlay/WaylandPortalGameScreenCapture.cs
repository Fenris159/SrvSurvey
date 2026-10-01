using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using PipeWire.NET;
using SrvSurvey.Core.Storage;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]

namespace SrvSurvey.Desktop.Platform.Overlay;

[SupportedOSPlatform("linux")]
// This adapter requires a live XDG portal and PipeWire desktop session. Its pure parsing and cropping helpers remain covered.
[ExcludeFromCodeCoverage]
internal sealed partial class WaylandPortalGameScreenCapture : IGameScreenCapture
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim RestoreTokenGate = new(1, 1);
    private static int nextToken;

    private readonly Lock gate = new();
    private readonly string dataDirectory;
    private readonly Func<CancellationToken, Task<bool>>? confirmScreenShare;
    private readonly Action<string>? log;
    private readonly string capturePurpose;
    private readonly CancellationTokenSource shutdown = new();
    private Task? initialization;
    private Connection? connection;
    private ISession? portalSession;
    private PipeWireContext? pipeWireContext;
    private PipeWireVideoCapture? pipeWireCapture;
    private PortalStreamInfo streamInfo;
    private IReadOnlyList<PixelRect> x11MonitorBounds = [];
    private CaptureRequest? pendingRequest;
    private bool hasLoggedFirstFrame;
    private bool hasLoggedFirstCrop;
    private bool disposed;

    public WaylandPortalGameScreenCapture(
        Func<CancellationToken, Task<bool>>? confirmScreenShare = null,
        Action<string>? log = null,
        string capturePurpose = "game-screen detection"
    )
        : this(
            WaylandCaptureSourceSelection.GetRestoreTokenPath(AppDataPaths.ResolveCurrent().DataDirectory),
            confirmScreenShare,
            log,
            capturePurpose
        ) { }

    /// <summary>Creates a portal capture for one data directory and capture purpose.</summary>
    internal WaylandPortalGameScreenCapture(
        string restoreTokenPath,
        Func<CancellationToken, Task<bool>>? confirmScreenShare = null,
        Action<string>? log = null,
        string capturePurpose = "game-screen detection"
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreTokenPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(capturePurpose);
        dataDirectory = Path.GetDirectoryName(Path.GetFullPath(restoreTokenPath))!;
        this.confirmScreenShare = confirmScreenShare;
        this.log = log;
        this.capturePurpose = capturePurpose;
    }

    public bool IsAvailable => !disposed;

    public string? UnavailableReason => disposed ? "The Wayland screen capture session is closed." : null;

    /// <summary>Opens the desktop source picker without starting a PipeWire video stream.</summary>
    internal static async Task SelectSourceAsync(string dataDirectory, Action<string>? log = null)
    {
        var capture = new WaylandPortalGameScreenCapture(
            WaylandCaptureSourceSelection.GetRestoreTokenPath(dataDirectory),
            log: log,
            capturePurpose: "capture source selection"
        );
        try
        {
            await capture.InitializePortalSessionAsync(connectVideoStream: false).ConfigureAwait(false);
        }
        finally
        {
            await capture.DisposeAsyncResources().ConfigureAwait(false);
        }
    }

    public CapturedPixelBuffer Capture(PixelRect bounds) => Capture(bounds, bounds);

    public CapturedPixelBuffer Capture(PixelRect bounds, PixelRect sourceBounds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateBounds(bounds, sourceBounds);
        EnsureInitialized();

        var completion = new TaskCompletionSource<CapturedPixelBuffer>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pendingRequest is not null)
            {
                throw new InvalidOperationException("A Wayland screen capture is already in progress.");
            }

            pendingRequest = new CaptureRequest(bounds, sourceBounds, completion);
        }

        try
        {
            return completion.Task.WaitAsync(FrameTimeout, shutdown.Token).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                "Wayland did not provide a frame from the shared Elite Dangerous window.",
                exception
            );
        }
        catch (OperationCanceledException exception)
        {
            throw new NotSupportedException("The Wayland screen capture session is closed.", exception);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(pendingRequest?.Completion, completion))
                {
                    pendingRequest = null;
                }
            }
        }
    }

    /// <summary>Cancels pending frames and releases the portal session; later calls retry incomplete cleanup.</summary>
    public void Dispose()
    {
        Task? initializationToWait;
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                shutdown.Cancel();
                pendingRequest?.Completion.TrySetException(
                    new ObjectDisposedException(nameof(WaylandPortalGameScreenCapture))
                );
                pendingRequest = null;
            }

            initializationToWait = initialization;
        }

        try
        {
            initializationToWait?.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Initialization errors have already been reported to the capture caller.
        }

        DisposeAsyncResources().GetAwaiter().GetResult();
    }

    private void EnsureInitialized()
    {
        Task initializationTask;
        lock (gate)
        {
            initialization ??= InitializeAsync();
            initializationTask = initialization;
        }

        try
        {
            initializationTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (
                    !disposed
                    && ReferenceEquals(initialization, initializationTask)
                    && IsRetryableInitializationFailure(exception)
                )
                {
                    initialization = null;
                }
            }

            if (exception is NotSupportedException)
            {
                throw;
            }

            throw new NotSupportedException("Wayland screen sharing could not start: " + exception.Message, exception);
        }
    }

    private static bool IsRetryableInitializationFailure(Exception exception)
    {
        if (exception is NotSupportedException && exception.InnerException is null)
        {
            return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidDataException)
            {
                return false;
            }
        }

        return true;
    }

    private async Task InitializeAsync()
    {
        try
        {
            await InitializePortalSessionAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log?.Invoke(
                $"Wayland capture ({capturePurpose}): initialization failed. "
                    + CaptureFailureDiagnostics.Describe(exception)
            );
            await ReleaseSessionResourcesAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates the portal session, chooses a source, and optionally attaches its video stream.</summary>
    private async Task InitializePortalSessionAsync(bool connectVideoStream = true)
    {
        var portalConnection = new Connection(Address.Session);
        connection = portalConnection;
        ConnectionInfo connectionInfo = await portalConnection.ConnectAsync();
        string senderName = NormalizeSenderName(connectionInfo.LocalName);
        IScreenCast screenCast = portalConnection.CreateProxy<IScreenCast>(PortalService, PortalPath);
        uint portalVersion = await screenCast.GetVersionAsync();
        uint sourceTypes = (await screenCast.GetAvailableSourceTypesAsync()) & 3U;
        if (sourceTypes == 0)
        {
            throw new NotSupportedException("The Wayland desktop does not offer window or monitor sharing.");
        }

        string sessionPath;
        PortalResponse startResponse;
        await RestoreTokenGate.WaitAsync(shutdown.Token).ConfigureAwait(false);
        try
        {
            bool forceReselection = TryConsumeReselectionRequest();
            string? restoreToken = portalVersion >= 4 && !forceReselection ? TryReadRestoreToken() : null;
            if (forceReselection)
            {
                log?.Invoke($"Wayland capture ({capturePurpose}): Settings requested a fresh source selection.");
            }

            log?.Invoke(
                $"Wayland capture ({capturePurpose}): portal version {portalVersion}; "
                    + $"available sources {DescribeAvailableSources(sourceTypes)}; "
                    + $"saved selection {(restoreToken is null ? "not available" : "requested")}."
            );
            if (
                GameScreenCapture.ShouldShowWaylandSelectionGuidance(portalVersion, restoreToken)
                && confirmScreenShare is not null
                && !await confirmScreenShare(shutdown.Token).ConfigureAwait(false)
            )
            {
                throw new NotSupportedException(
                    "Wayland screen sharing was canceled before the desktop picker opened."
                );
            }

            var createOptions = new Dictionary<string, object> { ["session_handle_token"] = NextToken("session") };
            PortalResponse createResponse = await InvokeRequestAsync(
                portalConnection,
                senderName,
                token => screenCast.CreateSessionAsync(AddRequestToken(createOptions, token)),
                shutdown.Token
            );
            sessionPath = ReadRequiredString(createResponse.Results, "session_handle");
            portalSession = portalConnection.CreateProxy<ISession>(PortalService, sessionPath);

            var selectOptions = new Dictionary<string, object> { ["types"] = sourceTypes, ["multiple"] = false };
            if (portalVersion >= 2)
            {
                selectOptions["cursor_mode"] = 1U;
            }

            if (portalVersion >= 4)
            {
                selectOptions["persist_mode"] = 2U;
                if (restoreToken is not null)
                {
                    selectOptions["restore_token"] = restoreToken;
                }
            }

            _ = await InvokeRequestAsync(
                portalConnection,
                senderName,
                token => screenCast.SelectSourcesAsync(sessionPath, AddRequestToken(selectOptions, token)),
                shutdown.Token
            );
            startResponse = await InvokeRequestAsync(
                portalConnection,
                senderName,
                token => screenCast.StartAsync(sessionPath, string.Empty, RequestOptions(token)),
                shutdown.Token
            );
            SaveRestoreToken(startResponse.Results);
        }
        finally
        {
            RestoreTokenGate.Release();
        }

        streamInfo = PortalStreamInfo.Read(startResponse.Results);
        if (streamInfo.SourceType == 1)
        {
            x11MonitorBounds = X11Native.ReadMonitorBounds();
        }
        log?.Invoke(
            $"Wayland capture ({capturePurpose}): portal selected {streamInfo.DescribeSource()} "
                + $"(PipeWire node {streamInfo.NodeId})."
        );
        if (!connectVideoStream)
        {
            return;
        }

        log?.Invoke($"Wayland capture ({capturePurpose}): opening the portal PipeWire remote.");
        using CloseSafeHandle remote = await screenCast.OpenPipeWireRemoteAsync(
            sessionPath,
            new Dictionary<string, object>()
        );
        log?.Invoke(
            $"Wayland capture ({capturePurpose}): portal PipeWire descriptor is "
                + $"{DescribePortalDescriptor(remote)}."
        );
        var context = new PipeWireContext("SrvSurvey.ScreenCapture");
        pipeWireContext = context;
        log?.Invoke($"Wayland capture ({capturePurpose}): starting PipeWire with the portal descriptor.");
        await context.StartAsync(remote, shutdown.Token);
        log?.Invoke(
            $"Wayland capture ({capturePurpose}): PipeWire connected; attaching video stream on node "
                + $"{streamInfo.NodeId}."
        );

        var videoCapture = new PipeWireVideoCapture(context, "SrvSurvey.ScreenCapture");
        pipeWireCapture = videoCapture;
        videoCapture.FrameReady += OnFrameReady;
        videoCapture.Connect(
            streamInfo.TargetNodeId,
            [PixelFormat.Bgra, PixelFormat.Bgrx, PixelFormat.Rgba, PixelFormat.Rgbx],
            targetObjectName: streamInfo.TargetObjectName
        );
        log?.Invoke(
            $"Wayland capture ({capturePurpose}): PipeWire video stream connected; waiting for the first frame."
        );
    }

    private static string DescribePortalDescriptor(SafeHandle remote) =>
        remote.IsInvalid || remote.IsClosed ? "unusable" : "open";

    /// <summary>Completes the pending crop request from a PipeWire frame.</summary>
    private void OnFrameReady(PipeWireVideoCapture sender, VideoFrame frame)
    {
        CaptureRequest? request;
        lock (gate)
        {
            request = pendingRequest;
            pendingRequest = null;
        }

        if (request is null)
        {
            return;
        }

        try
        {
            if (!hasLoggedFirstFrame)
            {
                hasLoggedFirstFrame = true;
                log?.Invoke(
                    $"Wayland capture ({capturePurpose}): first PipeWire frame "
                        + $"{frame.Width}x{frame.Height}, stride {frame.Stride}, format {frame.Format}; "
                        + $"requested {DescribeBounds(request.Bounds)} from game source "
                        + $"{DescribeBounds(request.SourceBounds)}."
                );
            }

            CapturedPixelBuffer result = PortalFrameCropper.Crop(
                frame,
                streamInfo,
                request.Bounds,
                request.SourceBounds,
                x11MonitorBounds
            );
            if (!hasLoggedFirstCrop)
            {
                hasLoggedFirstCrop = true;
                log?.Invoke(
                    $"Wayland capture ({capturePurpose}): first cropped frame is {result.Width}x{result.Height}."
                );
            }

            request.Completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            request.Completion.TrySetException(exception);
        }
    }

    /// <summary>Releases session resources and disposes the shutdown token even after cleanup errors.</summary>
    private async Task DisposeAsyncResources()
    {
        try
        {
            await ReleaseSessionResourcesAsync().ConfigureAwait(false);
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    /// <summary>Attempts every capture and portal cleanup step before reporting failures.</summary>
    private async Task ReleaseSessionResourcesAsync()
    {
        var failures = new List<Exception>();
        pipeWireCapture?.FrameReady -= OnFrameReady;
        if (pipeWireCapture is not null)
        {
            try
            {
                await pipeWireCapture.DisposeAsync().ConfigureAwait(false);
                pipeWireCapture = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (pipeWireContext is not null)
        {
            try
            {
                await pipeWireContext.DisposeAsync().ConfigureAwait(false);
                pipeWireContext = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (portalSession is not null)
        {
            try
            {
                await portalSession.CloseAsync().ConfigureAwait(false);
                portalSession = null;
            }
            catch (DBusException)
            {
                // The compositor may already have closed the portal session.
                portalSession = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (portalSession is null && connection is not null)
        {
            try
            {
                connection.Dispose();
                connection = null;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("The Wayland capture session could not be fully closed.", failures);
        }
    }

    private static async Task<PortalResponse> InvokeRequestAsync(
        Connection portalConnection,
        string senderName,
        Func<string, Task<ObjectPath>> invoke,
        CancellationToken cancellationToken
    )
    {
        string token = NextToken("request");
        var expectedPath = new ObjectPath($"/org/freedesktop/portal/desktop/request/{senderName}/{token}");
        var completion = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        IRequest request = portalConnection.CreateProxy<IRequest>(PortalService, expectedPath);
        using IDisposable subscription = await request.WatchResponseAsync(
            response => completion.TrySetResult(new PortalResponse(response.Response, response.Results)),
            exception => completion.TrySetException(exception)
        );
        ObjectPath returnedPath = await invoke(token);
        if (returnedPath != expectedPath)
        {
            throw new InvalidOperationException("The desktop portal returned an unexpected request handle.");
        }

        PortalResponse result;
        try
        {
            result = await completion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await request.CloseAsync();
            }
            catch (DBusException)
            {
                // The portal may have closed the request while shutdown was in progress.
            }

            throw;
        }
        return result.ResponseCode switch
        {
            0 => result,
            1 => throw new NotSupportedException("Wayland screen sharing was canceled."),
            _ => throw new NotSupportedException("The Wayland desktop denied screen sharing."),
        };
    }

    /// <summary>Reads a reusable portal token, tolerating inaccessible saved state.</summary>
    private string? TryReadRestoreToken()
    {
        try
        {
            return WaylandCaptureSourceSelection.ReadRestoreToken(dataDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"Wayland capture ({capturePurpose}): saved source selection could not be read. "
                    + CaptureFailureDiagnostics.Describe(exception)
            );
            return null;
        }
    }

    private bool TryConsumeReselectionRequest()
    {
        try
        {
            return WaylandCaptureSourceSelection.ConsumeReselectionRequest(dataDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"Wayland capture ({capturePurpose}): the source-reselection request could not be cleared. "
                    + CaptureFailureDiagnostics.Describe(exception)
            );
            return true;
        }
    }

    /// <summary>Persists the token returned by the desktop portal for later sessions.</summary>
    private void SaveRestoreToken(IDictionary<string, object> results)
    {
        string? token = results.TryGetValue("restore_token", out object? value) ? value as string : null;
        try
        {
            WaylandCaptureSourceSelection.StoreRestoreToken(dataDirectory, token);
            log?.Invoke(
                $"Wayland capture ({capturePurpose}): "
                    + (token is null ? "desktop did not retain this selection." : "saved selection renewed.")
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"Wayland capture ({capturePurpose}): selected source could not be saved for reuse. "
                    + CaptureFailureDiagnostics.Describe(exception)
            );
        }
    }

    private static Dictionary<string, object> AddRequestToken(IReadOnlyDictionary<string, object> options, string token)
    {
        var requestOptions = new Dictionary<string, object>(options) { ["handle_token"] = token };
        return requestOptions;
    }

    private static Dictionary<string, object> RequestOptions(string token) => new() { ["handle_token"] = token };

    private static string NextToken(string prefix)
    {
        int sequence = Interlocked.Increment(ref nextToken);
        return $"srvsurvey_{prefix}_{Environment.ProcessId}_{sequence}";
    }

    private static string NormalizeSenderName(string localName) => localName.TrimStart(':').Replace('.', '_');

    private static string DescribeAvailableSources(uint sourceTypes)
    {
        var sources = new List<string>(2);
        if ((sourceTypes & 1U) != 0)
        {
            sources.Add("monitor");
        }

        if ((sourceTypes & 2U) != 0)
        {
            sources.Add("window");
        }

        return string.Join(" and ", sources);
    }

    private static string DescribeBounds(PixelRect bounds) =>
        $"{bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})";

    private static string ReadRequiredString(IDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out object? value) || value is not string text || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException($"The Wayland portal response did not include {key}.");
        }

        return text;
    }

    private static void ValidateBounds(PixelRect bounds, PixelRect sourceBounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds must have a positive size.");
        }
    }

    private sealed record CaptureRequest(
        PixelRect Bounds,
        PixelRect SourceBounds,
        TaskCompletionSource<CapturedPixelBuffer> Completion
    );

    private sealed record PortalResponse(uint ResponseCode, IDictionary<string, object> Results);
}

internal readonly record struct PortalStreamInfo(
    uint NodeId,
    uint SourceType,
    PixelPoint? Position,
    PixelSize? Size,
    ulong? PipeWireSerial = null
)
{
    public uint TargetNodeId => PipeWireSerial.HasValue ? uint.MaxValue : NodeId;

    public string? TargetObjectName => PipeWireSerial?.ToString(CultureInfo.InvariantCulture);

    public string DescribeSource()
    {
        string source = SourceType switch
        {
            1U => "a monitor",
            2U => "a window",
            _ => "an unspecified source",
        };
        string position = Position is { } point ? $" at ({point.X},{point.Y})" : string.Empty;
        string size = Size is { } dimensions ? $" sized {dimensions.Width}x{dimensions.Height}" : string.Empty;
        return source + position + size;
    }

    public static PortalStreamInfo Read(IDictionary<string, object> results)
    {
        if (
            !results.TryGetValue("streams", out object? value)
            || value is not ValueTuple<uint, IDictionary<string, object>>[] streams
        )
        {
            throw new InvalidDataException("The Wayland portal did not return a screen capture stream.");
        }

        if (streams.Length != 1)
        {
            throw new InvalidDataException("Select exactly one Elite Dangerous window for screen capture.");
        }

        (uint nodeId, IDictionary<string, object> properties) = streams[0];
        uint sourceType =
            properties.TryGetValue("source_type", out object? sourceValue) && sourceValue is uint source ? source : 0U;
        return new PortalStreamInfo(
            nodeId,
            sourceType,
            ReadPoint(properties, "position"),
            ReadSize(properties, "size"),
            ReadUnsignedLong(properties, PipeWirePortalMetadata.SerialPropertyName)
        );
    }

    private static PixelPoint? ReadPoint(IDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) && value is ValueTuple<int, int> point
            ? new PixelPoint(point.Item1, point.Item2)
            : null;

    private static PixelSize? ReadSize(IDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) && value is ValueTuple<int, int> size
            ? new PixelSize(size.Item1, size.Item2)
            : null;

    private static ulong? ReadUnsignedLong(IDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) && value is ulong number ? number : null;
}

internal static class PortalFrameCropper
{
    private const int BytesPerPixel = 4;

    /// <summary>Crops a requested game region from the shared portal frame.</summary>
    public static CapturedPixelBuffer Crop(
        VideoFrame frame,
        PortalStreamInfo stream,
        PixelRect bounds,
        PixelRect sourceBounds,
        IReadOnlyList<PixelRect>? x11MonitorBounds = null
    )
    {
        if (frame.Data.IsEmpty || frame.Width <= 0 || frame.Height <= 0 || frame.Stride <= 0)
        {
            throw new InvalidDataException("The Wayland portal returned an unreadable video frame.");
        }

        if (frame.Format is not (PixelFormat.Bgra or PixelFormat.Bgrx or PixelFormat.Rgba or PixelFormat.Rgbx))
        {
            throw new InvalidDataException($"The Wayland portal returned unsupported {frame.Format} pixels.");
        }

        PixelRect sourceRectangle = ResolveSourceRectangle(stream, sourceBounds, x11MonitorBounds);
        PixelRect crop = ScaleAndClip(bounds, sourceRectangle, frame.Width, frame.Height);
        byte[] target = new byte[checked(crop.Width * crop.Height * BytesPerPixel)];
        for (int y = 0; y < crop.Height; y++)
        {
            int sourceOffset = checked(((crop.Y + y) * frame.Stride) + (crop.X * BytesPerPixel));
            ReadOnlySpan<byte> sourceRow = frame.Data.Slice(sourceOffset, checked(crop.Width * BytesPerPixel));
            Span<byte> targetRow = target.AsSpan(y * crop.Width * BytesPerPixel, crop.Width * BytesPerPixel);
            ConvertRow(sourceRow, targetRow, frame.Format);
        }

        return new CapturedPixelBuffer(crop.Width, crop.Height, target);
    }

    internal static PixelRect ScaleAndClip(PixelRect bounds, PixelRect source, int frameWidth, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        double scaleX = (double)frameWidth / source.Width;
        double scaleY = (double)frameHeight / source.Height;
        int left = (int)Math.Floor((bounds.X - source.X) * scaleX);
        int top = (int)Math.Floor((bounds.Y - source.Y) * scaleY);
        int right = (int)Math.Ceiling((bounds.Right - source.X) * scaleX);
        int bottom = (int)Math.Ceiling((bounds.Bottom - source.Y) * scaleY);
        left = Math.Clamp(left, 0, frameWidth);
        top = Math.Clamp(top, 0, frameHeight);
        right = Math.Clamp(right, 0, frameWidth);
        bottom = Math.Clamp(bottom, 0, frameHeight);
        if (right <= left || bottom <= top)
        {
            throw new InvalidDataException("The requested game area is outside the shared Wayland source.");
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>Maps portal monitor metadata to the matching X11 coordinate space.</summary>
    private static PixelRect ResolveSourceRectangle(
        PortalStreamInfo stream,
        PixelRect gameBounds,
        IReadOnlyList<PixelRect>? x11MonitorBounds
    )
    {
        const uint MonitorSource = 1;
        if (stream.SourceType == MonitorSource && stream.Position is { } position && stream.Size is { } size)
        {
            var portalBounds = new PixelRect(position, size);
            if (x11MonitorBounds is not null)
            {
                PixelRect x11Bounds = x11MonitorBounds.FirstOrDefault(current =>
                    MatchesScaledMonitor(portalBounds, current)
                );
                if (x11Bounds.Width > 0)
                {
                    return x11Bounds;
                }
            }

            return portalBounds;
        }

        return gameBounds;
    }

    /// <summary>Checks whether a portal monitor and X11 monitor differ only by uniform scaling.</summary>
    private static bool MatchesScaledMonitor(PixelRect portalBounds, PixelRect x11Bounds)
    {
        if (portalBounds.Width <= 0 || portalBounds.Height <= 0 || x11Bounds.Width <= 0 || x11Bounds.Height <= 0)
        {
            return false;
        }

        double scaleX = (double)x11Bounds.Width / portalBounds.Width;
        double scaleY = (double)x11Bounds.Height / portalBounds.Height;
        return Math.Abs(scaleX - scaleY) < .01
            && Math.Abs(x11Bounds.X - portalBounds.X * scaleX) <= 2
            && Math.Abs(x11Bounds.Y - portalBounds.Y * scaleY) <= 2;
    }

    private static void ConvertRow(ReadOnlySpan<byte> source, Span<byte> target, PixelFormat format)
    {
        for (int offset = 0; offset < source.Length; offset += BytesPerPixel)
        {
            if (format is PixelFormat.Rgba or PixelFormat.Rgbx)
            {
                target[offset] = source[offset + 2];
                target[offset + 1] = source[offset + 1];
                target[offset + 2] = source[offset];
            }
            else
            {
                target[offset] = source[offset];
                target[offset + 1] = source[offset + 1];
                target[offset + 2] = source[offset + 2];
            }

            target[offset + 3] = 255;
        }
    }
}

[DBusInterface("org.freedesktop.portal.ScreenCast")]
internal interface IScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    Task<ObjectPath> SelectSourcesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);

    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);

    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(ObjectPath sessionHandle, IDictionary<string, object> options);

    Task<T> GetAsync<T>(string property);
}

internal static class ScreenCastExtensions
{
    public static Task<uint> GetAvailableSourceTypesAsync(this IScreenCast screenCast) =>
        screenCast.GetAsync<uint>("AvailableSourceTypes");

    public static Task<uint> GetVersionAsync(this IScreenCast screenCast) => screenCast.GetAsync<uint>("version");
}

[DBusInterface("org.freedesktop.portal.Request")]
internal interface IRequest : IDBusObject
{
    Task CloseAsync();

    Task<IDisposable> WatchResponseAsync(
        Action<(uint Response, IDictionary<string, object> Results)> handler,
        Action<Exception> onError
    );
}

[DBusInterface("org.freedesktop.portal.Session")]
internal interface ISession : IDBusObject
{
    Task CloseAsync();
}
