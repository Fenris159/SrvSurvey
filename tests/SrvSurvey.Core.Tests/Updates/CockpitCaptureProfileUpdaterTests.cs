using System.Net;
using System.Text;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class CockpitCaptureProfileUpdaterTests : IDisposable
{
    private const string NewShader = "0123456789abcdef";
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-camera-update-{Guid.NewGuid():N}");
    private static int NextVersion => CockpitCaptureProfile.Embedded.ProfileVersion + 1;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewerValidatedProfileIsActivatedWithOneCompatiblePredecessor(bool existingCache)
    {
        int predecessor = existingCache ? NextVersion : CockpitCaptureProfile.Embedded.ProfileVersion;
        string oldShader = existingCache ? "fedcba9876543210" : CockpitCaptureProfile.Embedded.VertexShaderHash;
        int version = predecessor + 1;
        using HttpClient client = Client(Profile(version));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        if (existingCache)
        {
            Seed(updater, Profile(predecessor, oldShader));
        }
        CockpitCaptureProfileRefreshResult result = await updater.RefreshAsync(CancellationToken.None);

        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(version, CockpitCaptureProfileRefreshState.Updated),
            result
        );
        Assert.Equal(Profile(version), await File.ReadAllTextAsync(updater.CachePath));
        Assert.Equal(
            existingCache ? Profile(predecessor, oldShader) : CockpitCaptureProfile.Embedded.Serialize(),
            await File.ReadAllTextAsync(updater.CachePath + ".previous")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualAndOlderDownloadsDoNotRewriteTheWorkingProfile(bool older)
    {
        int currentVersion = NextVersion + 1;
        using HttpClient client = Client(Profile(older ? NextVersion : currentVersion));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        string original = "; retained comment\r\n" + Profile(currentVersion);
        Seed(updater, original);

        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(currentVersion, CockpitCaptureProfileRefreshState.Current),
            await updater.RefreshAsync(CancellationToken.None)
        );
        Assert.Equal(original, await File.ReadAllTextAsync(updater.CachePath));
        Assert.False(File.Exists(updater.CachePath + ".previous"));
    }

    [Fact]
    public async Task ReusingAVersionForAnotherShaderIsRejected()
    {
        using HttpClient client = Client(Profile(NextVersion, "fedcba9876543210"));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, Profile(NextVersion));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.VersionConflict),
            await updater.RefreshAsync(CancellationToken.None)
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
    }

    public static TheoryData<string, CockpitCaptureProfileRefreshState> RejectedDownloads =>
        new()
        {
            { "not an INI profile", CockpitCaptureProfileRefreshState.Invalid },
            { Profile(NextVersion + 1, "0000000000000000"), CockpitCaptureProfileRefreshState.Invalid },
            { Profile(NextVersion + 1) + "Validated=1\n", CockpitCaptureProfileRefreshState.Invalid },
            {
                Profile(NextVersion + 1) + "[Updates]\nCachedProfilePath=C:\\unexpected.ini\n",
                CockpitCaptureProfileRefreshState.Invalid
            },
            {
                Profile(NextVersion + 1).Replace("Validated=1", "Validated=0"),
                CockpitCaptureProfileRefreshState.Invalid
            },
            {
                Profile(NextVersion + 1).Replace("ProtocolVersion=1", "ProtocolVersion=2"),
                CockpitCaptureProfileRefreshState.Incompatible
            },
            {
                Profile(NextVersion + 1).Replace("RigidLayoutVersion=1", "RigidLayoutVersion=2"),
                CockpitCaptureProfileRefreshState.Invalid
            },
            { Profile(NextVersion + 1).Replace("DrawKind=3", "DrawKind=0"), CockpitCaptureProfileRefreshState.Invalid },
            {
                Profile(NextVersion + 1) + ";" + new string('x', CockpitCaptureProfile.MaximumBytes),
                CockpitCaptureProfileRefreshState.Invalid
            },
            {
                Profile(NextVersion + 1).Replace("ProfileVersion=" + (NextVersion + 1), "ProfileVersion=0"),
                CockpitCaptureProfileRefreshState.Invalid
            },
            {
                Profile(NextVersion + 1).Replace("DrawKind=3", "DrawKind=unknown"),
                CockpitCaptureProfileRefreshState.Invalid
            },
        };

    [Theory]
    [MemberData(nameof(RejectedDownloads))]
    public async Task InvalidOrUnsupportedDownloadsPreserveTheWorkingCache(
        string download,
        CockpitCaptureProfileRefreshState state
    )
    {
        using HttpClient client = Client(download);
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, Profile(NextVersion));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, state),
            await updater.RefreshAsync(CancellationToken.None)
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
        Assert.False(File.Exists(updater.CachePath + ".previous"));
    }

    [Fact]
    public async Task NewerDeclarativeLayoutsAreForwardedWithoutAClientDecoderUpdate()
    {
        string download =
            Profile(NextVersion)
                .Replace("RigidLayoutVersion=1", "RigidLayoutVersion=12")
                .Replace("DrawKind=3", "DrawKind=4")
            + "\n[Layout]\nSchemaVersion=3\nRequiredBridgeVersion=12\nFuturePacking=float16\nPositionOffset=128\n";
        using HttpClient client = Client(download);
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);

        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.Updated),
            await updater.RefreshAsync()
        );
        string expected =
            Profile(NextVersion)
                .Replace("RigidLayoutVersion=1", "RigidLayoutVersion=12")
                .Replace("DrawKind=3", "DrawKind=4")
            + "\n[Layout]\nfuturepacking=float16\npositionoffset=128\nrequiredbridgeversion=12\nschemaversion=3\n";
        Assert.Equal(expected, await File.ReadAllTextAsync(updater.CachePath));
        Assert.Equal(expected, CockpitCaptureProfile.Parse(expected)!.Serialize());
    }

    [Fact]
    public async Task LayoutOrderAndCasingDoNotChangeIdentityButChangedOffsetsRequireANewRevision()
    {
        string current =
            Profile(NextVersion).Replace("RigidLayoutVersion=1", "RigidLayoutVersion=2")
            + "[Layout]\nRecordStride=512\nPositionOffset=64\nSchemaVersion=1\nRequiredBridgeVersion=2\n";
        string reordered =
            Profile(NextVersion).Replace("RigidLayoutVersion=1", "RigidLayoutVersion=2")
            + "[Layout]\nrequiredbridgeversion=2\nschemaversion=1\npositionoffset=64\nrecordstride=512\n";
        using HttpClient sameClient = Client(reordered);
        CockpitCaptureProfileUpdater updater = CreateUpdater(sameClient);
        Seed(updater, current);
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.Current),
            await updater.RefreshAsync()
        );
        using HttpClient changedClient = Client(reordered.Replace("positionoffset=64", "positionoffset=128"));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.VersionConflict),
            await CreateUpdater(changedClient).RefreshAsync()
        );
        Assert.Equal(current, await File.ReadAllTextAsync(updater.CachePath));
    }

    [Theory]
    [InlineData("PositionOffset=C:\\game.ini")]
    [InlineData("PositionOffset=1;run")]
    [InlineData("PositionOffset=64\npositionoffset=128")]
    [InlineData("[Commands]\nRun=anything")]
    [InlineData("[Layout]\nPositionOffset=1")]
    [InlineData("PositionOffset=")]
    public async Task LayoutsRejectPathsCommandsDuplicateFieldsAndSections(string fields)
    {
        string download =
            Profile(NextVersion + 1).Replace("RigidLayoutVersion=1", "RigidLayoutVersion=2")
            + "[Layout]\n"
            + fields
            + "\n";
        using HttpClient client = Client(download);
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, Profile(NextVersion));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.Invalid),
            await updater.RefreshAsync()
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
    }

    [Fact]
    public async Task NetworkFailureKeepsCachedDataAndDoesNotCreateAnEmptyReplacement()
    {
        using var client = new HttpClient(new ResponseHandler((_, _) => throw new HttpRequestException("Offline")));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(
                CockpitCaptureProfile.Embedded.ProfileVersion,
                CockpitCaptureProfileRefreshState.Unavailable
            ),
            await updater.RefreshAsync(CancellationToken.None)
        );
        Assert.False(File.Exists(updater.CachePath));
        Seed(updater, Profile(NextVersion));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.Unavailable),
            await updater.RefreshAsync(CancellationToken.None)
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
    }

    [Fact]
    public async Task AnEarlierDownloadCannotOverwriteANewerActivationByAnotherInstance()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstClient = new HttpClient(new ResponseHandler((_, _) => delayed.Task));
        using HttpClient newerClient = Client(Profile(NextVersion + 1));
        CockpitCaptureProfileUpdater first = CreateUpdater(firstClient);
        CockpitCaptureProfileUpdater newer = CreateUpdater(newerClient);
        Task<CockpitCaptureProfileRefreshResult> earlier = first.RefreshAsync(CancellationToken.None);
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion + 1, CockpitCaptureProfileRefreshState.Updated),
            await newer.RefreshAsync(CancellationToken.None)
        );
        delayed.SetResult(Response(Profile(NextVersion)));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion + 1, CockpitCaptureProfileRefreshState.Current),
            await earlier
        );
        Assert.Equal(Profile(NextVersion + 1), await File.ReadAllTextAsync(first.CachePath));
    }

    [Fact]
    public async Task CancellationLeavesTheActiveProfileIntact()
    {
        using HttpClient client = Client(Profile(NextVersion + 1));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, Profile(NextVersion));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            updater.RefreshAsync(new CancellationToken(true))
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
    }

    [Fact]
    public async Task CorruptLocalCacheCanBeReplacedByAValidatedProfile()
    {
        using HttpClient client = Client(Profile(NextVersion));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, "corrupt cache");
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.Updated),
            await updater.RefreshAsync(CancellationToken.None)
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
    }

    [Fact]
    public async Task InvalidUtf8DoesNotReplaceTheActiveProfile()
    {
        using var client = new HttpClient(
            new ResponseHandler(
                (_, _) =>
                    Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent([0xff, 0xfe, 0xff]),
                        }
                    )
            )
        );
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, Profile(NextVersion));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion, CockpitCaptureProfileRefreshState.Invalid),
            await updater.RefreshAsync()
        );
        Assert.Equal(Profile(NextVersion), await File.ReadAllTextAsync(updater.CachePath));
    }

    [Fact]
    public async Task ACacheWriteFailureIsReportedWithoutChangingTheExistingDirectory()
    {
        using HttpClient client = Client(Profile(NextVersion));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Directory.CreateDirectory(updater.CachePath);
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(
                CockpitCaptureProfile.Embedded.ProfileVersion,
                CockpitCaptureProfileRefreshState.Unavailable
            ),
            await updater.RefreshAsync()
        );
        Assert.True(Directory.Exists(updater.CachePath));
        Assert.Empty(Directory.GetFileSystemEntries(updater.CachePath));
    }

    [Fact]
    public async Task EmbeddedCurrentRevisionIsMaterializedForAnIndependentlyInstalledBridge()
    {
        string embedded = CockpitCaptureProfile.Embedded.Serialize();
        using HttpClient client = Client(embedded);
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(
                CockpitCaptureProfile.Embedded.ProfileVersion,
                CockpitCaptureProfileRefreshState.Current
            ),
            await updater.RefreshAsync()
        );
        Assert.True(File.Exists(updater.CachePath));
        Assert.Equal(embedded, await File.ReadAllTextAsync(updater.CachePath));
    }

    [Fact]
    public async Task AValidPredecessorPreventsDowngradeWhenTheActiveCacheIsCorrupt()
    {
        using HttpClient client = Client(Profile(NextVersion + 2));
        CockpitCaptureProfileUpdater updater = CreateUpdater(client);
        Seed(updater, "corrupt active catalog");
        await File.WriteAllTextAsync(updater.CachePath + ".previous", Profile(NextVersion + 3));
        Assert.Equal(
            new CockpitCaptureProfileRefreshResult(NextVersion + 3, CockpitCaptureProfileRefreshState.Current),
            await updater.RefreshAsync()
        );
        Assert.Equal(Profile(NextVersion + 3), await File.ReadAllTextAsync(updater.CachePath));
        Assert.Equal(Profile(NextVersion + 3), await File.ReadAllTextAsync(updater.CachePath + ".previous"));
    }

    private CockpitCaptureProfileUpdater CreateUpdater(HttpClient client) =>
        new(
            directory,
            (source, token) => PublishedReferenceUpdateService.DownloadCaptureProfileAsync(source, token, client)
        );

    private static string Profile(int version, string shader = NewShader) =>
        $"[Capture]\nProfileVersion={version}\nProtocolVersion=1\nValidated=1\nVertexShaderHash={shader}\nRigidLayoutVersion=1\nDrawKind=3\n";

    private static void Seed(CockpitCaptureProfileUpdater updater, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(updater.CachePath)!);
        File.WriteAllText(updater.CachePath, content);
    }

    private static HttpResponseMessage Response(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8) };

    private static HttpClient Client(string content) =>
        new(new ResponseHandler((_, _) => Task.FromResult(Response(content))));

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => respond(request, cancellationToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
