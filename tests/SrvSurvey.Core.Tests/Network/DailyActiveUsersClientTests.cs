using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Network;
using Xunit;

namespace SrvSurvey.Core.Tests.Network;

public sealed class DailyActiveUsersClientTests
{
    /// <summary>Retains a random installation ID and suppresses repeat requests across restarts until the next UTC day.</summary>
    [Fact]
    public async Task DailyCheckInUsesOneAnonymousIdentityAcrossInstancesAndDays()
    {
        using var fixture = new UsageFixture();
        using DailyActiveUsersClient first = fixture.Create();
        Assert.Equal(42, await first.RefreshAsync());
        using DailyActiveUsersClient second = fixture.Create();
        Assert.Equal(42, await second.RefreshAsync());
        Assert.Equal(2, fixture.Handler.Requests.Count);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        fixture.Handler.Stats = """{"active7d":45}""";
        Assert.Equal(45, await second.RefreshAsync());

        JsonElement[] reports = fixture.Handler.Reports.ToArray();
        Assert.Equal(2, reports.Length);
        string id = reports[0].GetProperty("id").GetString()!;
        Assert.True(Guid.TryParseExact(id, "N", out _));
        Assert.Equal(id, reports[1].GetProperty("id").GetString());
        Assert.All(
            reports,
            report =>
            {
                Assert.Equal(["id", "os", "version"], report.EnumerateObject().Select(p => p.Name).Order().ToArray());
                Assert.Equal("2.1.3.0-rc.62", report.GetProperty("version").GetString());
                Assert.Equal("Linux", report.GetProperty("os").GetString());
            }
        );
        Assert.All(fixture.Handler.Requests, request => Assert.Equal(string.Empty, request.Query));
    }

    /// <summary>Serializes concurrent instances with the on-disk lease, preventing duplicate check-ins and IDs.</summary>
    [Fact]
    public async Task ConcurrentInstancesShareOneDailyAttempt()
    {
        using var fixture = new UsageFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.BeforePing = async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
        };
        using DailyActiveUsersClient first = fixture.Create();
        using DailyActiveUsersClient second = fixture.Create();
        Task<long?> pendingFirst = first.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<long?> pendingSecond = second.RefreshAsync();
        Assert.False(pendingSecond.IsCompleted);
        release.SetResult();
        Assert.Equal([42L, 42L], await Task.WhenAll(pendingFirst, pendingSecond));
        Assert.Single(fixture.Handler.Reports);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    /// <summary>Still refreshes public counts after an unsuccessful or uncertain ping, without retrying it that day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPingDoesNotHideStatsOrRetry(bool transportFailure)
    {
        using var fixture = new UsageFixture();
        fixture.Handler.PingStatus = HttpStatusCode.ServiceUnavailable;
        if (transportFailure)
        {
            fixture.Handler.BeforePing = _ => throw new HttpRequestException("Connection lost");
        }
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Equal(42, await client.RefreshAsync());
        Assert.Equal(42, await client.RefreshAsync());
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Single(fixture.Messages);
    }

    /// <summary>Bounds successful ping replies while still allowing a public-count refresh.</summary>
    [Fact]
    public async Task OversizedPingReplyIsQuietlyDiscarded()
    {
        using var fixture = new UsageFixture();
        fixture.Handler.PingReply = new string('x', 1025);
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Equal(42, await client.RefreshAsync());
        Assert.Single(fixture.Messages);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    /// <summary>Keeps the last valid count when the endpoint is unavailable, malformed, incomplete, or oversized.</summary>
    [Theory]
    [InlineData("{", 200)]
    [InlineData("null", 200)]
    [InlineData("{}", 200)]
    [InlineData("{\"active7d\":-1}", 200)]
    [InlineData("{\"active7d\":\"invalid\"}", 200)]
    [InlineData("{\"active7d\":0.5}", 200)]
    [InlineData("{\"active7d\":0}", 503)]
    [InlineData("oversized", 200)]
    public async Task FailedStatsKeepCachedCount(string stats, int status)
    {
        using var fixture = new UsageFixture();
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Equal(42, await client.RefreshAsync());
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        fixture.Handler.Stats = stats == "oversized" ? new string('x', 16 * 1024 + 1) : stats;
        fixture.Handler.StatsStatus = (HttpStatusCode)status;
        Assert.Equal(42, await client.RefreshAsync());
        using DailyActiveUsersClient restarted = fixture.Create();
        Assert.Equal(42, await restarted.RefreshAsync());
        Assert.Equal(4, fixture.Handler.Requests.Count);
        Assert.Single(fixture.Messages);
    }

    /// <summary>Accepts a genuine zero rather than confusing an empty population with unavailable statistics.</summary>
    [Fact]
    public async Task ZeroIsAValidCount()
    {
        using var fixture = new UsageFixture();
        fixture.Handler.Stats = """{"active7d":0}""";
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Equal(0, await client.RefreshAsync());
    }

    /// <summary>Preserves damaged state instead of creating an additional installation identity.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Id\":\"not-a-guid\"}")]
    [InlineData("{\"Id\":\"9a64d9e53af0411d926cb8e04a4b2e96\",\"Active7d\":-1}")]
    public async Task CorruptStateIsPreserved(string content)
    {
        using var fixture = new UsageFixture();
        string directory = Path.Combine(fixture.Root, "usage");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "local-user.json");
        await File.WriteAllTextAsync(path, content);
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Null(await client.RefreshAsync());
        Assert.Equal(content, await File.ReadAllTextAsync(path));
        Assert.Empty(fixture.Handler.Requests);
        Assert.Single(fixture.Messages);
    }

