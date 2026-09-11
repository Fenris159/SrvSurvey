using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using System.Text.Json;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MineMapServiceTests
{
    [Fact]
    public void InterruptedLegacyMigrationImportsEveryMissingSurvey()
    {
        using var directory = new TemporaryDirectory();
        var legacyDirectory = Path.Combine(directory.Path, "mine-maps");
        Directory.CreateDirectory(legacyDirectory);
        var catalog = new BookmarkCatalog(directory.Path);
        var first = LegacySurvey(Guid.NewGuid(), signal: 4);
        var second = LegacySurvey(Guid.NewGuid(), signal: 5);
        catalog.Save(new GalacticBookmark
        {
            Id = first.Id,
            System = first.SystemName,
            Body = first.BodyName,
            CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
            SurfaceMiningMap = first,
        });
        File.WriteAllText(
            Path.Combine(legacyDirectory, "first.json"),
            JsonSerializer.Serialize(first));
        File.WriteAllText(
            Path.Combine(legacyDirectory, "second.json"),
            JsonSerializer.Serialize(second));

        using var service = new MineMapService(directory.Path, catalog);

        Assert.Equal(2, service.Surveys.Count);
        Assert.Contains(service.Surveys, survey => survey.Id == first.Id);
        Assert.Contains(service.Surveys, survey => survey.Id == second.Id);
        Assert.True(File.Exists(Path.Combine(legacyDirectory, ".bookmarks-migrated")));
    }

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
        var bookmark = Assert.Single(new BookmarkCatalog(directory.Path).Items);
        Assert.Equal("Surface Mining", bookmark.Category);
        Assert.Equal(survey.Id, bookmark.Id);
        Assert.NotNull(bookmark.SurfaceMiningMap);
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
        Assert.Equal("Ruby", first.Material);
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
            marker => marker.Material == "Low Temperature Diamonds");

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
    [InlineData(".mine 15 unobtainium 1.2")]
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

    [Fact]
    public async Task MineCommandCanonicalizesCaseAndRejectsNamesOutsideHotspotList()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(10, 20));
        var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(
            ".MINING 120 4 HIGH/LOW",
            context)).Succeeded);

        var accepted = await service.ExecuteAsync(
            ".MINE 15 pErIcLaSe DuNiTe 1.24",
            context);
        var rejected = await service.ExecuteAsync(
            ".mine 15 unobtainium 1.24",
            context);

        Assert.True(accepted.Succeeded);
        Assert.Equal("Periclase Dunite", Assert.Single(
            service.ActiveSurvey!.Markers).Material);
        Assert.False(rejected.Succeeded);
        Assert.Contains("Hotspot List", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SurfaceHuntReferenceDrivesHotspotAvailabilityPricesAndNames()
    {
        Assert.Equal(37, SurfaceMiningCommodityCatalog.HuntReferences.Count);
        Assert.Equal(37, SurfaceMiningCommodityCatalog.All.Count);
        Assert.Equal(
            SurfaceMiningCommodityCatalog.HuntReferences.Select(row => row.Material)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            SurfaceMiningCommodityCatalog.All.Select(row => row.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            SurfaceMiningCommodityCatalog.All.Count,
            SurfaceMiningCommodityCatalog.All
                .Select(commodity => commodity.ColorHex)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        var helium = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Helium");
        Assert.True(helium.HighMetalContent);
        Assert.True(helium.RockyIce);
        Assert.False(helium.Icy);
        Assert.Equal(591_360, helium.MaximumSellPrice);

        var lowTempDiamonds = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Low Temperature Diamonds");
        Assert.True(lowTempDiamonds.Rocky);
        Assert.True(lowTempDiamonds.RockyIce);
        Assert.True(lowTempDiamonds.Icy);

        var iridium = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Iridium");
        Assert.True(iridium.HighMetalContent);
        Assert.True(iridium.MetalRich);
        Assert.False(iridium.Rocky);

        var rhodplumsite = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Rhodplumsite");
        Assert.True(rhodplumsite.HighMetalContent);
        Assert.True(rhodplumsite.MetalRich);
        Assert.False(rhodplumsite.Rocky);

        var diamond = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Diamond");
        Assert.True(diamond.RockyIce);

        Assert.True(SurfaceMiningCommodityCatalog.TryResolve(
            "low temp diamonds",
            out var aliasedDiamonds));
        Assert.Equal("Low Temperature Diamonds", aliasedDiamonds.Name);
        Assert.True(SurfaceMiningCommodityCatalog.TryResolve(
            "methanol crystals",
            out var aliasedMethanol));
        Assert.Equal("Methanol Monohydrate Crystals", aliasedMethanol.Name);
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

    private static MineMapSurvey LegacySurvey(Guid id, int signal)
    {
        var now = DateTimeOffset.UtcNow;
        return new MineMapSurvey
        {
            Id = id,
            FrontierId = "F123",
            CommanderName = "Fenris",
            SystemName = "Wille",
            SystemAddress = 123456789,
            SystemPosition = new GalacticCoordinate(1, 2, 3),
            BodyId = 5,
            BodyName = "Wille 2 C",
            BodyType = "Rocky Ice body",
            ArrivalDistanceLs = 129.5,
            LocationSignal = signal,
            MineralAmount = MineMapRating.High,
            Density = MineMapRating.Low,
            PlanetRadiusMeters = 855_573.1875,
            Center = new SurfaceCoordinate(1, 2),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

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
