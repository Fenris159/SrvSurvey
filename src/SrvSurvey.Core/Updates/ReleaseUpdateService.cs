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
        CancellationToken cancellationToken = default
    );
}

public sealed record ReleaseUpdateResult(
    ReleaseVersion CurrentVersion,
    ReleaseVersion? LatestVersion,
    bool IsUpdateAvailable,
    Uri ReleaseUri,
    CrossPlatformReleasePackage? Package,
    ReleaseChannel Channel,
    string ReleaseNotes = ""
);

public sealed class ReleaseUpdateService : IReleaseUpdateService
{
    public static readonly Uri DevelopmentReleaseUri = new(
        $"https://github.com/{CrossPlatformReleaseClient.ReleaseRepository}/releases"
    );
    public static readonly Uri StableReleaseUri = new(
        $"https://github.com/{CrossPlatformReleaseClient.ReleaseRepository}/releases"
    );

    private readonly ICrossPlatformReleaseClient releaseClient;
    private readonly string? runtimeIdentifier;
    private readonly Uri developmentReleaseUri;
    private readonly Uri stableReleaseUri;

    public ReleaseUpdateService(
        ICrossPlatformReleaseClient? releaseClient = null,
        string? runtimeIdentifier = null,
        Uri? developmentReleaseUri = null,
        Uri? stableReleaseUri = null
    )
    {
        this.releaseClient = releaseClient ?? new CrossPlatformReleaseClient();
        this.runtimeIdentifier = runtimeIdentifier;
        this.developmentReleaseUri = developmentReleaseUri ?? DevelopmentReleaseUri;
        this.stableReleaseUri = stableReleaseUri ?? StableReleaseUri;
    }

    public async Task<ReleaseUpdateResult> CheckAsync(
        ReleaseVersion currentVersion,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        string currentRuntimeIdentifier =
            runtimeIdentifier ?? CrossPlatformReleaseClient.ResolveCurrentRuntimeIdentifier();
        CrossPlatformRelease? release = await releaseClient
            .GetLatestAsync(currentRuntimeIdentifier, channel, cancellationToken)
            .ConfigureAwait(false);
        ReleaseVersion? latestVersion = release?.Version;
        bool isUpdateAvailable = latestVersion is { } available && available > currentVersion;
        Uri releaseUri =
            release?.ReleaseUri ?? (channel == ReleaseChannel.Development ? developmentReleaseUri : stableReleaseUri);
        return new ReleaseUpdateResult(
            currentVersion,
            latestVersion,
            isUpdateAvailable,
            releaseUri,
            isUpdateAvailable ? release!.Package : null,
            channel,
            isUpdateAvailable ? release!.ReleaseNotes : string.Empty
        );
    }
}
