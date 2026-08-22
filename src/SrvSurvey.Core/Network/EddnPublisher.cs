using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using System.Threading.Channels;

namespace SrvSurvey.Core.Network;

public sealed class EddnApplyRequest
{
    public required IReadOnlyList<JournalEventEnvelope> JournalEvents { get; init; }

    public EliteStatus? Status { get; init; }

    public bool Enabled { get; init; }

    public bool AllowPublishing { get; init; }

    public string? JournalDirectory { get; init; }

    public string? JournalPath { get; init; }

    public bool AllowSharedData { get; init; } = true;

    public string? CommanderName { get; init; }

    public string? FrontierId { get; init; }

    public string? GameVersion { get; init; }

    public string? GameBuild { get; init; }
}

public interface IEddnPublisher
{
    Task<EddnPublicationResult> ApplyAsync(
        EddnApplyRequest request,
        CancellationToken cancellationToken = default);

    void SetEnabled(bool enabled);

    void SetSuspended(bool suspended);
}

/// <summary>
/// Application-lifetime EDDN delivery module. Commander and journal state live
/// in a replaceable <see cref="EddnSessionPublisher"/>; this module owns the
/// single durable outbox, consent generation, transport, and persistence writer.
/// </summary>
public sealed class EddnPublisher : IEddnPublisher, IEddnSessionSink, IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim applyGate = new(1, 1);
    private readonly EddnTransport transport;
    private readonly EddnOutbox outbox;
    private readonly Channel<OutboxWriteCommand> outboxWrites;
    private readonly Task outboxWriterTask;
    private readonly Action<string> log;
    private readonly string softwareVersion;
    private EddnSessionPublisher? session;
    private EddnSessionKey? sessionKey;
    private bool sharingEnabled;
    private bool publishingSuspended;
    private bool acceptingWrites = true;
    private bool disposed;
    private int disposeStarted;
    private long ingestionGeneration;
    private long consentGeneration;
    private int stagedWriteCount;

    public EddnPublisher(
        string softwareVersion,
        HttpClient? client = null,
        Uri? endpoint = null,
        string? outboxPath = null,
        Action<string>? log = null,
        Func<DateTimeOffset>? utcNow = null,
        bool automaticProcessing = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        this.softwareVersion = softwareVersion.Trim();
        this.log = log ?? (_ => { });
        transport = new EddnTransport(
            client,
            endpoint,
            $"SrvSurvey/{this.softwareVersion}");
        outbox = new EddnOutbox(
            outboxPath ?? Path.Combine(
                Path.GetTempPath(),
                "SrvSurvey",
                "eddn-outbox-v1.json"),
            transport,
            WriteLog,
            utcNow,
            automaticProcessing);
        outboxWrites = Channel.CreateBounded<OutboxWriteCommand>(
            new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        outboxWriterTask = RunOutboxWriterAsync();
    }

    public int PendingCount => outbox.pendingCount
        + Math.Max(0, Volatile.Read(ref stagedWriteCount));

    public void SetEnabled(bool enabled)
    {
        EddnSessionPublisher? currentSession;
        lock (sync)
        {
            if (disposed || Volatile.Read(ref disposeStarted) != 0)
            {
                return;
            }

            if (sharingEnabled != enabled)
            {
                sharingEnabled = enabled;
                ingestionGeneration++;
                consentGeneration++;
            }

            currentSession = session;
        }

        currentSession?.SetEnabled(enabled);
        outbox.setEnabled(enabled, discardPendingWhenDisabled: !enabled);
    }

    public void SetSuspended(bool suspended)
    {
        EddnSessionPublisher? currentSession;
        lock (sync)
        {
            if (disposed
                || Volatile.Read(ref disposeStarted) != 0
                || publishingSuspended == suspended)
            {
                return;
            }

            publishingSuspended = suspended;
            ingestionGeneration++;
            currentSession = session;
        }

        currentSession?.SetSuspended(suspended);
        outbox.setSuspended(suspended);
    }

    public async Task<EddnPublicationResult> ApplyAsync(
        EddnApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        await applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (disposed)
                {
                    return EddnPublicationResult.Empty;
                }
            }

            SetEnabled(request.Enabled);
            var currentSession = ReplaceSessionIfNeeded(request);
            if (currentSession is null)
            {
                return EddnPublicationResult.Empty;
            }

            return await currentSession.ApplyAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            applyGate.Release();
        }
    }

    internal async Task ProcessPendingAsync(
        CancellationToken cancellationToken = default)
    {
        await FlushOutboxWritesAsync(cancellationToken).ConfigureAwait(false);
        await outbox.processDue(cancellationToken).ConfigureAwait(false);
    }

    internal async Task WaitForCompanionReadsAsync(
        CancellationToken cancellationToken = default)
    {
        EddnSessionPublisher? currentSession;
        lock (sync)
        {
            currentSession = session;
        }

        if (currentSession is not null)
        {
            await currentSession.WaitForCompanionReadsAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await FlushOutboxWritesAsync(cancellationToken).ConfigureAwait(false);
    }

    bool IEddnSessionSink.TryBeginIngestion(out long generation)
    {
        lock (sync)
        {
            generation = ingestionGeneration;
            return !disposed
                && sharingEnabled
                && !publishingSuspended;
        }
    }

    bool IEddnSessionSink.TryEnqueue(
        EddnPreparedMessage prepared,
        UploadPayloadHeader header,
        long expectedGeneration,
        string eventName,
        Action? rejected)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(header);

        long acceptedConsentGeneration;
        lock (sync)
        {
            if (!acceptingWrites
                || disposed
                || !sharingEnabled
                || publishingSuspended
                || ingestionGeneration != expectedGeneration)
            {
                return false;
            }

            acceptedConsentGeneration = consentGeneration;
        }

        var item = EddnTransport.prepare(
            prepared.message,
            prepared.schemaRef,
            header);
        Interlocked.Increment(ref stagedWriteCount);
        if (outboxWrites.Writer.TryWrite(new PersistOutboxWrite(
            item,
            acceptedConsentGeneration,
            eventName,
            rejected)))
        {
            return true;
        }

        Interlocked.Decrement(ref stagedWriteCount);
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        applyGate.Wait();
        try
        {
            EddnSessionPublisher? currentSession;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                currentSession = session;
                session = null;
                sessionKey = null;
            }

            // Let the captured session finish or reject its own pending work
            // while the application-owned sink and immutable header still exist.
            currentSession?.Dispose();

            lock (sync)
            {
                disposed = true;
                acceptingWrites = false;
                sharingEnabled = false;
                ingestionGeneration++;
                consentGeneration++;
            }

            outboxWrites.Writer.TryComplete();
            try
            {
                outboxWriterTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                WriteLog(
                    "EDDN queue writer stopped during shutdown: "
                        + exception.GetBaseException().Message);
            }

            outbox.Dispose();
        }
        finally
        {
            applyGate.Release();
        }
    }

    private EddnSessionPublisher? ReplaceSessionIfNeeded(EddnApplyRequest request)
    {
        var descriptor = EddnSessionDescriptor.TryCreate(request, softwareVersion);
        EddnSessionPublisher? previousSession;
        lock (sync)
        {
            if (disposed)
            {
                return null;
            }

            if (descriptor is not null && descriptor.Key == sessionKey)
            {
                return session;
            }

            previousSession = session;
            session = null;
            sessionKey = null;
        }

        // Session disposal cancels companion reads and may flush a valid batch
        // under its captured header before the generation is advanced.
        previousSession?.Dispose();

        bool enabled;
        bool suspended;
        lock (sync)
        {
            if (disposed)
            {
                return null;
            }

            ingestionGeneration++;
            enabled = sharingEnabled;
            suspended = publishingSuspended;
        }

        if (descriptor is null)
        {
            return null;
        }

        var replacement = new EddnSessionPublisher(
            this,
            descriptor.Header,
            descriptor.JournalDirectory,
            WriteLog);
        replacement.SetEnabled(enabled);
        replacement.SetSuspended(suspended);

        var disposeReplacement = false;
        lock (sync)
        {
            if (disposed)
            {
                disposeReplacement = true;
            }
            else
            {
                session = replacement;
                sessionKey = descriptor.Key;
            }
        }

        if (disposeReplacement)
        {
            replacement.Dispose();
            return null;
        }

        return replacement;
    }

    private async Task RunOutboxWriterAsync()
    {
        await foreach (var command in outboxWrites.Reader
                           .ReadAllAsync(CancellationToken.None)
                           .ConfigureAwait(false))
        {
            if (command is FlushOutboxWrites flush)
            {
                flush.Completion.TrySetResult();
                continue;
            }

            var write = (PersistOutboxWrite)command;
            try
            {
                var persisted = IsConsentCurrent(write.ConsentGeneration)
                    && outbox.enqueue(
                        write.Item,
                        allowWhileSuspended: true);
                if (!persisted)
                {
                    write.Rejected?.Invoke();
                    if (IsConsentCurrent(write.ConsentGeneration))
                    {
                        WriteLog(
                            $"EDDN could not persist {write.EventName} for upload.");
                    }
                }
            }
            catch (Exception exception)
            {
                write.Rejected?.Invoke();
                WriteLog(
                    $"EDDN could not persist {write.EventName} for upload: "
                        + exception.Message);
            }
            finally
            {
                Interlocked.Decrement(ref stagedWriteCount);
            }
        }
    }

    private async Task FlushOutboxWritesAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await outboxWrites.Writer.WriteAsync(
                new FlushOutboxWrites(completion),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            await outboxWriterTask.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await completion.Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsConsentCurrent(long generation)
    {
        lock (sync)
        {
            return !disposed
                && sharingEnabled
                && consentGeneration == generation;
        }
    }

    private void WriteLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            log(message);
        }
        catch
        {
            // Diagnostics must never interrupt the session or durable sender.
        }
    }

    private abstract record OutboxWriteCommand;

    private sealed record PersistOutboxWrite(
        EddnQueuedMessage Item,
        long ConsentGeneration,
        string EventName,
        Action? Rejected) : OutboxWriteCommand;

    private sealed record FlushOutboxWrites(
        TaskCompletionSource Completion) : OutboxWriteCommand;

    private sealed record EddnSessionDescriptor(
        EddnSessionKey Key,
        UploadPayloadHeader Header,
        string? JournalDirectory)
    {
        internal static EddnSessionDescriptor? TryCreate(
            EddnApplyRequest request,
            string softwareVersion)
        {
            if (string.IsNullOrWhiteSpace(request.CommanderName))
            {
                return null;
            }

            var commander = request.CommanderName.Trim();
            var journalSeries = GetJournalSeriesPath(request.JournalPath);
            var key = new EddnSessionKey(
                commander.ToUpperInvariant(),
                request.FrontierId?.Trim().ToUpperInvariant() ?? string.Empty,
                NormalizePathKey(journalSeries));
            var journalDirectory = string.IsNullOrWhiteSpace(request.JournalDirectory)
                ? Path.GetDirectoryName(request.JournalPath)
                : request.JournalDirectory;
            return new EddnSessionDescriptor(
                key,
                new UploadPayloadHeader(
                    commander,
                    request.GameVersion,
                    request.GameBuild,
                    softwareVersion),
                journalDirectory);
        }

        private static string NormalizePathKey(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = Path.GetFullPath(path);
            return OperatingSystem.IsWindows()
                ? normalized.ToUpperInvariant()
                : normalized;
        }

        private static string? GetJournalSeriesPath(string? journalPath)
        {
            if (string.IsNullOrWhiteSpace(journalPath))
            {
                return null;
            }

            var filenameParts = Path.GetFileName(journalPath).Split('.');
            if (filenameParts.Length < 4
                || !int.TryParse(filenameParts[^2], out _))
            {
                return Path.GetFullPath(journalPath);
            }

            var seriesName = string.Join('.', filenameParts[..^2])
                + "."
                + filenameParts[^1];
            return Path.Combine(
                Path.GetDirectoryName(journalPath) ?? string.Empty,
                seriesName);
        }
    }

    private sealed record EddnSessionKey(
        string Commander,
        string FrontierId,
        string JournalSeries);
}

/// <summary>Small seam consumed by one immutable Commander session.</summary>
internal interface IEddnSessionSink
{
    bool TryBeginIngestion(out long generation);

    bool TryEnqueue(
        EddnPreparedMessage prepared,
        UploadPayloadHeader header,
        long expectedGeneration,
        string eventName,
        Action? rejected = null);
}

public sealed record EddnPublishedEvent(
    string EventName,
    string SchemaReference,
    bool UsesTestSchemas);

public sealed record EddnPublicationResult(
    IReadOnlyList<EddnPublishedEvent> Published,
    IReadOnlyList<string> Warnings)
{
    public static EddnPublicationResult Empty { get; } = new([], []);
}
