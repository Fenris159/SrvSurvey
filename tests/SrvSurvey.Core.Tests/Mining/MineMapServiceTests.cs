using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

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
        catalog.Save(
            new GalacticBookmark
            {
                Id = first.Id,
                System = first.SystemName,
                Body = first.BodyName,
                CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
                SurfaceMiningMap = first,
            }
        );
        File.WriteAllText(Path.Combine(legacyDirectory, "first.json"), JsonSerializer.Serialize(first));
        File.WriteAllText(Path.Combine(legacyDirectory, "second.json"), JsonSerializer.Serialize(second));

        using var service = new MineMapService(directory.Path, catalog);

        Assert.Equal(2, service.Surveys.Count);
        Assert.Contains(service.Surveys, survey => survey.Id == first.Id);
        Assert.Contains(service.Surveys, survey => survey.Id == second.Id);
        Assert.True(File.Exists(Path.Combine(legacyDirectory, ".bookmarks-migrated")));
    }

    [Fact]
    public void LegacyMigrationSkipsInvalidSurveyAndFinishesRemainingImports()
    {
        using var directory = new TemporaryDirectory();
        var legacyDirectory = Path.Combine(directory.Path, "mine-maps");
        Directory.CreateDirectory(legacyDirectory);
        var invalid = LegacySurvey(Guid.NewGuid(), signal: 4) with { SystemAddress = 0 };
        var valid = LegacySurvey(Guid.NewGuid(), signal: 5);
        File.WriteAllText(Path.Combine(legacyDirectory, "invalid.json"), JsonSerializer.Serialize(invalid));
        File.WriteAllText(Path.Combine(legacyDirectory, "valid.json"), JsonSerializer.Serialize(valid));

        using var service = new MineMapService(directory.Path);

        Assert.DoesNotContain(service.Surveys, survey => survey.Id == invalid.Id);
        Assert.Contains(service.Surveys, survey => survey.Id == valid.Id);
        Assert.True(File.Exists(Path.Combine(legacyDirectory, ".bookmarks-migrated")));
    }

    [Fact]
    public async Task MiningCommandCreatesPersistentSurveyAtKnownRadiusAndBearing()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(0, 0));
        var service = new MineMapService(directory.Path);

        var result = await service.ExecuteAsync(".mining 90 6.44 4", context);

        Assert.True(result.Succeeded);
        var survey = Assert.Single(service.Surveys);
        Assert.Equal("Mining Location Signal 4", survey.Name);
        Assert.Equal(6_440, survey.LocationRadiusMeters);
        Assert.InRange(survey.Center.Latitude, -0.000001, 0.000001);
        Assert.InRange(
            SurfaceNavigation.GetDistance(context.PlayerLocation!.Value, survey.Center, context.PlanetRadiusMeters),
            6439.99,
            6440.01
        );
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
    public async Task ContextActivatesSurveyInsideItsBorderAndUnloadsItAfterExit()
    {
        using var directory = new TemporaryDirectory();
        var border = Context(new SurfaceCoordinate(0, 0));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
        var survey = Assert.Single(service.Surveys);

        service.UpdateContext(null);
        Assert.Null(service.ActiveSurvey);

        service.UpdateContext(border with { PlayerLocation = survey.Center });
        Assert.Equal(survey.Id, service.ActiveSurvey?.Id);

        var outside = MineMapService.GetDestination(
            survey.Center,
            270,
            survey.LocationRadiusMeters + 100,
            survey.PlanetRadiusMeters
        );
        service.UpdateContext(border with { PlayerLocation = outside });
        Assert.Null(service.ActiveSurvey);
    }

    [Fact]
    public async Task MiningCenterHereMovesOnlyTheSurveyCenterAndPersistsIt()
    {
        using var directory = new TemporaryDirectory();
        var border = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", border)).Succeeded);
        var original = service.ActiveSurvey!;
        Assert.True(
            (
                await service.ExecuteAsync(".mine ruby high/low here", border with { PlayerLocation = original.Center })
            ).Succeeded
        );
        original = service.ActiveSurvey!;
        var marker = Assert.Single(original.Markers);
        var newCenter = MineMapService.GetDestination(original.Center, 90, 500, original.PlanetRadiusMeters);

        var result = await service.ExecuteAsync(".MiNiNg CeNtEr HeRe", border with { PlayerLocation = newCenter });

        Assert.True(result.Succeeded);
        Assert.Contains("preserved", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original.Id, service.ActiveSurvey?.Id);
        Assert.Equal(original.LocationRadiusMeters, service.ActiveSurvey?.LocationRadiusMeters);
        Assert.Equal(newCenter, service.ActiveSurvey?.Center);
        var preserved = Assert.Single(service.ActiveSurvey!.Markers);
        Assert.Equal(marker.Id, preserved.Id);
        Assert.Equal(marker.Location, preserved.Location);

        using var reloaded = new MineMapService(directory.Path);
        var persisted = Assert.Single(reloaded.Surveys);
        Assert.Equal(newCenter, persisted.Center);
        Assert.Equal(marker.Location, Assert.Single(persisted.Markers).Location);
    }

    [Fact]
    public async Task MineCommandsAddRelativeAndHereMarkersAndDeleteNearestHere()
    {
        using var directory = new TemporaryDirectory();
        var border = Context(new SurfaceCoordinate(1, 2));
        var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", border)).Succeeded);
        var center = service.ActiveSurvey!.Center;
        var observationPoint = MineMapService.GetDestination(center, 90, 500, border.PlanetRadiusMeters);

        var relative = await service.ExecuteAsync(
            ".mine 15 ruby 1.24 medium/low",
            border with
            {
                PlayerLocation = observationPoint,
            }
        );
        Assert.True(relative.Succeeded);
        var first = Assert.Single(service.ActiveSurvey!.Markers);
        Assert.Equal("Ruby", first.Material);
        Assert.Equal(MineMapRating.Medium, first.MineralAmount);
        Assert.Equal(MineMapRating.Low, first.Density);
        Assert.InRange(
            SurfaceNavigation.GetDistance(observationPoint, first.Location, border.PlanetRadiusMeters),
            1239.99,
            1240.01
        );
        Assert.InRange(SurfaceNavigation.GetBearing(observationPoint, first.Location), 14.99, 15.01);
        Assert.Contains("from your position", relative.Message, StringComparison.OrdinalIgnoreCase);

        var here = first.Location;
        Assert.True(
            (
                await service.ExecuteAsync(
                    ".mine low temperature diamonds low/high here",
                    border with
                    {
                        PlayerLocation = here,
                    }
                )
            ).Succeeded
        );
        Assert.Equal(2, service.ActiveSurvey.Markers.Count);
        var second = Assert.Single(
            service.ActiveSurvey.Markers,
            marker => marker.Material == "Low Temperature Diamonds"
        );
        Assert.Equal(MineMapRating.Low, second.MineralAmount);
        Assert.Equal(MineMapRating.High, second.Density);

        var deleted = await service.ExecuteAsync(".mine delete here", border with { PlayerLocation = here });
        Assert.True(deleted.Succeeded);
        Assert.Single(service.ActiveSurvey.Markers);
        Assert.Contains("removed", deleted.Message, StringComparison.OrdinalIgnoreCase);

        var outside = MineMapService.GetDestination(center, 270, 3_500, border.PlanetRadiusMeters);
        var outsideResult = await service.ExecuteAsync(
            ".mine 15 ruby 1.24 high/medium",
            border with
            {
                PlayerLocation = outside,
            }
        );
        Assert.False(outsideResult.Succeeded);
        Assert.Contains("inside", outsideResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BearingPlacementRejectsNearbyDuplicateMaterialWhileHereAllowsOverlap()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 ruby 1.00 high/low", context)).Succeeded);

        var duplicate = await service.ExecuteAsync(".mine 180 ruby 1.05 medium/high", context);

        Assert.False(duplicate.Succeeded);
        Assert.Contains("100 m", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("here", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        var first = Assert.Single(service.ActiveSurvey!.Markers);

        var differentCommodity = await service.ExecuteAsync(".mine 180 gold 1.05 medium/high", context);
        Assert.True(differentCommodity.Succeeded);

        var overlap = await service.ExecuteAsync(
            ".mine ruby low/medium here",
            context with
            {
                PlayerLocation = first.Location,
            }
        );

        Assert.True(overlap.Succeeded);
        Assert.Equal(3, service.ActiveSurvey.Markers.Count);
        Assert.Equal(2, service.ActiveSurvey.Markers.Count(marker => marker.Material == "Ruby"));
    }

    [Fact]
    public async Task MoveMarkerHereMovesNearestMatchingCommodityWithinTwoHundredMetersAndPersistsIt()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 haematite 1.00 high/low", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 haematite 1.50 medium/high", context)).Succeeded);
        var first = service.ActiveSurvey!.Markers[0];
        var second = service.ActiveSurvey.Markers[1];
        var playerLocation = MineMapService.GetDestination(
            first.Location,
            90,
            50,
            service.ActiveSurvey.PlanetRadiusMeters
        );

        var moved = await service.ExecuteAsync(
            ".MiNe MoVe HaEmAtItE HeRe",
            context with
            {
                PlayerLocation = playerLocation,
            }
        );

        Assert.True(moved.Succeeded);
        Assert.Contains("50 m", moved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(playerLocation, service.ActiveSurvey.Markers[0].Location);
        Assert.Equal(first.Id, service.ActiveSurvey.Markers[0].Id);
        Assert.Equal(second.Location, service.ActiveSurvey.Markers[1].Location);

        var tooFar = await service.ExecuteAsync(
            ".mine move haematite here",
            context with
            {
                PlayerLocation = service.ActiveSurvey.Center,
            }
        );
        Assert.False(tooFar.Succeeded);
        Assert.Contains("200 m", tooFar.Message, StringComparison.OrdinalIgnoreCase);

        using var reloaded = new MineMapService(directory.Path);
        var persisted = Assert.Single(reloaded.Surveys);
        Assert.Equal(playerLocation, persisted.Markers[0].Location);
        Assert.Equal(second.Location, persisted.Markers[1].Location);
    }

    [Fact]
    public async Task JournalCommandsReturnFeedbackAndIgnoreBootstrapMutations()
    {
        using var directory = new TemporaryDirectory();
        var service = new MineMapService(directory.Path);
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var command,
                out _
            )
        );

        var ignored = await service.ApplyJournalEventsAsync(
            [command!],
            Context(new SurfaceCoordinate(10, 20)),
            allowMutations: false
        );
        Assert.Empty(ignored);
        Assert.Empty(service.Surveys);

        var applied = await service.ApplyJournalEventsAsync(
            [command!],
            Context(new SurfaceCoordinate(10, 20)),
            allowMutations: true
        );
        Assert.True(Assert.Single(applied).Succeeded);
        Assert.Single(service.Surveys);
    }

    [Theory]
    [InlineData(".mining 360 6.44 4")]
    [InlineData(".mining 120 0 4")]
    [InlineData(".mining 120 wide 4")]
    [InlineData(".mining 120 6.44 0")]
    [InlineData(".mining center elsewhere")]
    [InlineData(".mine 15 ruby -1 high/low")]
    [InlineData(".mine ruby high/low somewhere")]
    [InlineData(".mine 15 ruby 1.2 very-high/low")]
    [InlineData(".mine 15 unobtainium 1.2 high/low")]
    public async Task InvalidCommandsReturnActionableFeedback(string command)
    {
        using var directory = new TemporaryDirectory();
        var service = new MineMapService(directory.Path);

        var result = await service.ExecuteAsync(command, Context(new SurfaceCoordinate(10, 20)));

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task MineCommandCanonicalizesCaseAndRejectsNamesOutsideHotspotList()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(10, 20));
        var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".MINING 120 6.44 4", context)).Succeeded);

        var accepted = await service.ExecuteAsync(".MINE 15 pErIcLaSe DuNiTe 1.24 HIGH/MEDIUM", context);
        var rejected = await service.ExecuteAsync(".mine 15 unobtainium 1.24 high/low", context);

        Assert.True(accepted.Succeeded);
        var marker = Assert.Single(service.ActiveSurvey!.Markers);
        Assert.Equal("Periclase Dunite", marker.Material);
        Assert.Equal(MineMapRating.Medium, marker.Density);
        Assert.False(rejected.Succeeded);
        Assert.Contains("Hotspot List", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SurfaceHuntReferenceDrivesHotspotAvailabilityPricesAndNames()
    {
        Assert.Equal(37, SurfaceMiningCommodityCatalog.HuntReferences.Count);
        Assert.Equal(37, SurfaceMiningCommodityCatalog.All.Count);
        Assert.Equal(
            SurfaceMiningCommodityCatalog
                .HuntReferences.Select(row => row.Material)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            SurfaceMiningCommodityCatalog
                .All.Select(row => row.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        );
        Assert.Equal(
            SurfaceMiningCommodityCatalog.All.Count,
            SurfaceMiningCommodityCatalog
                .All.Select(commodity => commodity.ColorHex)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        );

        var helium = Assert.Single(SurfaceMiningCommodityCatalog.All, commodity => commodity.Name == "Helium");
        Assert.True(helium.HighMetalContent);
        Assert.True(helium.RockyIce);
        Assert.False(helium.Icy);
        Assert.Equal(591_360, helium.MaximumSellPrice);

        var lowTempDiamonds = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Low Temperature Diamonds"
        );
        Assert.True(lowTempDiamonds.Rocky);
        Assert.True(lowTempDiamonds.RockyIce);
        Assert.True(lowTempDiamonds.Icy);

        var iridium = Assert.Single(SurfaceMiningCommodityCatalog.All, commodity => commodity.Name == "Iridium");
        Assert.True(iridium.HighMetalContent);
        Assert.True(iridium.MetalRich);
        Assert.False(iridium.Rocky);

        var rhodplumsite = Assert.Single(
            SurfaceMiningCommodityCatalog.All,
            commodity => commodity.Name == "Rhodplumsite"
        );
        Assert.True(rhodplumsite.HighMetalContent);
        Assert.True(rhodplumsite.MetalRich);
        Assert.False(rhodplumsite.Rocky);

        var diamond = Assert.Single(SurfaceMiningCommodityCatalog.All, commodity => commodity.Name == "Diamond");
        Assert.True(diamond.RockyIce);

        Assert.True(SurfaceMiningCommodityCatalog.TryResolve("low temp diamonds", out var aliasedDiamonds));
        Assert.Equal("Low Temperature Diamonds", aliasedDiamonds.Name);
        Assert.True(SurfaceMiningCommodityCatalog.TryResolve("methanol crystals", out var aliasedMethanol));
        Assert.Equal("Methanol Monohydrate Crystals", aliasedMethanol.Name);
    }

    private static MineMapCommandContext Context(SurfaceCoordinate location) =>
        new(
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
            location
        );

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
            LocationRadiusMeters = 6_440,
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
                "SrvSurvey-MineMap-" + Guid.NewGuid().ToString("N")
            );
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
