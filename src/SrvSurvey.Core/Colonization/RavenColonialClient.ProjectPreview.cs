using System.Net;

namespace SrvSurvey.Core.Colonization;

/// <summary>Reads public build data without exposing Raven editing or publishing operations.</summary>
public interface IRavenColonialProjectReader
{
    /// <summary>Reads one public build snapshot and propagates cancellation when its preview closes.</summary>
    Task<ColonizationProjectPreviewData?> ReadProjectPreviewAsync(string buildId, CancellationToken cancellationToken);
}

public sealed partial class RavenColonialClient
{
    /// <summary>Loads a build and its public carrier and delivery statistics endpoints using bounded GET requests.</summary>
    public async Task<ColonizationProjectPreviewData?> ReadProjectPreviewAsync(
        string buildId,
        CancellationToken cancellationToken
    )
    {
        ColonizationProject? project = await GetProjectAsync(buildId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        string path = $"api/project/{Uri.EscapeDataString(buildId.Trim())}";
        Task<Dictionary<string, Dictionary<string, int>>?> cargoTask = ReadPreviewSectionAsync<
            Dictionary<string, Dictionary<string, int>>
        >($"{path}/fc/", cancellationToken);
        Task<ColonizationProjectStatistics?> statisticsTask = ReadPreviewSectionAsync<ColonizationProjectStatistics>(
            $"{path}/stats",
            cancellationToken
        );
        await Task.WhenAll(cargoTask, statisticsTask).ConfigureAwait(false);
        return new ColonizationProjectPreviewData(
            project,
            await cargoTask.ConfigureAwait(false),
            await statisticsTask.ConfigureAwait(false)
        );
    }

    /// <summary>Allows a failed optional section to be shown as unavailable without discarding the remaining live build data.</summary>
    private async Task<T?> ReadPreviewSectionAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri(path));
            using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return await ReadRequiredAsync<T>(response, "load a build preview", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or InvalidDataException
                || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested
            )
        {
            return null;
        }
    }
}
