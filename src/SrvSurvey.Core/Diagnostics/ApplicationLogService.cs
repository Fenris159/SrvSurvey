using System.Globalization;
using System.Text;

namespace SrvSurvey.Core.Diagnostics;

public sealed class ApplicationLogService
{
    private const int DefaultRetainedFileCount = 10;
    private readonly Lock syncRoot = new();
    private readonly List<string> entries = [];
    private readonly TimeProvider timeProvider;
    private readonly int retainedFileCount;
    private readonly List<string> deferredFileWrites = [];
    private int fileWriteSuspensionCount;
    private string? lastWriteError;

    public ApplicationLogService(
        string dataDirectory,
        TimeProvider? timeProvider = null,
        int retainedFileCount = DefaultRetainedFileCount
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedFileCount);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.retainedFileCount = retainedFileCount;
        LogDirectory = Path.GetFullPath(Path.Combine(dataDirectory, "logs"));
        CurrentLogPath = CreateSessionFile();
        PruneOldFiles();
    }

    public event EventHandler? Changed;

    public string LogDirectory { get; }

    public string CurrentLogPath { get; }

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (syncRoot)
            {
                return entries.ToArray();
            }
        }
    }

    public string Text
    {
        get
        {
            lock (syncRoot)
            {
                return string.Join(Environment.NewLine, entries);
            }
        }
    }

    public string? LastWriteError
    {
        get
        {
            lock (syncRoot)
            {
                return lastWriteError;
            }
        }
    }

    public string Append(object? value)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{timeProvider.GetLocalNow():HH:mm:ss}: {value}");
        lock (syncRoot)
        {
            entries.Add(line);
            if (fileWriteSuspensionCount > 0)
            {
                deferredFileWrites.Add(line);
            }
            else
            {
                WriteLineWithRetry(line);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return line;
    }

    public IDisposable SuspendFileWrites()
    {
        lock (syncRoot)
        {
            fileWriteSuspensionCount++;
        }

        return new FileWriteSuspension(this);
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            entries.Clear();
        }

        Append("Logs reset");
    }

    private string CreateSessionFile()
    {
        string timestamp = timeProvider.GetLocalNow().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string stem = $"srvs-{timestamp}";
        string path = Path.Combine(LogDirectory, stem + ".txt");
        try
        {
            Directory.CreateDirectory(LogDirectory);
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(LogDirectory, $"{stem}_{suffix}.txt");
                suffix++;
            }

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            lastWriteError = exception.Message;
        }

        return path;
    }

    private void WriteLineWithRetry(string line)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine, Encoding.UTF8);
                lastWriteError = null;
                return;
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                lastException = exception;
            }
        }

        lastWriteError = lastException!.Message;
    }

    private void ResumeFileWrites()
    {
        lock (syncRoot)
        {
            if (fileWriteSuspensionCount == 0)
            {
                return;
            }

            fileWriteSuspensionCount--;
            if (fileWriteSuspensionCount > 0)
            {
                return;
            }

            foreach (string line in deferredFileWrites)
            {
                WriteLineWithRetry(line);
            }

            deferredFileWrites.Clear();
        }
    }

    private void PruneOldFiles()
    {
        try
        {
            FileInfo[] obsolete = Directory
                .EnumerateFiles(LogDirectory, "*.txt")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(retainedFileCount)
                .ToArray();
            foreach (FileInfo? file in obsolete)
            {
                file.Delete();
            }
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            lastWriteError = exception.Message;
        }
    }

    private static bool IsFileSystemException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException;
    }

    private sealed class FileWriteSuspension(ApplicationLogService owner) : IDisposable
    {
        private ApplicationLogService? owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.ResumeFileWrites();
        }
    }
}
