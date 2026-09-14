using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PipeWire.NET.Generated;

namespace PipeWire.NET;

/// <summary>
/// Manages the PipeWire thread-loop, context, and daemon connection.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <c>pw_thread_loop</c>, which runs the event loop on its own thread and
/// provides a recursive lock. ALL PipeWire object operations (creating streams,
/// connecting, disconnecting, destroying) must happen under that lock - take it with
/// <see cref="Lock"/> from any thread. Stream callbacks are invoked by the loop thread
/// with the lock already held, so they must not re-lock.
/// </para>
/// <para>
/// One <see cref="PipeWireContext"/> is typically shared by all streams in the process.
/// Dispose it only after all dependent streams are disposed.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class PipeWireContext : IAsyncDisposable
{
    private unsafe pw_thread_loop* _loop;
    private unsafe pw_context*     _context;
    private unsafe pw_core*        _core;
    private volatile bool          _started;
    private volatile bool          _disposed;

    /// <summary>
    /// The logger factory streams created against this context use for diagnostics. Defaults to
    /// <see cref="NullLoggerFactory"/> (no output) when none is supplied. A host that wants PipeWire
    /// stream tracing on its own <c>--verbose</c>/Debug switch passes its application factory here.
    /// </summary>
    internal ILoggerFactory LoggerFactory { get; }

    /// <summary>Initializes PipeWire and creates the thread-loop + context (not yet started).</summary>
    /// <param name="name">Context name advertised to the daemon and used as the loop-thread name.</param>
    /// <param name="loggerFactory">
    /// Optional factory for stream diagnostics. Pass the host's factory to surface PipeWire stream
    /// state transitions, format/buffer negotiation, and errors at Debug/Trace level; omit for none.
    /// </param>
    public PipeWireContext(string name = "PipeWire.NET", ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        InitializeNative(name);
    }

    private unsafe void InitializeNative(string name)
    {
        Native.pw_init(null, null);

        ReadOnlySpan<byte> nameUtf8 = Encoding.UTF8.GetBytes(name + '\0');
        fixed (byte* n = nameUtf8)
            _loop = Native.pw_thread_loop_new((sbyte*)n, null);

        if (_loop is null)
            throw new InvalidOperationException("pw_thread_loop_new failed.");

        _context = Native.pw_context_new(
            Native.pw_thread_loop_get_loop(_loop),
            props: null,
            user_data_size: 0);

        if (_context is null)
        {
            Native.pw_thread_loop_destroy(_loop);
            _loop = null;
            throw new InvalidOperationException("pw_context_new failed.");
        }
    }

    /// <summary>
    /// Starts the loop thread and connects to the daemon. Idempotent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the loop fails to start or the daemon connection fails (daemon not running).
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_started) return Task.CompletedTask;

        StartNative();
        _started = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the loop thread and connects through a permission-scoped PipeWire remote supplied by
    /// an XDG desktop portal. PipeWire takes ownership of the descriptor.
    /// </summary>
    public Task StartAsync(int remoteFileDescriptor, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(remoteFileDescriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (_started)
        {
            return Task.CompletedTask;
        }

        StartNative(remoteFileDescriptor);
        _started = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the loop through a permission-scoped portal descriptor while preserving ownership of
    /// the supplied safe handle. PipeWire owns the duplicate created by this method.
    /// </summary>
    public Task StartAsync(SafeHandle remoteFileDescriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteFileDescriptor);
        ObjectDisposedException.ThrowIf(remoteFileDescriptor.IsClosed, remoteFileDescriptor);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_started) return Task.CompletedTask;

        bool addedReference = false;
        try
        {
            remoteFileDescriptor.DangerousAddRef(ref addedReference);
            int duplicate = DuplicateFileDescriptor(checked((int)remoteFileDescriptor.DangerousGetHandle()));
            if (duplicate < 0)
                throw new InvalidOperationException("Could not duplicate the desktop portal PipeWire descriptor.");

            try
            {
                return StartAsync(duplicate, cancellationToken);
            }
            catch
            {
                _ = CloseFileDescriptor(duplicate);
                throw;
            }
        }
        finally
        {
            if (addedReference)
                remoteFileDescriptor.DangerousRelease();
        }
    }

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int DuplicateFileDescriptor(int fileDescriptor);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseFileDescriptor(int fileDescriptor);

    private unsafe void StartNative()
    {
        if (Native.pw_thread_loop_start(_loop) < 0)
            throw new InvalidOperationException("pw_thread_loop_start failed.");

        // Connecting touches the loop's objects -> must hold the loop lock.
        Native.pw_thread_loop_lock(_loop);
        try
        {
            _core = Native.pw_context_connect(_context, properties: null, user_data_size: 0);
        }
        finally
        {
            Native.pw_thread_loop_unlock(_loop);
        }

        if (_core is null)
            throw new InvalidOperationException(
                "pw_context_connect failed. Ensure the PipeWire daemon is running " +
                "(pipewire.service / wireplumber.service).");
    }

    private unsafe void StartNative(int remoteFileDescriptor)
    {
        if (Native.pw_thread_loop_start(_loop) < 0)
        {
            throw new InvalidOperationException("pw_thread_loop_start failed.");
        }

        Native.pw_thread_loop_lock(_loop);
        try
        {
            _core = Native.pw_context_connect_fd(
                _context,
                remoteFileDescriptor,
                properties: null,
                user_data_size: 0
            );
        }
        finally
        {
            Native.pw_thread_loop_unlock(_loop);
        }

        if (_core is null)
        {
            throw new InvalidOperationException("pw_context_connect_fd failed for the desktop portal stream.");
        }
    }

    /// <summary>
    /// Acquires the thread-loop lock. Dispose the returned scope to release it.
    /// Hold this around every PipeWire object operation invoked from outside a callback.
    /// </summary>
    public LoopLock Lock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LoopLock(this);
    }

    internal unsafe pw_core* CoreHandle
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _core; }
    }

    internal unsafe pw_thread_loop* LoopHandle
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _loop; }
    }

    internal unsafe void LockRaw()   => Native.pw_thread_loop_lock(_loop);
    internal unsafe void UnlockRaw() => Native.pw_thread_loop_unlock(_loop);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        DisposeNative();
        return ValueTask.CompletedTask;
    }

    private unsafe void DisposeNative()
    {
        // Stopping the loop joins its thread, so no callback can be in flight afterwards.
        if (_loop is not null && _started)
            Native.pw_thread_loop_stop(_loop);

        if (_core is not null)
        {
            Native.pw_core_disconnect(_core);
            _core = null;
        }
        if (_context is not null)
        {
            Native.pw_context_destroy(_context);
            _context = null;
        }
        if (_loop is not null)
        {
            Native.pw_thread_loop_destroy(_loop);
            _loop = null;
        }
    }

    /// <summary>
    /// RAII scope holding the PipeWire thread-loop lock. Created by <see cref="Lock"/>.
    /// </summary>
    public readonly ref struct LoopLock
    {
        private readonly PipeWireContext _ctx;

        internal LoopLock(PipeWireContext ctx)
        {
            _ctx = ctx;
            _ctx.LockRaw();
        }

        /// <summary>Releases the loop lock.</summary>
        public void Dispose() => _ctx.UnlockRaw();
    }
}
