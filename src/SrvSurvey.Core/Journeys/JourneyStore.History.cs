using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyStore
{
    /// <summary>Changes only the archive flag, preserving the saved log and forward-compatible fields.</summary>
    public async Task<JourneyDocument> SetArchivedAsync(
        string frontierId,
        string fileName,
        bool archived,
        CancellationToken cancellationToken = default
    )
    {
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (JsonObject root, JourneyDocument journey) = await ReadHistoricalJourneyAsync(
                    frontierId,
                    fileName,
                    cancellationToken
                )
                .ConfigureAwait(false);
            JourneyDocument updated = journey with { IsArchived = archived };
            root["archived"] = archived;
            await WriteObjectAsync(journey.FilePath, root, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            saveLock.Release();
        }
    }

    /// <summary>Deletes a completed journey record without touching journals, notes, or screenshots.</summary>
    public async Task DeleteAsync(string frontierId, string fileName, CancellationToken cancellationToken = default)
    {
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (_, JourneyDocument journey) = await ReadHistoricalJourneyAsync(frontierId, fileName, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(journey.FilePath);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task<(JsonObject Root, JourneyDocument Journey)> ReadHistoricalJourneyAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken
    )
    {
        ValidateFolderName(frontierId, nameof(frontierId));
        string path = Path.Combine(GetJourneyDirectory(frontierId), NormalizeFileName(fileName) + JsonFileExtension);
        JsonObjectReadResult read = await ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
        JsonObject root = read.Root ?? throw new InvalidDataException(read.Error);
        JourneyDocument journey = ParseJourney(frontierId, path, root);
        if (!string.Equals(journey.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The journey belongs to another commander.");
        }
        if (journey.IsActive)
        {
            throw new InvalidOperationException("Complete this journey before archiving or deleting it.");
        }
        return (root, journey);
    }
}
