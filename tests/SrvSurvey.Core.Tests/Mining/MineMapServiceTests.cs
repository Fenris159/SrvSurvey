using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MineMapServiceTests
{
    [Fact]
    public async Task MiningCommandCreatesPersistentSurveyAtKnownRadiusAndBearing()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(0, 0));
        var service = new MineMapService(directory.Path);

        var result = await service.ExecuteAsync(
            ".mining 90 4 high/low",
            context);

        Assert.True(result.Succeeded);
        var survey = Assert.Single(service.Surveys);
        Assert.Equal("Mining Location Signal 4", survey.Name);
        Assert.Equal(MineMapRating.High, survey.MineralAmount);
        Assert.Equal(MineMapRating.Low, survey.Density);
        Assert.InRange(survey.Center.Latitude, -0.000001, 0.000001);
        Assert.InRange(
            SurfaceNavigation.GetDistance(
                context.PlayerLocation!.Value,
                survey.Center,
                context.PlanetRadiusMeters),
            2469.99,
            2470.01);
        Assert.Contains("center saved", result.Message, StringComparison.OrdinalIgnoreCase);

        var reloaded = new MineMapService(directory.Path);
        var persisted = Assert.Single(reloaded.Surveys);
        Assert.Equal(survey.Id, persisted.Id);
        Assert.Equal(context.SystemPosition, persisted.SystemPosition);
        Assert.Equal("Rocky Ice body", persisted.BodyType);
    }

    [Fact]
    public async Task MineCommandsAddRelativeAndHereMarkersAndDeleteNearestHere()
    {
        using var directory = new TemporaryDirectory();
        var border = Context(new SurfaceCoordinate(1, 2));
        var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(
            ".mining 180 7 low/high",
            border)).Succeeded);

        var relative = await service.ExecuteAsync(
            ".mine 15 ruby 1.24",
            border with { PlayerLocation = service.ActiveSurvey!.Center });
        Assert.True(relative.Succeeded);
        var first = Assert.Single(service.ActiveSurvey!.Markers);
        Assert.Equal("ruby", first.Material);
        Assert.InRange(
            SurfaceNavigation.GetDistance(
                service.ActiveSurvey.Center,
                first.Location,
                border.PlanetRadiusMeters),
            1239.99,
            1240.01);
        Assert.InRange(
            SurfaceNavigation.GetBearing(
                service.ActiveSurvey.Center,
                first.Location),
            14.99,
            15.01);

        var here = first.Location;
        Assert.True((await service.ExecuteAsync(
            ".mine low temperature diamonds here",
            border with { PlayerLocation = here })).Succeeded);
        Assert.Equal(2, service.ActiveSurvey.Markers.Count);
        Assert.Contains(service.ActiveSurvey.Markers,
            marker => marker.Material == "low temperature diamonds");

        var deleted = await service.ExecuteAsync(
            ".mine delete here",
            border with { PlayerLocation = here });
        Assert.True(deleted.Succeeded);
        Assert.Single(service.ActiveSurvey.Markers);
        Assert.Contains("removed", deleted.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JournalCommandsReturnFeedbackAndIgnoreBootstrapMutations()
    {
        using var directory = new TemporaryDirectory();
        var service = new MineMapService(directory.Path);
        Assert.True(JournalEventEnvelope.TryParse(
            """{"event":"SendText","Message":".mining 120 4 high/low"}""",
            out var command,
            out _));

        var ignored = await service.ApplyJournalEventsAsync(
            [command!],
            Context(new SurfaceCoordinate(10, 20)),
            allowMutations: false);
        Assert.Empty(ignored);
        Assert.Empty(service.Surveys);

        var applied = await service.ApplyJournalEventsAsync(
            [command!],
            Context(new SurfaceCoordinate(10, 20)),
            allowMutations: true);
        Assert.True(Assert.Single(applied).Succeeded);
        Assert.Single(service.Surveys);
    }

    [Theory]
    [InlineData(".mining 360 4 high/low")]
    [InlineData(".mining 120 0 high/low")]
    [InlineData(".mining 120 4 medium/low")]
    [InlineData(".mining 120 4 1/0")]
    [InlineData(".mine 15 ruby -1")]
    [InlineData(".mine ruby somewhere")]
    public async Task InvalidCommandsReturnActionableFeedback(string command)
    {
        using var directory = new TemporaryDirectory();
        var service = new MineMapService(directory.Path);

        var result = await service.ExecuteAsync(
            command,
            Context(new SurfaceCoordinate(10, 20)));

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    private static MineMapCommandContext Context(SurfaceCoordinate location) => new(
        "F123",
        "Fenris",
        "Wille",
        123456789,
        new GalacticCoordinate(1, 2, 3),
        5,
        "Wille 2 C",
        "Rocky Ice body",
        129.5,
        855_573.1875,
        location);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SrvSurvey-MineMap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
