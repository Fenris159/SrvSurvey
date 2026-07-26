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
    CrossPlatformReleasePackage? Package);

public sealed class ReleaseUpdateService : IReleaseUpdateService
{
    public static readonly Uri DefaultReleaseUri = new(
        "https://github.com/Fenris159/SrvSurvey/releases");

    private readonly ICrossPlatformReleaseClient releaseClient;
    private readonly string? runtimeIdentifier;
    private readonly Uri releaseUri;

    public ReleaseUpdateService(
        ICrossPlatformReleaseClient? releaseClient = null,
        string? runtimeIdentifier = null,
        Uri? releaseUri = null)
    {
        this.releaseClient = releaseClient ?? new CrossPlatformReleaseClient();
        this.runtimeIdentifier = runtimeIdentifier;
        this.releaseUri = releaseUri ?? DefaultReleaseUri;
    }

    public async Task<ReleaseUpdateResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        var currentRuntimeIdentifier = runtimeIdentifier
            ?? CrossPlatformReleaseClient.ResolveCurrentRuntimeIdentifier();
        var release = await releaseClient.GetLatestAsync(
                currentRuntimeIdentifier,
                cancellationToken)
            .ConfigureAwait(false);
        var latestVersion = release?.Version ?? currentVersion;
        var isUpdateAvailable = latestVersion > currentVersion;
        return new ReleaseUpdateResult(
            currentVersion,
            latestVersion,
            isUpdateAvailable,
            release?.ReleaseUri ?? releaseUri,
            isUpdateAvailable ? release!.Package : null);
    }
}
