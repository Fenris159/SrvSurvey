namespace SrvSurvey.Core.Updates;

public interface IReleaseUpdateService
{
    Task<ReleaseUpdateResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);
}

public sealed record ReleaseUpdateResult(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    Uri ReleaseUri,
    PublishedDataIndex PublishedData);

public sealed class ReleaseUpdateService : IReleaseUpdateService
{
    public static readonly Uri DefaultReleaseUri = new(
        "https://github.com/njthomson/SrvSurvey/releases");

    private readonly IPublishedDataIndexClient indexClient;
    private readonly Uri releaseUri;

    public ReleaseUpdateService(
        IPublishedDataIndexClient? indexClient = null,
        Uri? releaseUri = null)
    {
        this.indexClient = indexClient ?? new PublishedDataIndexClient();
        this.releaseUri = releaseUri ?? DefaultReleaseUri;
    }

    public async Task<ReleaseUpdateResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        var publishedData = await indexClient.GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ReleaseUpdateResult(
            currentVersion,
            publishedData.GitHubVersion,
            publishedData.GitHubVersion > currentVersion,
            releaseUri,
            publishedData);
    }
}
