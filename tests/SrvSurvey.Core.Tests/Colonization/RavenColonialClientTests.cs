using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class RavenColonialClientTests
{
    [Fact]
    public async Task LoadsCommanderWorkspaceFromThreeLegacyEndpoints()
    {
        var requested = new List<string>();
        var handler = new StubHandler(request =>
        {
            lock (requested)
            {
                requested.Add(request.RequestUri!.AbsolutePath);
            }

            return request.RequestUri!.AbsolutePath switch
            {
                "/root/api/cmdr/Test%20Cmdr/active" => Json(
                    "[{\"buildId\":\"build-1\",\"buildName\":\"Port\"}]"),
                "/root/api/cmdr/Test%20Cmdr/hiddenIDs" => Json(
                    "[\"build-2\"]"),
                "/root/api/cmdr/Test%20Cmdr/primary" => Json(
                    "\"build-1\""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var client = Create(handler);

        var result = await client.GetCommanderProjectsAsync("Test Cmdr");

        Assert.Single(result.Projects);
        Assert.Equal("build-1", result.Projects[0].BuildId);
        Assert.Equal(["build-2"], result.HiddenProjectIds);
        Assert.Equal("build-1", result.PrimaryProjectId);
        Assert.Equal(3, requested.Count);
    }

    [Fact]
    public async Task SavesDistinctHiddenIdsWithExpectedJsonShape()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/root/api/cmdr/Test%20Cmdr/hiddenIDs",
                request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadAsStringAsync();
            return Json("[\"build-1\"]");
        });
        var client = Create(handler);

        var result = await client.SaveHiddenProjectIdsAsync(
            "Test Cmdr",
            ["build-1", "BUILD-1", ""]);

        Assert.Equal(["build-1"], result);
        Assert.Equal("[\"build-1\"]", body);
    }

    [Fact]
    public async Task CreatesProjectWithLegacyDepotPropertyNames()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/root/api/project/", request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                "{\"buildId\":\"created\",\"buildType\":\"no_truss\",\"buildName\":\"Port\"}");
        });
        var client = Create(handler);
        var depot = new ColonizationConstructionDepotSnapshot(
            DateTimeOffset.UtcNow,
            42,
            0.25,
            IsComplete: false,
            IsFailed: false,
            [new ColonizationResourceRequirement(
                "steel",
                "Steel",
                100,
                25,
                5_057)]);

        var result = await client.CreateProjectAsync(
            new ColonizationProjectCreate
            {
                BuildType = "no_truss",
                BuildName = "Port",
                MarketId = 42,
                SystemAddress = 99,
                SystemName = "Test",
                MaximumRequired = 100,
                Commodities = new Dictionary<string, int>
                {
                    ["steel"] = 75,
                },
                ConstructionDepot =
                    ColonizationConstructionDepotPayload.FromSnapshot(depot),
            });

        Assert.Equal("created", result?.BuildId);
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal("no_truss", root.GetProperty("buildType").GetString());
        var depotJson = root.GetProperty(
            "colonisationConstructionDepot");
        Assert.Equal(42, depotJson.GetProperty("MarketID").GetInt64());
        var resource = depotJson.GetProperty("ResourcesRequired")[0];
        Assert.Equal("$steel_name;", resource.GetProperty("Name").GetString());
        Assert.Equal(25, resource.GetProperty("ProvidedAmount").GetInt32());
    }

    [Fact]
    public async Task ReadsPlannedSitesAndArchitect()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(
                "/sites",
                StringComparison.Ordinal)
                ? Json(
                    "[{\"id\":\"site-1\",\"name\":\"Port\",\"bodyNum\":3,\"buildType\":\"no_truss\",\"status\":\"plan\"}]")
                : Json("\"Architect\""));
        var client = Create(handler);

        var sites = await client.GetSystemSitesAsync("Test System");
        var architect = await client.GetSystemArchitectAsync("Test System");

        var site = Assert.Single(sites);
        Assert.Equal(ColonizationSystemSiteStatus.Plan, site.Status);
        Assert.Equal("Architect", architect);
    }

    [Fact]
    public async Task ReportsStatusAndBoundedServiceDetail()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(new string('x', 600)),
        });
        var client = Create(handler);

        var exception = await Assert.ThrowsAsync<RavenColonialServiceException>(
            () => client.GetProjectAsync("build-1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("load a colonisation project", exception.Operation);
        Assert.Contains("...", exception.Message);
        Assert.True(exception.Message.Length < 700);
    }

    [Fact]
    public async Task MissingProjectReturnsNull()
    {
        var client = Create(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await client.GetProjectAsync("missing"));
    }

    private static RavenColonialClient Create(HttpMessageHandler handler)
    {
        return new RavenColonialClient(
            new HttpClient(handler),
            new Uri("https://example.test/root/"));
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>
            send;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this(request => Task.FromResult(send(request)))
        {
        }

        public StubHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request);
        }
    }
}
