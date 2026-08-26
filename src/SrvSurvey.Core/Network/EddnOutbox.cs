using Newtonsoft.Json;
using SrvSurvey.Core.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Network
{
    /// <summary>
    /// A durable, ordered EDDN replay queue modelled after EDMC's sender queue.
    /// Messages are persisted before the first network attempt and transient
    /// failures wait at least one minute before another attempt.
    /// </summary>
    internal sealed class EddnOutbox : IDisposable
    {
        private static readonly TimeSpan startupDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan sendSpacing = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan minimumRetryDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan maximumRetryDelay = TimeSpan.FromMinutes(30);
        private const int defaultMaximumPendingMessages = 4096;
        private const long defaultMaximumStoreBytes = 64L * 1024 * 1024;

        private readonly string filepath;
        private readonly string storeFolder;
        private readonly string ownershipPath;
        private readonly string sharedDisablePath;
        private readonly string sharedDisableLeasePath;
        private readonly EddnTransport transport;
        private readonly Action<string> log;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly UploadSuccessLogAggregator successfulUploads;
        private readonly bool automaticProcessing;
        private readonly int maximumPendingMessages;
        private readonly long maximumStoreBytes;
        private readonly object sync = new();
        private readonly object sharedConsentSync = new();
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "The durable queue worker may release this gate after Dispose returns.")]
        private readonly SemaphoreSlim processing = new(1, 1);
        private readonly System.Threading.Timer timer;
        private readonly CancellationTokenSource shutdown = new();
        private readonly FileSystemWatcher? sharedConsentWatcher;
        private CancellationTokenSource activityCancellation = new();
        private List<EddnQueuedMessage> pending;
        private readonly Dictionary<Guid, long> persistedBytes = [];
        private readonly HashSet<Guid> loadCycleIds = [];
        private long storeBytes;
        private FileStream? ownershipLease;
        private FileStream? sharedDisableLease;
        private bool? requestedEnabled;
        private bool enabled;
        private bool suspended;
        private bool loadingTruncated;
        private bool ownershipWarningReported;
        private volatile bool disposed;

        internal EddnOutbox(
            string filepath,
            EddnTransport transport,
            Action<string>? log = null,
            Func<DateTimeOffset>? utcNow = null,
            bool automaticProcessing = true,
            int maximumPendingMessages = defaultMaximumPendingMessages,
            long maximumStoreBytes = defaultMaximumStoreBytes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filepath);
            ArgumentNullException.ThrowIfNull(transport);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                maximumPendingMessages);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                maximumStoreBytes);

            this.filepath = filepath;
            storeFolder = filepath + ".d";
            ownershipPath = getOwnershipPath(filepath);
            sharedDisablePath = ownershipPath + ".sharing-disabled";
            sharedDisableLeasePath = sharedDisablePath + ".lease";
            this.transport = transport;
            this.log = log ?? (_ => { });
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            successfulUploads = new UploadSuccessLogAggregator(this.utcNow);
            this.automaticProcessing = automaticProcessing;
            this.maximumPendingMessages = maximumPendingMessages;
            this.maximumStoreBytes = maximumStoreBytes;
            pending = [];
            timer = new System.Threading.Timer(
                _ => triggerProcessing(),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            List<string> ownershipLogs = [];
            lock (sync)
            {
                tryAcquireOwnershipLocked(ownershipLogs);
            }

            sharedConsentWatcher = createSharedConsentWatcher(ownershipLogs);

            writeLogs(ownershipLogs);
        }

        internal int pendingCount
        {
            get
            {
                lock (sync) return pending.Count;
            }
        }

        internal bool hasExclusiveOwnership
        {
            get
            {
                lock (sync) return ownershipLease is not null;
            }
        }

        internal void setEnabled(bool value, bool discardPendingWhenDisabled)
        {
            lock (sharedConsentSync)
            {
                bool requestedStateChanged;
                lock (sync)
                {
                    if (disposed) return;
                    requestedStateChanged = requestedEnabled != value;
                    requestedEnabled = value;
                }

                List<string> markerLogs = [];
                if (!value)
                {
                    addLog(markerLogs, acquireSharedDisableLease());
                    if (requestedStateChanged
                        || !isSharedConsentDisabled())
                    {
                        addLog(markerLogs, writeSharedDisableMarker());
                    }
                }
                else
                {
                    releaseSharedDisableLease();
                    if (requestedStateChanged)
                    {
                        addLog(markerLogs, tryClearSharedDisableMarker());
                    }
                }

                writeLogs(markerLogs);
                var sharedDisabled = isSharedConsentDisabled();
                applyEnabledState(
                    value && !sharedDisabled,
                    discardPendingWhenDisabled || sharedDisabled);
            }
        }

        private void applyEnabledState(
            bool value,
            bool discardPendingWhenDisabled)
        {
            var changed = false;
            string? persistenceLog = null;
            string? sharingLog = null;
            CancellationTokenSource? cancellation = null;
            List<string> ownershipLogs = [];
            var canSchedule = false;
            var acquiredOwnership = false;
            var shouldReleaseOwnership = false;
            lock (sync)
            {
                if (disposed) return;
                if (value || discardPendingWhenDisabled)
                {
                    var hadOwnership = ownershipLease is not null;
                    tryAcquireOwnershipLocked(ownershipLogs);
                    acquiredOwnership = !hadOwnership
                        && ownershipLease is not null;
                }

                changed = enabled != value;
                enabled = value;
                if (!enabled)
                {
                    (cancellation, persistenceLog, sharingLog) =
                        disableLocked(discardPendingWhenDisabled);
                }

                canSchedule = enabled
                    && !suspended
                    && ownershipLease is not null;
                shouldReleaseOwnership = !enabled
                    && ownershipLease is not null;
            }

            cancellation?.Cancel();
            writeLogs(ownershipLogs);
            writeLog(persistenceLog);
            writeLog(sharingLog);
            if (shouldReleaseOwnership)
            {
                releaseOwnershipIfIdle();
            }

            if (canSchedule
                && (changed || acquiredOwnership)
                && automaticProcessing)
                schedule(startupDelay);
            else if (!canSchedule)
                stopTimer();
        }

        private (
            CancellationTokenSource Cancellation,
            string? PersistenceLog,
            string? SharingLog) disableLocked(
                bool discardPendingWhenDisabled)
        {
            var cancellation = replaceActivityCancellationLocked();
            if (!discardPendingWhenDisabled
                || ownershipLease is null
                || (pending.Count == 0 && !loadingTruncated))
            {
                return (cancellation, null, null);
            }

            var count = pending.Count;
            var includedUnloadedFiles = loadingTruncated;
            pending.Clear();
            persistedBytes.Clear();
            loadCycleIds.Clear();
            storeBytes = 0;
            loadingTruncated = false;
            var persistenceLog = deleteStore();
            var sharingLog = includedUnloadedFiles
                ? "EDDN discarded all pending uploads because sharing was disabled."
                : $"EDDN discarded {count:N0} pending upload(s) because sharing was disabled.";
            return (cancellation, persistenceLog, sharingLog);
        }

        internal void setSuspended(bool value)
        {
            CancellationTokenSource? cancellation = null;
            List<string> ownershipLogs = [];
            var shouldSchedule = false;
            lock (sync)
            {
                if (disposed || suspended == value) return;
                suspended = value;
                if (suspended)
                {
                    cancellation = replaceActivityCancellationLocked();
                }
                else if (enabled)
                {
                    tryAcquireOwnershipLocked(ownershipLogs);
                    shouldSchedule = ownershipLease is not null;
                }
            }

            cancellation?.Cancel();
            writeLogs(ownershipLogs);
            if (value)
            {
                stopTimer();
            }
            else if (shouldSchedule && automaticProcessing)
            {
                schedule(TimeSpan.Zero);
            }
        }

        internal bool enqueue(
            EddnQueuedMessage message,
            bool allowWhileSuspended = false)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (isSharedConsentDisabled())
            {
                applyEnabledState(false, discardPendingWhenDisabled: true);
                return false;
            }

            string? persistenceLog = null;
            var queued = false;
            lock (sync)
            {
                if (!enabled
                    || (suspended && !allowWhileSuspended)
                    || disposed
                    || ownershipLease is null)
                {
                    return false;
                }

                if (pending.Count >= maximumPendingMessages)
                {
                    persistenceLog =
                        $"EDDN did not queue {eventName(message)} because the local backlog reached {maximumPendingMessages:N0} messages.";
                }
                else
                {
                    pending.Add(message);
                    if (!persistMessage(message, out persistenceLog))
                    {
                        pending.Remove(message);
                    }
                    else
                        queued = true;
                }
            }

            writeLog(persistenceLog);
            if (!queued) return false;

            if (automaticProcessing && !suspended) schedule(TimeSpan.Zero);
            return true;
        }

        internal async Task processDue(CancellationToken cancellationToken = default)
        {
            if (!await processing.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
            try
            {
                while (await ProcessNextDueAsync(cancellationToken).ConfigureAwait(false))
                {
                    await Task.Delay(sendSpacing, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ReleaseProcessingGate();
            }
        }

        private async Task<bool> ProcessNextDueAsync(CancellationToken cancellationToken)
        {
            if (isSharedConsentDisabled())
            {
                applyEnabledState(false, discardPendingWhenDisabled: true);
                return false;
            }

            if (!TryDequeueReadyMessage(
                    cancellationToken,
                    out var next,
                    out var combinedCancellation))
            {
                return false;
            }

            var (result, failure) = await UploadOnceAsync(
                    next!,
                    combinedCancellation!,
                    cancellationToken)
                .ConfigureAwait(false);
            return ApplyUploadOutcome(
                next!,
                result,
                failure,
                failure != null || result?.isRetryable == true);
        }

        private bool TryDequeueReadyMessage(
            CancellationToken cancellationToken,
            out EddnQueuedMessage? next,
            out CancellationTokenSource? combinedCancellation)
        {
            next = null;
            combinedCancellation = null;
            lock (sync)
            {
                if (!enabled
                    || suspended
                    || disposed
                    || ownershipLease is null)
                {
                    return false;
                }

                var now = utcNow();
                next = nextDueLocked(now);
                if (next == null)
                {
                    scheduleNextLocked(now);
                    return false;
                }

                if (next.nextAttempt > now)
                {
                    schedule(next.nextAttempt - now);
                    return false;
                }

                // Create the linked source while holding the same lock used
                // by Dispose. Once the worker leaves this block, shutdown can
                // dispose its source without racing a late token registration.
                combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    shutdown.Token,
                    activityCancellation.Token,
                    cancellationToken);
                return true;
            }
        }

        private async Task<(EddnUploadResult? Result, Exception? Failure)> UploadOnceAsync(
            EddnQueuedMessage next,
            CancellationTokenSource combinedCancellation,
            CancellationToken cancellationToken)
        {
            try
            {
                using (combinedCancellation)
                {
                    var result = await transport.upload(
                            next,
                            combinedCancellation.Token)
                        .ConfigureAwait(false);
                    return (result, null);
                }
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or OperationCanceledException)
            {
                return (null, ex);
            }
        }

        private bool ApplyUploadOutcome(
            EddnQueuedMessage next,
            EddnUploadResult? result,
            Exception? failure,
            bool retry)
        {
            string? persistenceLog = null;
            string? resultLog = null;
            List<string> reloadLogs = [];
            var continueProcessing = true;
            lock (sync)
            {
                if (!enabled || suspended || disposed)
                {
                    return false;
                }

                if (!pending.Any(item => item.id == next.id))
                {
                    return true;
                }

                if (retry)
                {
                    (persistenceLog, resultLog) = ScheduleRetryLocked(next, result, failure);
                }
                else
                {
                    (persistenceLog, resultLog) = CompleteUploadLocked(
                        next,
                        result,
                        reloadLogs);
                }
            }

            // Logging can ultimately marshal to the UI. Never invoke it while
            // holding the queue lock or Settings can deadlock against this worker.
            writeLog(persistenceLog);
            writeLog(resultLog);
            writeLogs(reloadLogs);
            return continueProcessing;
        }

        private (string? PersistenceLog, string ResultLog) ScheduleRetryLocked(
            EddnQueuedMessage next,
            EddnUploadResult? result,
            Exception? failure)
        {
            next.attempts++;
            var retryAt = utcNow() + getRetryDelay(next.attempts);
            next.nextAttempt = retryAt;
            persistMessage(next, out var persistenceLog);
            var detail = failure?.Message
                ?? result?.responseDetail
                ?? result?.reasonPhrase
                ?? "request failed";
            var resultLog =
                $"EDDN upload for {eventName(next)} will retry after {retryAt:u}: {singleLine(detail)}";
            scheduleNextLocked(utcNow());
            return (persistenceLog, resultLog);
        }

        private (string? PersistenceLog, string? ResultLog) CompleteUploadLocked(
            EddnQueuedMessage next,
            EddnUploadResult? result,
            List<string> reloadLogs)
        {
            pending.RemoveAll(item => item.id == next.id);
            var persistenceLog = deleteMessage(next);
            if (pending.Count == 0)
            {
                if (loadingTruncated)
                {
                    pending = load(
                        reloadLogs,
                        continueTruncatedLoad: true);
                }
                else
                {
                    persistenceLog ??= deleteStore();
                }
            }

            if (result?.isSuccess == true)
            {
                var completedCount = successfulUploads.Record(1);
                var messageLabel = completedCount == 1
                    ? "journal message"
                    : "journal messages";
                return (
                    persistenceLog,
                    completedCount is { } count
                        ? $"EDDN uploaded {count:N0} {messageLabel} in the previous 15-minute activity window using test schemas."
                        : null);
            }

            var detail = result?.skipReason
                ?? result?.responseDetail
                ?? result?.reasonPhrase
                ?? "request was rejected";
            return (
                persistenceLog,
                $"EDDN dropped {eventName(next)} without retry: {singleLine(detail)}");
        }

        private void ReleaseProcessingGate()
        {
            bool shouldReleaseOwnership;
            lock (sync)
            {
                shouldReleaseOwnership = disposed || !enabled;
            }

            if (shouldReleaseOwnership)
            {
                releaseOwnership();
            }

            processing.Release();
        }

        internal void clear()
        {
            string? persistenceLog;
            lock (sync)
            {
                if (ownershipLease is null) return;
                pending.Clear();
                persistedBytes.Clear();
                loadCycleIds.Clear();
                storeBytes = 0;
                loadingTruncated = false;
                persistenceLog = deleteStore();
            }
            writeLog(persistenceLog);
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            CancellationTokenSource idleCancellation;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                enabled = false;
                suspended = true;
                cancellation = replaceActivityCancellationLocked();
                idleCancellation = activityCancellation;
            }
            cancellation.Cancel();
            shutdown.Cancel();
            timer.Dispose();
            sharedConsentWatcher?.Dispose();

            lock (sharedConsentSync)
            {
                releaseSharedDisableLease();
            }

            cancellation.Dispose();
            idleCancellation.Dispose();
            shutdown.Dispose();

            if (processing.Wait(0, CancellationToken.None))
            {
                try
                {
                    releaseOwnership();
                }
                finally
                {
                    processing.Release();
                }
            }

            // processDue may still be between its disposed check and WaitAsync, or
            // may still need to release the semaphore. The active worker releases
            // the store lease from its finally block before another process can use it.
        }

        private void triggerProcessing()
        {
            if (disposed) return;
            processDue(shutdown.Token).ContinueWith(
                task => writeLog($"EDDN queue processing failed: {singleLine(task.Exception?.GetBaseException().Message)}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private void schedule(TimeSpan delay)
        {
            if (disposed || !automaticProcessing) return;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            try
            {
                timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Dispose won the race after the check above.
            }
        }

        private void scheduleNextLocked(DateTimeOffset now)
        {
            if (!enabled
                || suspended
                || ownershipLease is null
                || pending.Count == 0)
            {
                stopTimer();
                return;
            }

            schedule(pending.Min(item => item.nextAttempt) - now);
        }

        private EddnQueuedMessage? nextDueLocked(DateTimeOffset now)
        {
            // Give every new message one attempt in durable creation order.
            // Retried messages then use their own due time. One persistently
            // retryable payload therefore cannot block unrelated uploads.
            return pending
                .Where(item => item.nextAttempt <= now)
                .OrderBy(item => item.attempts == 0 ? 0 : 1)
                .ThenBy(item => item.attempts == 0
                    ? item.created
                    : item.nextAttempt)
                .ThenBy(item => item.created)
                .FirstOrDefault();
        }

        private List<EddnQueuedMessage> load(
            List<string> messages,
            bool continueTruncatedLoad = false)
        {
            if (!continueTruncatedLoad)
            {
                loadCycleIds.Clear();
            }

            persistedBytes.Clear();
            storeBytes = 0;
            loadingTruncated = false;
            var loaded = loadMessageFiles(messages, loadCycleIds);
            migrateLegacyStore(loaded, messages);
            foreach (var item in loaded)
            {
                loadCycleIds.Add(item.id);
            }

            if (!loadingTruncated)
            {
                loadCycleIds.Clear();
            }

            return loaded.OrderBy(item => item.created).ToList();
        }

        private void stopTimer()
        {
            try
            {
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException) when (disposed)
            {
                // Dispose won the race after the caller checked the queue state.
            }
        }

        private List<EddnQueuedMessage> loadMessageFiles(
            List<string> messages,
            HashSet<Guid> ids)
        {
            if (!Directory.Exists(storeFolder)) return [];
            List<EddnQueuedMessage> loaded = [];
            foreach (var path in Directory.EnumerateFiles(storeFolder, "*.json"))
            {
                if (loaded.Count >= maximumPendingMessages)
                {
                    loadingTruncated = true;
                    messages.Add(
                        $"EDDN stopped loading pending uploads after reaching the {maximumPendingMessages:N0}-message limit; remaining files were left unchanged.");
                    break;
                }

                try
                {
                    var length = new FileInfo(path).Length;
                    if (length > maximumStoreBytes - storeBytes)
                    {
                        loadingTruncated = true;
                        messages.Add(
                            $"EDDN stopped loading pending uploads after reaching the {maximumStoreBytes / 1024 / 1024:N0} MiB storage limit; remaining files were left unchanged.");
                        break;
                    }

                    if (length <= 0)
                    {
                        throw new InvalidDataException(
                            "the queue contained an empty entry");
                    }

                    var item = JsonConvert.DeserializeObject<EddnQueuedMessage>(
                        File.ReadAllText(path));
                    normalize(item);
                    if (!isValid(item) || !ids.Add(item!.id))
                    {
                        throw new InvalidDataException(
                            "the queue contained an invalid or duplicate entry");
                    }

                    loaded.Add(item);
                    persistedBytes[item.id] = length;
                    storeBytes += length;
                }
                catch (Exception exception) when (
                    exception is IOException
                        or JsonException
                        or UnauthorizedAccessException
                        or InvalidDataException)
                {
                    quarantine(path, messages);
                    messages.Add(
                        "EDDN could not load a pending upload: "
                            + exception.Message);
                }
            }

            return loaded;
        }

        private void migrateLegacyStore(
            List<EddnQueuedMessage> loaded,
            List<string> messages)
        {
            if (!File.Exists(filepath)) return;
            try
            {
                if (new FileInfo(filepath).Length > maximumStoreBytes)
                {
                    throw new InvalidDataException(
                        $"the queue exceeded {maximumStoreBytes / 1024 / 1024:N0} MiB");
                }

                var legacy = JsonConvert.DeserializeObject<List<EddnQueuedMessage>>(
                    File.ReadAllText(filepath)) ?? [];
                if (legacy.Count + loaded.Count > maximumPendingMessages)
                {
                    throw new InvalidDataException(
                        "the queue contained excessive entries");
                }

                if (migrateLegacyMessages(legacy, loaded, messages))
                {
                    File.Delete(filepath);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or JsonException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                quarantine(filepath, messages);
                messages.Add(
                    "EDDN could not load its legacy pending uploads: "
                        + exception.Message);
            }
        }

        private bool migrateLegacyMessages(
            IEnumerable<EddnQueuedMessage> legacy,
            List<EddnQueuedMessage> loaded,
            List<string> messages)
        {
            var ids = loaded.Select(item => item.id).ToHashSet();
            var migrated = true;
            foreach (var item in legacy)
            {
                normalize(item);
                if (!isValid(item))
                {
                    throw new InvalidDataException(
                        "the queue contained invalid entries");
                }

                if (!ids.Add(item.id)) continue;
                if (persistMessage(item, out var error))
                {
                    loaded.Add(item);
                }
                else
                {
                    migrated = false;
                    if (error is not null) messages.Add(error);
                }
            }

            return migrated;
        }

        private bool persistMessage(
            EddnQueuedMessage message,
            out string? errorLog)
        {
            errorLog = null;
            try
            {
                Directory.CreateDirectory(storeFolder);
                var json = JsonConvert.SerializeObject(message, Formatting.None);
                var bytes = Encoding.UTF8.GetByteCount(json);
                var previousBytes = persistedBytes.GetValueOrDefault(message.id);
                if (storeBytes - previousBytes + bytes > maximumStoreBytes)
                {
                    errorLog =
                        $"EDDN did not grow its local queue beyond {maximumStoreBytes / 1024 / 1024:N0} MiB.";
                    return false;
                }

                var path = messagePath(message.id);
                var temporary = path + ".tmp";
                File.WriteAllText(
                    temporary,
                    json);
                File.Move(temporary, path, true);
                persistedBytes[message.id] = bytes;
                storeBytes = storeBytes - previousBytes + bytes;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                errorLog =
                    "EDDN could not persist a pending upload: "
                        + exception.Message;
                return false;
            }
        }

        private string? deleteMessage(EddnQueuedMessage message)
        {
            try
            {
                var path = messagePath(message.id);
                if (File.Exists(path)) File.Delete(path);
                var temporary = path + ".tmp";
                if (File.Exists(temporary)) File.Delete(temporary);
                storeBytes -= persistedBytes.GetValueOrDefault(message.id);
                persistedBytes.Remove(message.id);
                return null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return "EDDN could not remove a delivered upload: "
                    + exception.Message;
            }
        }

        private string? deleteStore()
        {
            try
            {
                if (File.Exists(filepath)) File.Delete(filepath);
                var temporary = filepath + ".tmp";
                if (File.Exists(temporary)) File.Delete(temporary);
                if (Directory.Exists(storeFolder))
                {
                    Directory.Delete(storeFolder, recursive: true);
                }

                persistedBytes.Clear();
                loadCycleIds.Clear();
                storeBytes = 0;
                loadingTruncated = false;
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"EDDN could not remove its empty queue: {ex.Message}";
            }
        }

        private string messagePath(Guid id)
        {
            return Path.Combine(storeFolder, id.ToString("N") + ".json");
        }

        private static void normalize(EddnQueuedMessage? item)
        {
            item?.normalizeSchemaMode();
        }

        private static void quarantine(string path, List<string> messages)
        {
            var backup = path
                + ".bad-"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                + "-"
                + Guid.NewGuid().ToString("N");
            try
            {
                File.Move(path, backup);
                messages.Add(
                    $"EDDN moved an unreadable queue entry to: {backup}");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                messages.Add(
                    "EDDN could not preserve an unreadable queue entry: "
                        + exception.Message);
            }
        }

        private void writeLog(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            try
            {
                log(message);
            }
            catch
            {
                // Diagnostics must never stop or poison the durable upload queue.
            }
        }

        private void writeLogs(IEnumerable<string> messages)
        {
            foreach (var message in messages)
            {
                writeLog(message);
            }
        }

        private static void addLog(
            List<string> messages,
            string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) messages.Add(message);
        }

        private void tryAcquireOwnershipLocked(List<string> messages)
        {
            if (ownershipLease is not null) return;
            try
            {
                var folder = Path.GetDirectoryName(ownershipPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                ownershipLease = new FileStream(
                    ownershipPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                pending = load(messages);
                if (ownershipWarningReported)
                {
                    messages.Add(
                        "EDDN acquired the local outbox after the other SrvSurvey instance released it.");
                    ownershipWarningReported = false;
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (!ownershipWarningReported)
                {
                    messages.Add(
                        "EDDN uploads are paused because another SrvSurvey instance owns the local outbox.");
                    ownershipWarningReported = true;
                }

                return;
            }
        }

        private CancellationTokenSource replaceActivityCancellationLocked()
        {
            var previous = activityCancellation;
            activityCancellation = new CancellationTokenSource();
            return previous;
        }

        private void releaseOwnership()
        {
            FileStream? lease;
            lock (sync)
            {
                lease = ownershipLease;
                ownershipLease = null;
            }

            lease?.Dispose();
        }

        private static bool isValid(EddnQueuedMessage? message)
        {
            return message is not null
                && message.id != Guid.Empty
                && message.created != default
                && message.nextAttempt != default
                && message.attempts >= 0
                && !string.IsNullOrWhiteSpace(message.schemaRef)
                && message.schemaRef.StartsWith(
                    "https://eddn.edcd.io/schemas/",
                    StringComparison.Ordinal)
                && message.header is not null
                && !string.IsNullOrWhiteSpace(message.header.uploaderID)
                && message.message is not null
                && !string.IsNullOrWhiteSpace(
                    message.message.Value<string>("event"));
        }

        private static string getOwnershipPath(string filepath)
        {
            var normalizedPath = Path.GetFullPath(filepath);
            if (OperatingSystem.IsWindows())
            {
                normalizedPath = normalizedPath.ToUpperInvariant();
            }

            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
            return Path.Combine(
                Path.GetTempPath(),
                "SrvSurvey",
                "eddn-outbox-locks",
                hash + ".lock");
        }

        private FileSystemWatcher? createSharedConsentWatcher(
            List<string> messages)
        {
            try
            {
                var folder = Path.GetDirectoryName(sharedDisablePath);
                if (string.IsNullOrWhiteSpace(folder)) return null;
                Directory.CreateDirectory(folder);
                var watcher = new FileSystemWatcher(
                    folder,
                    Path.GetFileName(sharedDisablePath))
                {
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                };
                watcher.Created += onSharedConsentChanged;
                watcher.Changed += onSharedConsentChanged;
                watcher.Deleted += onSharedConsentChanged;
                watcher.Renamed += onSharedConsentChanged;
                watcher.EnableRaisingEvents = true;
                return watcher;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                messages.Add(
                    "EDDN could not monitor shared opt-out state: "
                        + exception.Message);
                return null;
            }
        }

        private void onSharedConsentChanged(
            object sender,
            FileSystemEventArgs eventArgs)
        {
            lock (sharedConsentSync)
            {
                bool shouldEnable;
                lock (sync)
                {
                    if (disposed) return;
                    shouldEnable = requestedEnabled == true;
                }

                var sharedDisabled = isSharedConsentDisabled();
                applyEnabledState(
                    shouldEnable && !sharedDisabled,
                    discardPendingWhenDisabled: sharedDisabled);
            }
        }

        private bool isSharedConsentDisabled()
        {
            return File.Exists(sharedDisablePath);
        }

        private string? writeSharedDisableMarker()
        {
            try
            {
                var folder = Path.GetDirectoryName(sharedDisablePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(
                    sharedDisablePath,
                    "EDDN sharing is disabled for all SrvSurvey instances.");
                return null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return "EDDN could not publish the shared opt-out state: "
                    + exception.Message;
            }
        }

        private string? acquireSharedDisableLease()
        {
            if (sharedDisableLease is not null) return null;
            try
            {
                var folder = Path.GetDirectoryName(sharedDisableLeasePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                sharedDisableLease = new FileStream(
                    sharedDisableLeasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.Read);
                return null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return "EDDN could not acquire the shared opt-out lease: "
                    + exception.Message;
            }
        }

        private void releaseSharedDisableLease()
        {
            sharedDisableLease?.Dispose();
            sharedDisableLease = null;
        }

        private string? tryClearSharedDisableMarker()
        {
            if (!File.Exists(sharedDisablePath)) return null;
            FileStream activeOptOutProbe;
            try
            {
                var folder = Path.GetDirectoryName(sharedDisableLeasePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                // Opted-out processes share read handles with one another.
                // FileShare.None is incompatible with every live reader, so
                // this probe succeeds only after all opt-out leases close.
                activeOptOutProbe = new FileStream(
                    sharedDisableLeasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.None);
            }
            catch (IOException)
            {
                // Another process still holds an active opt-out lease.
                return null;
            }
            catch (UnauthorizedAccessException exception)
            {
                return "EDDN could not inspect the shared opt-out state: "
                    + exception.Message;
            }

            using (activeOptOutProbe)
            {
                try
                {
                    if (File.Exists(sharedDisablePath))
                    {
                        File.Delete(sharedDisablePath);
                    }

                    return null;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    return "EDDN could not clear the shared opt-out state: "
                        + exception.Message;
                }
            }
        }

        private void releaseOwnershipIfIdle()
        {
            if (!processing.Wait(0, CancellationToken.None)) return;
            try
            {
                releaseOwnership();
            }
            finally
            {
                processing.Release();
            }
        }

        private static TimeSpan getRetryDelay(int attempts)
        {
            var multiplier = Math.Pow(2, Math.Clamp(attempts - 1, 0, 10));
            var delay = TimeSpan.FromTicks((long)(minimumRetryDelay.Ticks * multiplier));
            return delay > maximumRetryDelay ? maximumRetryDelay : delay;
        }

        private static string eventName(EddnQueuedMessage message)
        {
            return message.message.Value<string>("event")
                ?? message.schemaRef.Split('/').Reverse().Skip(1).FirstOrDefault()
                ?? "message";
        }

        private static string singleLine(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "request failed";
            var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= EddnTransport.MaximumResponseDetailBytes
                ? text
                : text[..EddnTransport.MaximumResponseDetailBytes];
        }
    }
}


