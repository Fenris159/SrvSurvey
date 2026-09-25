using System.Text.Json;

namespace SrvSurvey.Core.Search;

public sealed class FallbackSystemNameSuggestionClient(
    ISystemNameSuggestionClient primary,
    ISystemNameSuggestionClient fallback
) : ISystemNameSuggestionClient, IDisposable
{
    public void Dispose()
    {
        (primary as IDisposable)?.Dispose();
        (fallback as IDisposable)?.Dispose();
    }

    public async Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await primary.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested
                && exception
                    is HttpRequestException
                        or OperationCanceledException
                        or InvalidDataException
                        or JsonException
            )
        {
            return await fallback.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
    }
}
