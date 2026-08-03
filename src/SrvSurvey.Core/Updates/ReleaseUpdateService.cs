namespace SrvSurvey.Core.Updates;

public enum ReleaseChannel
{
    Stable,
    Development,
}

public interface IReleaseUpdateService
{
    Task<ReleaseUpdateResult> CheckAsync(
        ReleaseVersion currentVersion,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default);
}

public sealed record ReleaseUpdateResult(
    ReleaseVersion CurrentVersion,
    ReleaseVersion? LatestVersion,
    bool IsUpdateAvailable,
    Uri ReleaseUri,
    CrossPlatformReleasePackage? Package,
    ReleaseChannel Channel);

public sealed class ReleaseUpdateService : IReleaseUpdateService
{
    public static readonly Uri DevelopmentReleaseUri = new(
        "https://github.com/Fenris159/SrvSurvey/releases");
    public static readonly Uri StableReleaseUri = new(
        "https://github.com/njthomson/SrvSurvey/releases");

    private readonly ICrossPlatformReleaseClient releaseClient;
    private readonly string? runtimeIdentifier;
    private readonly Uri developmentReleaseUri;
    private readonly Uri stableReleaseUri;

    public ReleaseUpdateService(
        ICrossPlatformReleaseClient? releaseClient = null,
        string? runtimeIdentifier = null,
        Uri? developmentReleaseUri = null,
        Uri? stableReleaseUri = null)
    {
        this.releaseClient = releaseClient ?? new CrossPlatformReleaseClient();
        this.runtimeIdentifier = runtimeIdentifier;
        this.developmentReleaseUri = developmentReleaseUri
            ?? DevelopmentReleaseUri;
        this.stableReleaseUri = stableReleaseUri ?? StableReleaseUri;
    }

    public async Task<ReleaseUpdateResult> CheckAsync(
        ReleaseVersion currentVersion,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        var currentRuntimeIdentifier = runtimeIdentifier
            ?? CrossPlatformReleaseClient.ResolveCurrentRuntimeIdentifier();
        var release = await releaseClient.GetLatestAsync(
                currentRuntimeIdentifier,
                channel,
                cancellationToken)
            .ConfigureAwait(false);
        var latestVersion = release?.Version;
        var isUpdateAvailable = latestVersion is { } available
            && available > currentVersion;
        var releaseUri = release?.ReleaseUri
            ?? (channel == ReleaseChannel.Development
                ? developmentReleaseUri
                : stableReleaseUri);
        return new ReleaseUpdateResult(
            currentVersion,
            latestVersion,
            isUpdateAvailable,
            releaseUri,
            isUpdateAvailable ? release!.Package : null,
            channel);
    }
}
