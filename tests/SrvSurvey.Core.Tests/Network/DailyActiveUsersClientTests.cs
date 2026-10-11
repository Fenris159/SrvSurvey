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

    /// <summary>Leaves time to fetch statistics when either check-in headers or its response body stalls.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PingTimeoutStillRefreshesStatistics(bool stallBody)
    {
        using var fixture = new UsageFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (stallBody)
        {
            fixture.Handler.PingContentFactory = () => new StreamContent(new StalledReadStream(entered));
        }
        else
        {
            fixture.Handler.BeforePing = token =>
            {
                entered.SetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
        }
        using var cancellation = new CancellationTokenSource();
        using DailyActiveUsersClient client = fixture.Create();
        Task<long?> pending = client.RefreshAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(pending.IsCompleted);
            Assert.Equal(
                [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3)],
                fixture.Clock.Timers.Select(timer => timer.DueTime)
            );
            fixture.Clock.Timers[1].Fire();
            Assert.Equal(42, await pending.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(42, await client.RefreshAsync());
            Assert.Equal(2, fixture.Handler.Requests.Count);
            Assert.Single(fixture.Handler.Reports);
            Assert.Contains("check-in skipped", Assert.Single(fixture.Messages));
        }
        finally
        {
            await cancellation.CancelAsync();
            await pending;
        }
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
        /// <summary>Isolates identity and daily checkpoints from real application data.</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "srv-usage-" + Guid.NewGuid().ToString("N"));

        /// <summary>Records requests and controls the simulated Worker responses.</summary>
        public UsageHandler Handler { get; } = new();

        /// <summary>Controls UTC rollover without waiting for the next reporting day.</summary>
        public UsageClock Clock { get; } = new();

        /// <summary>Collects quiet failure reports for assertions.</summary>
        public List<string> Messages { get; } = [];

        /// <summary>Shares the deterministic transport across clients representing one installation.</summary>
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
        /// <summary>Records independent deadlines so tests can expire them without waiting on the runner's clock.</summary>
        public List<UsageTimer> Timers { get; } = [];

        /// <summary>Sets the simulated UTC time used to select the daily reporting checkpoint.</summary>
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 11, 23, 59, 0, TimeSpan.Zero);

        /// <summary>Returns the fixture's current UTC date for daily rollover tests.</summary>
        public override DateTimeOffset GetUtcNow() => UtcNow;

        /// <summary>Creates manually fired, one-shot deadlines for deterministic cancellation tests.</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            var timer = new UsageTimer(callback, state, dueTime);
            Timers.Add(timer);
            return timer;
        }
    }

    /// <summary>Expires a captured deadline only when a test explicitly requests it.</summary>
    private sealed class UsageTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        private int disposed;

        /// <summary>Exposes the scheduled deadline for assertions about check-in and refresh budgets.</summary>
        public TimeSpan DueTime { get; private set; } = dueTime;

        /// <summary>Runs the deadline callback while the owning cancellation source is still alive.</summary>
        public void Fire()
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                callback(state);
            }
        }

        /// <summary>Updates an active deadline without introducing a wall-clock timer.</summary>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }
            DueTime = dueTime;
            return true;
        }

        /// <summary>Prevents a disposed deadline from firing.</summary>
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);

        /// <summary>Disposes the deadline without asynchronous work.</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UsageHandler : HttpMessageHandler
    {
        /// <summary>Records endpoint requests in the order the client sends them.</summary>
        public List<Uri> Requests { get; } = [];

        /// <summary>Retains anonymous check-in payloads for identity and privacy assertions.</summary>
        public List<JsonElement> Reports { get; } = [];

        /// <summary>Supplies the public statistics response without contacting the Worker.</summary>
        public string Stats { get; set; } =
            """{"totalUnique":100,"active7d":42,"active30d":75,"byVersion":{"test":100},"byOs":{"Linux":100}}""";

        /// <summary>Supplies check-in response text for bounded-content tests.</summary>
        public string PingReply { get; set; } = "ok";

        /// <summary>Controls whether the simulated check-in succeeds or returns an HTTP error.</summary>
        public HttpStatusCode PingStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>Controls the simulated public-statistics HTTP status.</summary>
        public HttpStatusCode StatsStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>Allows a check-in to stall or fail before response headers arrive.</summary>
        public Func<CancellationToken, Task>? BeforePing { get; set; }

        /// <summary>Supplies custom response streams to exercise cancellation while reading the check-in body.</summary>
        public Func<HttpContent>? PingContentFactory { get; set; }

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
                return new HttpResponseMessage(PingStatus)
                {
                    Content = PingContentFactory?.Invoke() ?? new StringContent(PingReply),
                };
            }
            Assert.Equal(WellKnownUris.ActiveUsersPublic, request.RequestUri);
            return new HttpResponseMessage(StatsStatus) { Content = new StringContent(Stats) };
        }
    }

    /// <summary>Imitates a response body that remains unreadable until its supplied token is canceled.</summary>
    private sealed class StalledReadStream(TaskCompletionSource entered) : MemoryStream
    {
        /// <summary>Waits indefinitely so the request's body-reading deadline must release the stream.</summary>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
