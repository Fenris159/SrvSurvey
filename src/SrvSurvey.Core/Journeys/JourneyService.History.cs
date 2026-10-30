using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyService
{
    public async Task<JourneyDocument> SetArchivedAsync(
        string frontierId,
        string fileName,
        bool archived,
        CancellationToken cancellationToken = default
    )
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await journeyStore
                .SetArchivedAsync(frontierId, fileName, archived, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task DeleteAsync(string frontierId, string fileName, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JourneyLoadResult load = await journeyStore
                .LoadAsync(frontierId, fileName, cancellationToken)
                .ConfigureAwait(false);
            JourneyDocument journey = load.Journey ?? throw new InvalidDataException(load.Error);
            if (!string.Equals(journey.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The journey belongs to another commander.");
            }
            if (journey.IsActive)
            {
                throw new InvalidOperationException("Complete this journey before deleting it.");
            }
            if (
                activeProcessor?.Journey.FrontierId == frontierId
                && activeProcessor.Journey.FileName == journey.FileName
            )
            {
                activeProcessor = null;
            }
            foreach (bool odyssey in new[] { true, false })
            {
                CommanderProfileLoadResult profile = await profileStore
                    .LoadAsync(frontierId, odyssey, cancellationToken)
                    .ConfigureAwait(false);
                CommanderProfileData data = profile.Data ?? throw new InvalidDataException(profile.Error);
                if (Path.GetFileNameWithoutExtension(data.ActiveJourneyFileName) == journey.FileName)
                {
                    await profileStore
                        .SaveActiveJourneyAsync(frontierId, data.CommanderName, odyssey, null, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            await journeyStore.DeleteAsync(frontierId, fileName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }
}
