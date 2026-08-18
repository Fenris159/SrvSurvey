using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Desktop.Platform;

internal sealed record ApplicationInstanceRecord(
    int SchemaVersion,
    string Product,
    int ProcessId,
    long ProcessStartTimeUtcTicks,
    string ExecutablePath,
    string PipeName);

internal sealed class ApplicationInstanceRegistry : IAsyncDisposable
{
    private const int SchemaVersion = 1;
    private const int MaximumRecordBytes = 16 * 1024;
    private const int MaximumRecords = 256;
    private const string Product = "SrvSurvey.XP";
    private const string ShutdownCommand = "shutdown";
    private const string AcceptedResponse = "accepted";

    private readonly string directory;
    private readonly string recordPath;
    private readonly Func<Task> requestShutdown;
    private readonly Action<string>? log;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task listener;
    private readonly object disposalGate = new();
    private Task? disposalTask;

    public ApplicationInstanceRegistry(
        string dataDirectory,
        Func<Task> requestShutdown,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.requestShutdown = requestShutdown
            ?? throw new ArgumentNullException(nameof(requestShutdown));
        this.log = log;
        directory = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "updates",
            "instances"));
        Directory.CreateDirectory(directory);
        RestrictToCurrentUser(directory, isDirectory: true);

        using var current = Process.GetCurrentProcess();
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The running SrvSurvey executable path is unavailable.");
        var canonicalPath = ApplicationProcessPathResolver.Canonicalize(processPath);
        var startTicks = current.StartTime.ToUniversalTime().Ticks;
        var pipeName = $"SrvSurvey.XP.{current.Id}.{startTicks}.{Guid.NewGuid():N}";
        Current = new ApplicationInstanceRecord(
            SchemaVersion,
            Product,
            current.Id,
            startTicks,
            canonicalPath,
            pipeName);
        recordPath = Path.Combine(directory, $"{current.Id}-{startTicks}.json");
        WriteRecord(Current);
        listener = ListenAsync(cancellation.Token);
        TryLog(
            $"Registered update instance PID {current.Id} at '{canonicalPath}'.");
    }

    public ApplicationInstanceRecord Current { get; }

    public IReadOnlyList<ApplicationInstanceRecord> ReadOtherRecords()
    {
        var records = new List<ApplicationInstanceRecord>();
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(directory, "*.json")
                .Take(MaximumRecords)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("Could not enumerate update instance registrations: "
                + exception.Message);
            return records;
        }

        foreach (var path in paths)
        {
            if (IsCurrentRecord(path))
            {
                continue;
            }

            var record = ReadRecord(path);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    public void RemoveStale(ApplicationInstanceRecord record)
    {
        TryDelete(Path.Combine(
            directory,
            $"{record.ProcessId}-{record.ProcessStartTimeUtcTicks}.json"));
    }

    public static async Task<bool> RequestShutdownAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        if (!IsSafePipeName(pipeName))
        {
            return false;
        }

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await writer.WriteLineAsync(ShutdownCommand.AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            var response = await reader.ReadLineAsync(timeout.Token)
                .ConfigureAwait(false);
            return string.Equals(
                response,
                AcceptedResponse,
                StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new ValueTask(disposalTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await listener.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown cancels the listener.
        }
        finally
        {
            TryDelete(recordPath);
            cancellation.Dispose();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                Current.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var reader = new StreamReader(
                    pipe,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                await using var writer = new StreamWriter(
                    pipe,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };
                var command = await reader.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(command, ShutdownCommand, StringComparison.Ordinal))
                {
                    continue;
                }

                await writer.WriteLineAsync(
                        AcceptedResponse.AsMemory(),
                        cancellationToken)
                    .ConfigureAwait(false);
                log?.Invoke(
                    "Accepted a verified shutdown request from another SrvSurvey instance.");
                _ = RequestShutdownSafelyAsync();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                log?.Invoke("Update instance communication failed: "
                    + exception.Message);
            }
        }
    }

    private bool IsCurrentRecord(string path)
    {
        return string.Equals(
            Path.GetFullPath(path),
            recordPath,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private ApplicationInstanceRecord? ReadRecord(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaximumRecordBytes)
            {
                TryDelete(path);
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var record = JsonSerializer.Deserialize<ApplicationInstanceRecord>(stream);
            if (IsValid(record))
            {
                return record;
            }

            TryDelete(path);
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            TryLog($"Ignored unreadable update instance record '{path}': "
                + exception.Message);
            return null;
        }
    }

    private void WriteRecord(ApplicationInstanceRecord record)
    {
        var temporaryPath = recordPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, record);
                stream.Flush(flushToDisk: true);
            }

            RestrictToCurrentUser(temporaryPath, isDirectory: false);
            File.Move(temporaryPath, recordPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task RequestShutdownSafelyAsync()
    {
        try
        {
            await requestShutdown().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or TaskCanceledException)
        {
            log?.Invoke("A cooperative instance shutdown request failed: "
                + exception.Message);
        }
    }

    private static bool IsValid(ApplicationInstanceRecord? record)
    {
        return record is not null
            && record.SchemaVersion == SchemaVersion
            && string.Equals(record.Product, Product, StringComparison.Ordinal)
            && record.ProcessId > 0
            && record.ProcessStartTimeUtcTicks > 0
            && Path.IsPathFullyQualified(record.ExecutablePath)
            && IsSafePipeName(record.PipeName);
    }

    private static bool IsSafePipeName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 200
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-');
    }

    private static void RestrictToCurrentUser(string path, bool isDirectory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                isDirectory
                    ? UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            // The registration remains non-authoritative if permissions fail.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Stale records are validated before use and can be retried later.
        }
    }

    private void TryLog(string message)
    {
        try
        {
            log?.Invoke(message);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            // Diagnostic logging must not interrupt instance coordination.
        }
    }
}
