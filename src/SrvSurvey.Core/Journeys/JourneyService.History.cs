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
            await journeyStore.DeleteAsync(frontierId, fileName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }
}