    /// <summary>Quietly skips reporting when the machine-local state directory is not writable.</summary>
    [Fact]
    public async Task UnavailableStateDoesNotReportAnUnpersistedIdentity()
    {
        using var fixture = new UsageFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "usage"), "occupied");
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Null(await client.RefreshAsync());
        Assert.Empty(fixture.Handler.Requests);
        Assert.Single(fixture.Messages);
    }

    /// <summary>Cancellation releases the reporting lease, retains cached statistics, and does not retry uncertain writes.</summary>
    [Fact]
    public async Task CancellationPreservesCacheAndReleasesLease()
    {
        using var fixture = new UsageFixture();
        using DailyActiveUsersClient first = fixture.Create();
        Assert.Equal(42, await first.RefreshAsync());
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        using var cancellation = new CancellationTokenSource();
        fixture.Handler.BeforePing = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        Assert.Equal(42, await first.RefreshAsync(cancellation.Token));
        using DailyActiveUsersClient second = fixture.Create();
        Assert.Equal(42, await second.RefreshAsync());
        Assert.Equal(3, fixture.Handler.Requests.Count);
    }

    /// <summary>Can cancel while waiting on another process's lease without making a request.</summary>
    [Fact]
    public async Task CancellationWhileWaitingForLeaseDoesNotReport()
    {
        using var fixture = new UsageFixture();
        string directory = Path.Combine(fixture.Root, "usage");
        Directory.CreateDirectory(directory);
        using var lease = new FileStream(
            Path.Combine(directory, "report.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        using DailyActiveUsersClient client = fixture.Create();
        Assert.Null(await client.RefreshAsync(cancellation.Token));
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>Disposes its own HTTP client without taking ownership of a supplied shared client.</summary>
    [Fact]
    public async Task DisposalLeavesSharedClientUsable()
    {
        using var fixture = new UsageFixture();
        fixture.Create().Dispose();
        using HttpResponseMessage response = await fixture.Network.GetAsync(WellKnownUris.ActiveUsersPublic);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var owned = new DailyActiveUsersClient(fixture.Root, "test", "Linux");
    }

    /// <summary>Provides isolated durable state, a controllable UTC clock, and a deterministic Worker transport.</summary>
    private sealed class UsageFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "srv-usage-" + Guid.NewGuid().ToString("N"));
        public UsageHandler Handler { get; } = new();
        public UsageClock Clock { get; } = new();
        public List<string> Messages { get; } = [];
        public HttpClient Network { get; }

        /// <summary>Creates the machine-local root and shared test HTTP client.</summary>
        public UsageFixture()
        {
            Directory.CreateDirectory(Root);
            Network = new HttpClient(Handler);
        }

        /// <summary>Constructs an independent client sharing this installation's durable state.</summary>
        public DailyActiveUsersClient Create() => new(Root, "2.1.3.0-rc.62", "Linux", Network, Messages.Add, Clock);

        /// <summary>Releases the HTTP client and deletes the isolated state.</summary>
        public void Dispose()
        {
            Network.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class UsageClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 11, 23, 59, 0, TimeSpan.Zero);

        /// <summary>Returns the fixture's current UTC date for daily rollover tests.</summary>
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class UsageHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<JsonElement> Reports { get; } = [];
        public string Stats { get; set; } =
            """{"totalUnique":100,"active7d":42,"active30d":75,"byVersion":{"test":100},"byOs":{"Linux":100}}""";
        public string PingReply { get; set; } = "ok";
        public HttpStatusCode PingStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode StatsStatus { get; set; } = HttpStatusCode.OK;
        public Func<CancellationToken, Task>? BeforePing { get; set; }

        /// <summary>Imitates only the public Worker contract and records exactly which data was sent.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal(WellKnownUris.ActiveUsersPing, request.RequestUri);
                using var report = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Reports.Add(report.RootElement.Clone());
                if (BeforePing is not null)
                {
                    await BeforePing(cancellationToken);
                }
                return new HttpResponseMessage(PingStatus) { Content = new StringContent(PingReply) };
            }
            Assert.Equal(WellKnownUris.ActiveUsersPublic, request.RequestUri);
            return new HttpResponseMessage(StatsStatus) { Content = new StringContent(Stats) };
        }
    }
}
