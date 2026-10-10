using System.Net;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    /// <summary>Verifies that the preview uses exactly the public GET endpoints and sends no write credentials.</summary>
    [Fact]
    public async Task LoadsReadOnlyPreviewWithCargoAndHourlyStatistics()
    {
        var paths = new List<string>();
        RavenColonialClient client = Create(
            new StubHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.False(request.Headers.Contains("rcc-key"));
                lock (paths)
                {
                    paths.Add(request.RequestUri!.AbsolutePath);
                }
                return request.RequestUri.AbsolutePath switch
                {
                    "/root/api/project/build%20id" => Json("""{"buildId":"build id","commodities":{"steel":5}}"""),
                    "/root/api/project/build%20id/fc/" => Json("""{"1":{"steel":3}}"""),
                    "/root/api/project/build%20id/stats" => Json(
                        """{"totalCargo":20,"totalDeliveries":2,"cmdrs":{"Test":20},"stats":[{"time":"2026-10-09T01:00:00Z","cmdrs":{"Test":20}}]}"""
                    ),
                    _ => throw new InvalidOperationException("Unexpected request"),
                };
            })
        );
        ColonizationProjectPreviewData data = Assert.IsType<ColonizationProjectPreviewData>(
            await client.ReadProjectPreviewAsync(" build id ", CancellationToken.None)
        );
        Assert.Equal(3, paths.Count);
        Assert.Equal(3, data.CarrierCargo!["1"]["steel"]);
        Assert.Equal(20, data.Statistics!.TotalCargo);
        Assert.Equal(20, Assert.Single(data.Statistics.Stats).Total);
    }

    /// <summary>Stops reading optional sections when Raven confirms the build is missing.</summary>
    [Fact]
    public async Task DoesNotFetchSectionsForMissingBuild()
    {
        int requests = 0;
        RavenColonialClient client = Create(
            new StubHandler(_ =>
            {
                requests++;
                return new(HttpStatusCode.NotFound);
            })
        );
        Assert.Null(await client.ReadProjectPreviewAsync("missing", CancellationToken.None));
        Assert.Equal(1, requests);
    }

    /// <summary>Displays unavailable optional sections without losing valid project data, even for malformed responses.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "error")]
    [InlineData(HttpStatusCode.OK, "invalid-json")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task KeepsProjectWhenPreviewSectionsFail(HttpStatusCode status, string body)
    {
        RavenColonialClient client = Create(
            new StubHandler(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/build", StringComparison.Ordinal)
                    ? Json("""{"buildId":"build"}""")
                    : new(status) { Content = new StringContent(body) }
            )
        );
        ColonizationProjectPreviewData data = Assert.IsType<ColonizationProjectPreviewData>(
            await client.ReadProjectPreviewAsync("build", CancellationToken.None)
        );
        Assert.Equal("build", data.Project.BuildId);
        Assert.Null(data.CarrierCargo);
        Assert.Null(data.Statistics);
    }

    /// <summary>Preserves cancellation rather than treating a closed window as an unavailable service.</summary>
    [Fact]
    public async Task CancelsReadOnlyPreview()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        RavenColonialClient client = Create(new StubHandler(_ => Json("{}")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ReadProjectPreviewAsync("build", cancellation.Token)
        );
    }
}
