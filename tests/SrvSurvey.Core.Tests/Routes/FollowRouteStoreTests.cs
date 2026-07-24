using System.Text.Json.Nodes;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class FollowRouteStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-store-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingRouteReturnsLegacyDefaultsWithoutCreatingFile()
    {
        var store = new FollowRouteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Exists);
        Assert.True(result.Route!.IsActive);
        Assert.True(result.Route.AutoCopy);
        Assert.Equal(-1, result.Route.LastReachedIndex);
        Assert.Empty(result.Route.Hops);
        Assert.Equal(
            Path.Combine(temporaryDirectory, "routes", "F123.json"),
            result.Path);
        Assert.False(File.Exists(result.Path));
    }

    [Fact]
    public async Task LoadReadsLegacyRouteShapeAndComputedState()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "active": true,
              "autoCopy": true,
              "last": 0,
              "hops": [
                { "name": "Sol", "id64": 1, "x": 0, "y": 0, "z": 0 },
                {
                  "name": "Skaudai CH-B d14-34",
                  "id64": 2,
                  "x": 10.5,
                  "y": -2,
                  "z": 7,
                  "notes": "Map planet 2",
                  "refuel": true,
                  "neutron": true
                }
              ]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123");

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Exists);
        Assert.True(result.Route!.IsStarted);
        Assert.False(result.Route.IsComplete);
        Assert.True(result.Route.UseNextHop);
        var next = result.Route.NextHop;
        Assert.NotNull(next);
        Assert.Equal("Skaudai CH-B d14-34", next.Name);
        Assert.Equal(2, next.SystemAddress);
        Assert.Equal(new GalacticCoordinate(10.5, -2, 7), next.Position);
        Assert.Equal("Map planet 2", next.Notes);
        Assert.True(next.Refuel);
        Assert.True(next.Neutron);
    }

    [Fact]
    public async Task SaveUsesLegacyFieldsAndPreservesMatchingUnknownData()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "active": true,
              "autoCopy": false,
              "last": -1,
              "futureRoot": { "enabled": true },
              "hops": [
                {
                  "name": "Sol",
                  "id64": 1,
                  "x": 0,
                  "y": 0,
                  "z": 0,
                  "refuel": true,
                  "futureHop": 7
                }
              ]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);
        var loaded = await store.LoadAsync("F123");
        var route = loaded.Route! with
        {
            IsActive = false,
            AutoCopy = true,
            LastReachedIndex = 0,
            Hops =
            [
                new FollowRouteHop(
                    "Sol",
                    1,
                    new GalacticCoordinate(1, 2, 3),
                    null,
                    false,
                    true),
            ],
        };

        await store.SaveAsync(route);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureRoot"]!["enabled"]!.GetValue<bool>());
        Assert.False(root["active"]!.GetValue<bool>());
        Assert.True(root["autoCopy"]!.GetValue<bool>());
        Assert.Equal(0, root["last"]!.GetValue<int>());
        var hop = root["hops"]![0]!.AsObject();
        Assert.Equal(7, hop["futureHop"]!.GetValue<int>());
        Assert.Equal(1D, hop["x"]!.GetValue<double>());
        Assert.True(hop["neutron"]!.GetValue<bool>());
        Assert.False(hop.ContainsKey("refuel"));
        Assert.False(hop.ContainsKey("notes"));
    }

    [Fact]
    public async Task ReplacingHopsDoesNotTransferUnknownDataToAnotherSystem()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(
            path,
            """
            {
              "hops": [
                { "name": "Sol", "id64": 1, "futureHop": "Sol only" }
              ]
            }
            """);
        var store = new FollowRouteStore(temporaryDirectory);
        var loaded = await store.LoadAsync("F123");
        var route = loaded.Route! with
        {
            Hops =
            [
                new FollowRouteHop(
                    "Achenar",
                    2,
                    null,
                    null,
                    false,
                    false),
            ],
        };

        await store.SaveAsync(route);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var hop = root["hops"]![0]!.AsObject();
        Assert.Equal("Achenar", hop["name"]!.GetValue<string>());
        Assert.False(hop.ContainsKey("futureHop"));
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedRoute()
    {
        var path = CreateRoutePath();
        const string malformed = "{\"hops\":";
        await File.WriteAllTextAsync(path, malformed);
        var store = new FollowRouteStore(temporaryDirectory);
        var route = new FollowRouteDocument(
            "F123",
            path,
            true,
            true,
            -1,
            []);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(route));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task InvalidRouteAndUnsafeFrontierIdAreReported()
    {
        var path = CreateRoutePath();
        await File.WriteAllTextAsync(path, "{\"hops\":[{\"id64\":1}]}");
        var store = new FollowRouteStore(temporaryDirectory);

        var result = await store.LoadAsync("F123");

        Assert.False(result.IsSuccess);
        Assert.Contains("name", result.Error, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("../outside"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.LoadAsync("unsafe:name"));
    }

    private string CreateRoutePath()
    {
        var directory = Path.Combine(temporaryDirectory, "routes");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "F123.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
