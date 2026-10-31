using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyStore
{
    internal async Task<JourneyLoadResult> UpgradeAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken
    )
    {
        ValidateFolderName(frontierId, nameof(frontierId));
        string path = Path.Combine(GetJourneyDirectory(frontierId), NormalizeFileName(fileName) + JsonFileExtension);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObjectReadResult read = await ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
            if (read.Root is not { } root)
            {
                return new(path, read.Exists, null, read.Error);
            }
            JourneyDocument journey;
            try
            {
                journey = ParseJourney(frontierId, path, root);
            }
            catch (InvalidDataException exception)
            {
                return new(path, true, null, exception.Message);
            }
            if (
                GetSchemaVersion(root, path) == CurrentSchemaVersion
                && GetString(root, "startingJournal") == journey.StartingJournal
            )
            {
                return new(path, true, journey, null);
            }
            try
            {
                await BackUpLegacyJourneyAsync(path, cancellationToken).ConfigureAwait(false);
                // Both legacy star-reference representations remain supported. Only metadata needs conversion.
                root["schemaVersion"] = CurrentSchemaVersion;
                root["startingJournal"] = journey.StartingJournal;
                await WriteObjectAsync(path, root, cancellationToken).ConfigureAwait(false);
                return new(path, true, journey, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(
                    path,
                    true,
                    journey,
                    $"The saved journey remains usable, but its format upgrade could not be saved: {path}. {exception.Message}"
                );
            }
        }
        finally
        {
            saveLock.Release();
        }
    }

    internal static string NormalizeReference(string reference) =>
        NormalizeFileName(PortableFileName(reference.Trim()));

    private static string PortableFileName(string path) => Path.GetFileName(path.Replace('\\', '/'));

    private static int GetSchemaVersion(JsonObject root, string path)
    {
        if (root["schemaVersion"] is null)
        {
            return 0;
        }
        if (
            root["schemaVersion"] is JsonValue value
            && value.TryGetValue<int>(out int version)
            && version >= 0
            && version <= CurrentSchemaVersion
        )
        {
            return version;
        }
        throw new InvalidDataException(
            $"The journey {path} has an unsupported schemaVersion. Its saved file was kept."
        );
    }

    private static async Task BackUpLegacyJourneyAsync(string path, CancellationToken cancellationToken)
    {
        string backup = path + ".pre-upgrade.bak";
        if (File.Exists(backup))
        {
            return;
        }
        string temporary = backup + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var source = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            await using (
                var destination = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous
                )
            )
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            try
            {
                File.Move(temporary, backup);
            }
            catch (IOException) when (File.Exists(backup))
            {
                // A concurrent reader already retained the original. Never replace that backup.
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
