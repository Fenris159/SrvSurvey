using System.Net;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    /// <summary>Enforces the request deadline while reading a response body after headers arrive.</summary>
    [Fact]
    public async Task TimesOutStalledResponseBody()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingBodyStream(stall: true)),
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var client = new RavenColonialClient(http, new Uri("https://example.test/"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.GetSystemSitesAsync("42").WaitAsync(TimeSpan.FromSeconds(2))
        );
    }

    /// <summary>Routes truncated response-body I/O through the recoverable HTTP failure boundary.</summary>
    [Fact]
    public async Task NormalizesResponseBodyIoFailure()
    {
        RavenColonialClient client = Create(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingBodyStream(stall: false)),
            })
        );
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetSystemSitesAsync("42")
        );
        Assert.IsAssignableFrom<IOException>(error.InnerException);
    }

    /// <summary>Allows an unassigned system architect without weakening required-object validation.</summary>
    [Fact]
    public async Task AcceptsNullArchitectButRejectsNullSystem()
    {
        RavenColonialClient client = Create(new StubHandler(_ => Json("null")));
        Assert.Null(await client.GetSystemArchitectAsync("42"));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetSystemAsync("42"));
    }

    /// <summary>Loads a commander who has no primary project selected.</summary>
    [Fact]
    public async Task AcceptsNullPrimaryProject()
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
                Json(request.RequestUri!.AbsolutePath.EndsWith("/primary", StringComparison.Ordinal) ? "null" : "[]")
            )
        );
        Assert.Null((await client.GetCommanderProjectsAsync("Cmdr")).PrimaryProjectId);
    }

    /// <summary>Preserves statusless legacy repair eligibility and accepts old missing/string market identifiers.</summary>
    [Theory]
    [InlineData("null", "\"\"", null)]
    [InlineData("\"\"", "\"0\"", 0L)]
    [InlineData("null", "\"4300000123\"", 4300000123L)]
    [InlineData("null", "\"invalid\"", null)]
    public async Task NormalizesLegacyRepairFields(string status, string marketId, long? expectedMarketId)
    {
        RavenColonialClient client = Create(
            new StubHandler(_ =>
                Json($"[{{\"id\":\"site\",\"name\":\"Port\",\"status\":{status},\"marketId\":{marketId}}}]")
            )
        );
        ColonizationSystemSite site = Assert.Single(await client.GetSystemSitesAsync("42"));
        Assert.False(site.HasExplicitStatus);
        Assert.Equal(expectedMarketId, site.MarketId);
        Assert.NotNull(ColonizationBuildSiteRepair.CreatePlan([site], "Port", 4300000456));
    }

    /// <summary>Checks the response to the final allowed order correction before reporting a warning.</summary>
    [Fact]
    public async Task VerifiesFinalPrimaryOrderCorrection()
    {
        int reads = 0;
        int updates = 0;
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    reads++;
                    return Json(
                        reads switch
                        {
                            1 => "[{\"id\":\"primary\",\"name\":\"Port\"}]",
                            4 =>
                                "[{\"id\":\"primary\",\"name\":\"Port\"},{\"id\":\"created\",\"buildId\":\"build\",\"name\":\"Build\"}]",
                            _ =>
                                "[{\"id\":\"created\",\"buildId\":\"build\",\"name\":\"Build\"},{\"id\":\"primary\",\"name\":\"Port\"}]",
                        }
                    );
                }
                if (request.RequestUri!.AbsolutePath.EndsWith("/sites", StringComparison.Ordinal))
                {
                    updates++;
                    return Json("{}");
                }
                return Json("{\"buildId\":\"build\",\"buildName\":\"Build\"}");
            })
        );
        ColonizationProjectPublishResult result = await new ColonizationProjectPublisher(client).CreateAsync(
            CreateProjectRequest(),
            "key"
        );
        Assert.Equal(ColonizationPrimarySiteOrderStatus.Restored, result.PrimarySiteOrderStatus);
        Assert.Equal(2, updates);
        Assert.Null(result.Warning);
    }

    /// <summary>Accepts an acknowledged contribution without reading an unused stalled success body.</summary>
    [Fact]
    public async Task AcknowledgedContributionDoesNotWaitForResponseBody()
    {
        RavenColonialClient client = Create(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingBodyStream(stall: true)),
            })
        );
        Exception? error = await Record.ExceptionAsync(() =>
            client
                .ContributeToProjectAsync("build", "Cmdr", new Dictionary<string, int> { ["steel"] = 5 })
                .WaitAsync(TimeSpan.FromSeconds(2))
        );
        Assert.Null(error);
    }

    /// <summary>Honors caller cancellation after receiving headers instead of substituting the request timeout.</summary>
    [Fact]
    public async Task BodyReadHonorsCallerCancellation()
    {
        RavenColonialClient client = Create(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingBodyStream(stall: true)),
            })
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetSystemSitesAsync("42", cancellation.Token)
        );
    }

    /// <summary>Rejects unsupported explicit statuses and nonnumeric market tokens instead of expanding repair eligibility.</summary>
    [Theory]
    [InlineData("\"unknown\"", "1")]
    [InlineData("42", "1")]
    [InlineData("\"complete\"", "true")]
    public async Task RejectsUnsupportedLegacyTokens(string status, string market)
    {
        RavenColonialClient client = Create(
            new StubHandler(_ =>
                Json($"[{{\"id\":\"site\",\"name\":\"Port\",\"status\":{status},\"marketId\":{market}}}]")
            )
        );
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetSystemSitesAsync("42"));
    }

    /// <summary>Round-trips normalized explicit statuses and market identifiers through the publication JSON contract.</summary>
    [Theory]
    [InlineData(ColonizationSystemSiteStatus.Complete, 4300000123L)]
    [InlineData(ColonizationSystemSiteStatus.Build, null)]
    public void WritesNormalizedLegacyFields(ColonizationSystemSiteStatus status, long? market)
    {
        var site = new ColonizationSystemSite
        {
            Id = "site",
            Name = "Port",
            Status = status,
            MarketId = market,
        };
        string json = System.Text.Json.JsonSerializer.Serialize(site);
        ColonizationSystemSite restored = System.Text.Json.JsonSerializer.Deserialize<ColonizationSystemSite>(json)!;
        Assert.True(restored.HasExplicitStatus);
        Assert.Equal(status, restored.Status);
        Assert.Equal(market, restored.MarketId);
    }

    /// <summary>Provides a response body which either waits for cancellation or fails on its first read.</summary>
    private sealed class FailingBodyStream(bool stall) : MemoryStream
    {
        /// <summary>Simulates a stalled or truncated body at the actual asynchronous read boundary.</summary>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (stall)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            throw new IOException("Response ended prematurely.");
        }
    }
}
