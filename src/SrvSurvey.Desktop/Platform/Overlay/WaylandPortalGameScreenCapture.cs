using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Avalonia;
using PipeWire.NET;
using SrvSurvey.Core.Storage;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]

namespace SrvSurvey.Desktop.Platform.Overlay;

[SupportedOSPlatform("linux")]
internal sealed partial class WaylandPortalGameScreenCapture : IGameScreenCapture
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);
    private static int nextToken;

    private readonly object gate = new();
    private readonly string restoreTokenPath;
    private readonly CancellationTokenSource shutdown = new();
    private Task? initialization;
    private Connection? connection;
    private ISession? portalSession;
    private PipeWireContext? pipeWireContext;
    private PipeWireVideoCapture? pipeWireCapture;
    private PortalStreamInfo streamInfo;
    private CaptureRequest? pendingRequest;
    private bool disposed;

    public WaylandPortalGameScreenCapture()
        : this(Path.Combine(AppDataPaths.ResolveCurrent().DataDirectory, "wayland-screen-capture.token")) { }

    internal WaylandPortalGameScreenCapture(string restoreTokenPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreTokenPath);
        this.restoreTokenPath = Path.GetFullPath(restoreTokenPath);
    }

    public bool IsAvailable => !disposed;

    public string? UnavailableReason => disposed ? "The Wayland screen capture session is closed." : null;

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
        catch (OperationCanceledException exception) when (disposed)
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

    public void Dispose()
    {
        Task? initializationToWait;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            shutdown.Cancel();
            pendingRequest?.Completion.TrySetException(
                new ObjectDisposedException(nameof(WaylandPortalGameScreenCapture))
            );
            pendingRequest = null;
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
        catch (Exception exception) when (exception is not NotSupportedException)
        {
            throw new NotSupportedException("Wayland screen sharing could not start: " + exception.Message, exception);
        }
    }

    private async Task InitializeAsync()
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

        var createOptions = new Dictionary<string, object> { ["session_handle_token"] = NextToken("session") };
        PortalResponse createResponse = await InvokeRequestAsync(
            portalConnection,
            senderName,
            token => screenCast.CreateSessionAsync(AddRequestToken(createOptions, token)),
            shutdown.Token
        );
        string sessionPath = ReadRequiredString(createResponse.Results, "session_handle");
        portalSession = portalConnection.CreateProxy<ISession>(PortalService, sessionPath);

        var selectOptions = new Dictionary<string, object> { ["types"] = sourceTypes, ["multiple"] = false };
        if (portalVersion >= 2)
        {
            selectOptions["cursor_mode"] = 1U;
        }

        if (portalVersion >= 4)
        {
            selectOptions["persist_mode"] = 2U;
            string? restoreToken = TryReadRestoreToken();
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
        PortalResponse startResponse = await InvokeRequestAsync(
            portalConnection,
            senderName,
            token => screenCast.StartAsync(sessionPath, string.Empty, RequestOptions(token)),
            shutdown.Token
        );
        streamInfo = PortalStreamInfo.Read(startResponse.Results);
        SaveRestoreToken(startResponse.Results);

        using CloseSafeHandle remote = await screenCast.OpenPipeWireRemoteAsync(
            sessionPath,
            new Dictionary<string, object>()
        );
        var context = new PipeWireContext("SrvSurvey.RigDetection");
        pipeWireContext = context;
        await context.StartAsync(remote, shutdown.Token);

        var videoCapture = new PipeWireVideoCapture(context, "SrvSurvey.RigDetection");
        pipeWireCapture = videoCapture;
        videoCapture.FrameReady += OnFrameReady;
        videoCapture.Connect(
            streamInfo.NodeId,
            [PixelFormat.Bgra, PixelFormat.Bgrx, PixelFormat.Rgba, PixelFormat.Rgbx]
        );
    }

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
            request.Completion.TrySetResult(
                PortalFrameCropper.Crop(frame, streamInfo, request.Bounds, request.SourceBounds)
            );
        }
        catch (Exception exception)
        {
            request.Completion.TrySetException(exception);
        }
    }

    private async Task DisposeAsyncResources()
    {
        if (pipeWireCapture is not null)
        {
            pipeWireCapture.FrameReady -= OnFrameReady;
            await pipeWireCapture.DisposeAsync();
            pipeWireCapture = null;
        }

        if (pipeWireContext is not null)
        {
            await pipeWireContext.DisposeAsync();
            pipeWireContext = null;
        }

        if (portalSession is not null)
        {
            try
            {
                await portalSession.CloseAsync();
            }
            catch (DBusException)
            {
                // The compositor may already have closed the portal session.
            }

            portalSession = null;
        }

        connection?.Dispose();
        connection = null;
        shutdown.Dispose();
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

    private string? TryReadRestoreToken()
    {
        try
        {
            if (!File.Exists(restoreTokenPath))
            {
                return null;
            }

            string token = File.ReadAllText(restoreTokenPath).Trim();
            File.Delete(restoreTokenPath);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveRestoreToken(IDictionary<string, object> results)
    {
        if (!results.TryGetValue("restore_token", out object? value) || value is not string token)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(restoreTokenPath)!);
            File.WriteAllText(restoreTokenPath, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Persistence is optional; capture remains valid for the current session.
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

internal readonly record struct PortalStreamInfo(uint NodeId, uint SourceType, PixelPoint? Position, PixelSize? Size)
{
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
            throw new InvalidDataException("Select exactly one Elite Dangerous window for rig detection.");
        }

        (uint nodeId, IDictionary<string, object> properties) = streams[0];
        uint sourceType =
            properties.TryGetValue("source_type", out object? sourceValue) && sourceValue is uint source ? source : 0U;
        return new PortalStreamInfo(
            nodeId,
            sourceType,
            ReadPoint(properties, "position"),
            ReadSize(properties, "size")
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
}

internal static class PortalFrameCropper
{
    private const int BytesPerPixel = 4;

    public static CapturedPixelBuffer Crop(
        VideoFrame frame,
        PortalStreamInfo stream,
        PixelRect bounds,
        PixelRect sourceBounds
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

        PixelRect sourceRectangle = ResolveSourceRectangle(stream, sourceBounds);
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
            throw new InvalidDataException("The calibrated rig area is outside the shared Wayland source.");
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static PixelRect ResolveSourceRectangle(PortalStreamInfo stream, PixelRect gameBounds)
    {
        const uint MonitorSource = 1;
        if (stream.SourceType == MonitorSource && stream.Position is { } position && stream.Size is { } size)
        {
            return new PixelRect(position, size);
        }

        return gameBounds;
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
