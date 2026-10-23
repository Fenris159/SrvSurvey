using System.Threading.Channels;

namespace SrvSurvey.Core.Journal;

/// <summary>Coalesces companion and journal notifications; periodic polling remains the recovery path.</summary>
public sealed class JournalDirectoryChangeSignal : IDisposable
{
    private readonly Channel<byte> changes = Channel.CreateBounded<byte>(1);
    private readonly List<FileSystemWatcher> watchers = [];

    public JournalDirectoryChangeSignal(IReadOnlyList<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        foreach (string directory in directories)
        {
            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.Error += OnError;
                watchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception)
                when (exception is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Missing/unavailable folders or native watch failures retain normal polling.
            }
        }
    }

    public async Task WaitAsync(TimeSpan fallbackInterval, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(fallbackInterval);
        try
        {
            await changes.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The fallback poll also recovers from missed notifications and partial writes.
        }
    }

    public void Dispose()
    {
        foreach (FileSystemWatcher watcher in watchers)
        {
            watcher.Dispose();
        }
        watchers.Clear();
        changes.Writer.TryComplete();
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        string? name = args.Name;
        if (name is not null && IsRelevant(name))
        {
            changes.Writer.TryWrite(0);
        }
    }

    private void OnError(object sender, ErrorEventArgs args) => changes.Writer.TryWrite(0);

    internal static bool IsRelevant(string name)
    {
        return name.Equals("Status.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cargo.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NavRoute.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ShipLocker.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Market.json", StringComparison.OrdinalIgnoreCase)
            || (
                name.StartsWith("Journal.", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            );
    }
}
