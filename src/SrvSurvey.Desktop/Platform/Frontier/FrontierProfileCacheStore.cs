using System.Text.Json;
using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Desktop.Platform.Frontier;

public sealed class FrontierProfileCacheStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<FrontierAccountSnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        return await JsonSerializer.DeserializeAsync<FrontierAccountSnapshot>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(
        FrontierAccountSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Frontier profile cache has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        snapshot,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<IAsyncDisposable> AcquireRefreshLeaseAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Frontier profile cache has no parent directory.");
        Directory.CreateDirectory(directory);
        var leasePath = path + ".refresh.lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new RefreshLease(new FileStream(
                    leasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    useAsync: true));
            }
            catch (IOException)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(250),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed class RefreshLease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
