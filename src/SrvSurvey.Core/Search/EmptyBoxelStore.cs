using System.Text.Json;

namespace SrvSurvey.Core.Search;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The store is application-scoped and its semaphore may still have in-flight waiters.")]
public sealed class EmptyBoxelStore : IBoxelEmptyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string emptyBoxelDirectory;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public EmptyBoxelStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        emptyBoxelDirectory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "emptyBoxels");
    }

    public async Task<bool> IsEmptyAsync(
        BoxelAddress boxel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        var emptyBoxels = await LoadAsync(boxel, cancellationToken)
            .ConfigureAwait(false);
        return emptyBoxels.Contains(boxel.Id);
    }

    public async Task<IReadOnlySet<string>> LoadGroupAsync(
        BoxelAddress boxel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        var emptyBoxels = await LoadAsync(boxel, cancellationToken)
            .ConfigureAwait(false);
        return emptyBoxels;
    }

    public async Task<bool> SetEmptyAsync(
        BoxelAddress boxel,
        bool isEmpty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        var path = GetFilePath(boxel);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var emptyBoxels = await LoadFileAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var changed = isEmpty
                ? emptyBoxels.Add(boxel.Id)
                : emptyBoxels.Remove(boxel.Id);
            if (!changed)
            {
                return false;
            }

            Directory.CreateDirectory(emptyBoxelDirectory);
            var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                            stream,
                            emptyBoxels,
                            SerializerOptions,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public string GetFilePath(BoxelAddress boxel)
    {
        ArgumentNullException.ThrowIfNull(boxel);
        if (boxel.MassCode == BoxelAddress.MaximumMassCode)
        {
            throw new ArgumentException(
                "Mass-code h boxels cannot be stored as empty.",
                nameof(boxel));
        }

        var group = boxel.WithSystemNumber(0);
        while (group.MassCode < 'g')
        {
            group = group.Parent;
        }

        var fileName = group.Name + ".json";
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidDataException(
                $"The generated empty-boxel file name is invalid: {fileName}");
        }

        return Path.Combine(emptyBoxelDirectory, fileName);
    }

    private async Task<HashSet<string>> LoadAsync(
        BoxelAddress boxel,
        CancellationToken cancellationToken)
    {
        var path = GetFilePath(boxel);
        return await LoadFileAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> LoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<HashSet<string>>(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? throw new JsonException("The empty-boxel file contained no JSON value.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            throw new InvalidDataException(
                $"Could not read the existing empty-boxel file {path}. It was not changed.",
                exception);
        }
    }
}
