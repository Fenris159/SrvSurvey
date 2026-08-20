using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.Platform;

public sealed class BoxelClipboardAdapter : IBoxelClipboard
{
    private readonly Lock sync = new();
    private Func<string, Task>? writer;

    public bool IsReady
    {
        get
        {
            lock (sync)
            {
                return writer is not null;
            }
        }
    }

    public void SetWriter(Func<string, Task>? value)
    {
        lock (sync)
        {
            writer = value;
        }
    }

    public Task WriteTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();
        Func<string, Task>? currentWriter;
        lock (sync)
        {
            currentWriter = writer;
        }

        return currentWriter is null
            ? Task.FromException(new InvalidOperationException(
                "The desktop clipboard is not available."))
            : currentWriter(text);
    }
}
