using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class RavenColonialClientTests
{
    [Fact]
    public async Task LoadsCommanderWorkspaceFromFourLegacyEndpoints()
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
                "/root/api/cmdr/Test%20Cmdr/fc/all" => Json(
                    "[{\"marketId\":42,\"name\":\"ABC-123\",\"displayName\":\"Supply\",\"cargo\":{\"steel\":75}}]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var client = Create(handler);

        var result = await client.GetCommanderProjectsAsync("Test Cmdr");

        Assert.Single(result.Projects);
        Assert.Equal("build-1", result.Projects[0].BuildId);
        Assert.Equal(["build-2"], result.HiddenProjectIds);
        Assert.Equal("build-1", result.PrimaryProjectId);
        var carrier = Assert.Single(result.FleetCarriers);
        Assert.Equal(42, carrier.MarketId);
        Assert.Equal(75, carrier.Cargo["steel"]);
        Assert.Equal(4, requested.Count);
    }

    [Fact]
    public async Task ResolvesCommanderForRavenApiKey()
    {
        var client = Create(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/root/api/cmdr/", request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            return Json("{\"displayName\":\"Test Cmdr\"}");
        }));

        var commander = await client.GetCommanderByApiKeyAsync("secret-key");

        Assert.Equal("Test Cmdr", commander);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task TreatsRejectedRavenApiKeyAsInvalid(
        HttpStatusCode statusCode)
    {
        var client = Create(new StubHandler(_ =>
            new HttpResponseMessage(statusCode)));

        Assert.Null(await client.GetCommanderByApiKeyAsync("invalid-key"));
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
    public async Task LoadsAndImportsFullSystemRecords()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        var handler = new StubHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Json(
                """
                {
                  "v":2,
                  "id64":123,
                  "name":"Test System",
                  "architect":"Architect",
                  "open":true,
                  "rev":4,
                  "sites":[],
                  "bodies":[{"name":"Test System A 1","num":1,"distLS":42,"parents":[0],"type":"hmc","features":["landable"],"future":true}],
                  "futureSystem":"retain"
                }
                """);
        });
        var client = Create(handler);

        var loaded = await client.GetSystemAsync("123");
        var imported = await client.ImportSystemBodiesAsync("123");

        Assert.Equal(123, loaded.SystemAddress);
        Assert.Equal("Architect", loaded.Architect);
        Assert.True(loaded.IsOpen);
        var body = Assert.Single(imported.Bodies!);
        Assert.Equal(1, body.Number);
        Assert.Contains("landable", body.Features);
        Assert.True(body.ExtensionData["future"].GetBoolean());
        Assert.True(loaded.ExtensionData.ContainsKey("futureSystem"));
        Assert.Equal(
            [
                (HttpMethod.Get, "/root/api/v2/system/123"),
                (HttpMethod.Post, "/root/api/v2/system/123/import/bodies"),
            ],
            requests);
    }

    [Fact]
    public async Task UpdatesSystemSitesWithApiKeyAndLegacyPayloadShape()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(
                "/root/api/v2/system/Test%20System/sites",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                """{"id64":123,"name":"Test System","sites":[],"bodies":[]}""");
        });
        var client = Create(handler);

        var result = await client.UpdateSystemSitesAsync(
            "Test System",
            new ColonizationSystemSiteUpdate
            {
                UpdatedSites =
                [
                    new ColonizationSystemSite
                    {
                        Id = "site-1",
                        Name = "Port",
                        BodyNumber = 2,
                        BuildType = "no_truss",
                        Status = ColonizationSystemSiteStatus.Complete,
                    },
                ],
                DeletedSiteIds = ["site-2"],
            },
            "secret-key");

        Assert.Equal(123, result.SystemAddress);
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal("site-1", root.GetProperty("update")[0]
            .GetProperty("id").GetString());
        Assert.Equal("complete", root.GetProperty("update")[0]
            .GetProperty("status").GetString());
        Assert.Equal("site-2", root.GetProperty("delete")[0].GetString());
        Assert.False(root.TryGetProperty("architect", out _));
        Assert.False(root.TryGetProperty("open", out _));
    }

    [Fact]
    public async Task ReportsStatusAndBoundedServiceDetail()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(new string('x', 10_000)),
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
    public async Task RejectsOversizedSuccessfulResponse()
    {
        var client = Create(new StubHandler(_ => Json(
            new string('x', 8 * 1024 * 1024 + 1))));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetProjectAsync("build-1"));

        Assert.Contains("8,388,608", exception.Message);
    }

    [Fact]
    public async Task MissingProjectReturnsNull()
    {
        var client = Create(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await client.GetProjectAsync("missing"));
    }

    [Fact]
    public async Task LoadsProjectByLegacyConstructionSiteEndpoint()
    {
        var client = Create(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/root/api/system/123/456",
                request.RequestUri!.AbsolutePath);
            return Json(
                "{\"buildId\":\"build-1\",\"buildName\":\"Port\",\"marketId\":456}");
        }));

        var project = await client.GetProjectAsync(123, 456);

        Assert.Equal("build-1", project?.BuildId);
    }

    [Fact]
    public async Task RestoresLegacyProjectMutationContracts()
    {
        var requests = new List<(
            HttpMethod Method,
            string Path,
            string? Body)>();
        var handler = new StubHandler(async request =>
        {
            requests.Add((
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync()));
            return request.RequestUri.AbsolutePath.EndsWith(
                "/build-1",
                StringComparison.Ordinal)
                ? Json(
                    "{\"buildId\":\"build-1\",\"buildName\":\"Port\",\"sumNeed\":75}")
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = Create(handler);
        var depot = new ColonizationConstructionDepotSnapshot(
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            456,
            0.25,
            IsComplete: false,
            IsFailed: false,
            [new ColonizationResourceRequirement(
                "steel",
                "Steel",
                100,
                25,
                5_000)]);

        var updated = await client.UpdateProjectAsync(
            new ColonizationProjectUpdate
            {
                BuildId = "build-1",
                MaximumRequired = 100,
                Commodities = new Dictionary<string, int>
                {
                    ["steel"] = 75,
                },
                ConstructionDepot =
                    ColonizationConstructionDepotPayload.FromSnapshot(depot),
            });
        await client.ContributeToProjectAsync(
            "build-1",
            "Test Cmdr",
            new Dictionary<string, int> { ["steel"] = 25 });
        await client.MarkProjectCompleteAsync("build-1");
        await client.SetPrimaryProjectAsync("Test Cmdr", "build-1");
        await client.SetPrimaryProjectAsync("Test Cmdr", null);

        Assert.Equal(75, updated.RemainingRequired);
        Assert.Equal(
            [
                (HttpMethod.Post, "/root/api/project/build-1"),
                (HttpMethod.Post,
                    "/root/api/project/build-1/contribute/Test%20Cmdr"),
                (HttpMethod.Post, "/root/api/project/build-1/complete"),
                (HttpMethod.Put,
                    "/root/api/cmdr/Test%20Cmdr/primary/build-1"),
                (HttpMethod.Delete,
                    "/root/api/cmdr/Test%20Cmdr/primary/"),
            ],
            requests.Select(request => (request.Method, request.Path)));
        using var updateJson = JsonDocument.Parse(requests[0].Body!);
        var updateRoot = updateJson.RootElement;
        Assert.Equal("build-1", updateRoot.GetProperty("buildId").GetString());
        Assert.Equal(75, updateRoot.GetProperty("commodities")
            .GetProperty("steel").GetInt32());
        Assert.False(updateRoot.TryGetProperty("buildType", out _));
        using var contributionJson = JsonDocument.Parse(requests[1].Body!);
        Assert.Equal(25, contributionJson.RootElement
            .GetProperty("steel").GetInt32());
    }

    [Fact]
    public async Task LoadsFleetCarrierByLegacyMarketEndpoint()
    {
        var client = Create(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/root/api/fc/3700123456",
                request.RequestUri!.AbsolutePath);
            return Json(
                "{\"marketId\":3700123456,\"name\":\"ABC-123\",\"cargo\":{\"steel\":75}}");
        }));

        var carrier = await client.GetFleetCarrierAsync(3700123456);

        Assert.Equal("ABC-123", carrier?.Name);
        Assert.Equal(75, carrier?.Cargo["steel"]);
    }

    [Fact]
    public async Task PublishesFleetCarrierWithoutReplacingExistingCargo()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(
                "/root/api/fc/3700123456",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                "{\"marketId\":3700123456,\"name\":\"ABC-123\","
                + "\"displayName\":\"Supply carrier\","
                + "\"cargo\":{\"steel\":75}}");
        });
        var client = Create(handler);

        var result = await client.PublishFleetCarrierAsync(
            new ColonizationFleetCarrierRegistration
            {
                MarketId = 3700123456,
                Name = "ABC-123",
                DisplayName = "Supply carrier",
            },
            "secret-key");

        Assert.Equal(75, result.Cargo["steel"]);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(3700123456, json.RootElement
            .GetProperty("marketId").GetInt64());
        Assert.Equal("ABC-123", json.RootElement
            .GetProperty("name").GetString());
        Assert.Equal("Supply carrier", json.RootElement
            .GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement
            .GetProperty("cargo").ValueKind);
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("PATCH", true)]
    public async Task WritesFleetCarrierCargoWithApiKey(
        string method,
        bool adjust)
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(method, request.Method.Method);
            Assert.Equal(
                "/root/api/fc/3700123456/cargo",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            body = await request.Content!.ReadAsStringAsync();
            return Json("{\"steel\":80}");
        });
        var client = Create(handler);

        var result = adjust
            ? await client.AdjustFleetCarrierCargoAsync(
                3700123456,
                new Dictionary<string, int> { ["steel"] = -5 },
                "secret-key")
            : await client.ReplaceFleetCarrierCargoAsync(
                3700123456,
                new Dictionary<string, int> { ["steel"] = 75 },
                "secret-key");

        Assert.Equal(80, result["STEEL"]);
        Assert.Equal(
            adjust ? -5 : 75,
            JsonDocument.Parse(body!).RootElement
                .GetProperty("steel")
                .GetInt32());
    }

    [Fact]
    public async Task RejectsNegativeReplacementCargoBeforeSending()
    {
        var sent = false;
        var client = Create(new StubHandler(_ =>
        {
            sent = true;
            return Json("{}");
        }));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.ReplaceFleetCarrierCargoAsync(
                42,
                new Dictionary<string, int> { ["steel"] = -1 },
                "secret-key"));
        Assert.False(sent);
    }

    [Fact]
    public async Task PublishesCurrentShipWithLegacyPayloadAndApiKey()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/root/api/cmdr/currentShip",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = Create(handler);

        await client.PublishCurrentShipAsync(
            new ColonizationCurrentShip
            {
                CommanderName = "Test Cmdr",
                Name = "Raven One",
                Type = "python",
                MaximumCargo = 192,
                Cargo = new Dictionary<string, int> { ["steel"] = 27 },
            },
            "secret-key");

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal("Test Cmdr", root.GetProperty("cmdr").GetString());
        Assert.Equal("Raven One", root.GetProperty("name").GetString());
        Assert.Equal("python", root.GetProperty("type").GetString());
        Assert.Equal(192, root.GetProperty("maxCargo").GetInt32());
        Assert.Equal(27, root.GetProperty("cargo")
            .GetProperty("steel").GetInt32());
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
