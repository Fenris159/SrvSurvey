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
        Assert.Contains("bearing 90°", result.Message, StringComparison.OrdinalIgnoreCase);

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
    public async Task GuidedSurveyWalksFromBorderThroughCenterAndEveryScanWaypoint()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext border = Context(new SurfaceCoordinate(0, 0));
        using var service = new MineMapService(directory.Path);

        MineMapCommandResult started = await service.ExecuteAsync(".MiNiNg SuRvEy", border);

        Assert.True(started.Succeeded);
        Assert.Equal(MineMapSurveyGuidePhase.Border, service.SurveyGuide?.Phase);

        Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
        MineMapSurvey survey = service.ActiveSurvey!;
        Assert.Equal(MineMapSurveyGuidePhase.Center, service.SurveyGuide?.Phase);

        service.UpdateContext(border with { PlayerLocation = survey.Center });
        Assert.Equal(MineMapSurveyGuidePhase.ConfirmCenter, service.SurveyGuide?.Phase);

        Assert.True(
            (
                await service.ExecuteAsync(".mining center here", border with { PlayerLocation = survey.Center })
            ).Succeeded
        );
        Assert.Equal(MineMapSurveyGuidePhase.Waypoint, service.SurveyGuide?.Phase);
        Assert.NotEmpty(service.SurveyGuide!.Waypoints!);

        while (service.SurveyGuide is { Phase: MineMapSurveyGuidePhase.Waypoint, CurrentWaypoint: { } waypoint })
        {
            service.UpdateContext(border with { PlayerLocation = waypoint });
        }

        Assert.Equal(MineMapSurveyGuidePhase.Complete, service.SurveyGuide?.Phase);
        service.DismissSurveyGuide();
        Assert.Null(service.SurveyGuide);
    }

    [Fact]
    public async Task RestartingGuidedSurveyBeforeCenterReturnsToBorderSetup()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext border = Context(new SurfaceCoordinate(0, 0));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining survey", border)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
        Assert.Equal(MineMapSurveyGuidePhase.Center, service.SurveyGuide?.Phase);

        MineMapCommandResult restarted = await service.ExecuteAsync(".mining survey", border);

        Assert.True(restarted.Succeeded);
        Assert.Equal(MineMapSurveyGuidePhase.Border, service.SurveyGuide?.Phase);
        Assert.Null(service.SurveyGuide?.SurveyId);
        Assert.Contains("border setup", restarted.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
        Assert.Single(service.Surveys);
    }

    [Fact]
    public async Task RestartingGuidedSurveyDuringWaypointsReturnsToFirstWaypoint()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext border = Context(new SurfaceCoordinate(0, 0));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining survey", border)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
        MineMapSurvey survey = service.ActiveSurvey!;
        Assert.True(
            (
                await service.ExecuteAsync(".mining center here", border with { PlayerLocation = survey.Center })
            ).Succeeded
        );
        SurfaceCoordinate firstWaypoint = Assert.IsType<SurfaceCoordinate>(service.SurveyGuide?.CurrentWaypoint);
        service.UpdateContext(border with { PlayerLocation = firstWaypoint });
        Assert.Equal(1, service.SurveyGuide?.WaypointIndex);

        MineMapCommandResult restarted = await service.ExecuteAsync(
            ".mining survey",
            border with
            {
                PlayerLocation = firstWaypoint,
            }
        );

        Assert.True(restarted.Succeeded);
        Assert.Equal(MineMapSurveyGuidePhase.Waypoint, service.SurveyGuide?.Phase);
        Assert.Equal(0, service.SurveyGuide?.WaypointIndex);
        Assert.Equal(firstWaypoint, service.SurveyGuide?.CurrentWaypoint);
        Assert.Contains("waypoint 1", restarted.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuidedSurveyProgressResumesAfterServiceRestart()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext border = Context(new SurfaceCoordinate(0, 0));
        SurfaceCoordinate resumeLocation;
        SurfaceCoordinate expectedWaypoint;

        using (var service = new MineMapService(directory.Path))
        {
            Assert.True((await service.ExecuteAsync(".mining survey", border)).Succeeded);
            Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
            MineMapSurvey survey = service.ActiveSurvey!;
            Assert.True(
                (
                    await service.ExecuteAsync(".mining center here", border with { PlayerLocation = survey.Center })
                ).Succeeded
            );
            SurfaceCoordinate firstWaypoint = Assert.IsType<SurfaceCoordinate>(service.SurveyGuide?.CurrentWaypoint);
            service.UpdateContext(border with { PlayerLocation = firstWaypoint });
            Assert.Equal(1, service.SurveyGuide?.WaypointIndex);
            resumeLocation = firstWaypoint;
            expectedWaypoint = Assert.IsType<SurfaceCoordinate>(service.SurveyGuide?.CurrentWaypoint);
        }

        using var restored = new MineMapService(directory.Path);
        Assert.Equal(MineMapSurveyGuidePhase.Waypoint, restored.SurveyGuide?.Phase);
        Assert.Equal(1, restored.SurveyGuide?.WaypointIndex);
        Assert.Equal(expectedWaypoint, restored.SurveyGuide?.CurrentWaypoint);

        restored.UpdateContext(border with { PlayerLocation = resumeLocation });

        Assert.Equal(MineMapSurveyGuidePhase.Waypoint, restored.SurveyGuide?.Phase);
        Assert.Equal(1, restored.SurveyGuide?.WaypointIndex);
        Assert.Equal(expectedWaypoint, restored.SurveyGuide?.CurrentWaypoint);
    }

    [Fact]
    public void GuidedSurveySpiralCoversTheSavedAreaWhileDriving()
    {
        MineMapSurvey survey = LegacySurvey(Guid.NewGuid(), signal: 4);
        IReadOnlyList<SurfaceCoordinate> waypoints = MineMapService.CreateSurveyWaypoints(survey);

        Assert.NotEmpty(waypoints);
        Assert.All(
            waypoints,
            waypoint =>
                Assert.True(
                    SurfaceNavigation.GetDistance(survey.Center, waypoint, survey.PlanetRadiusMeters)
                        < survey.LocationRadiusMeters
                )
        );
        AssertSurveyRouteCoverage(survey, waypoints);
    }

    [Fact]
    public void GuidedSurveyUsesAnImmediateInsetSweepWhenTheSiteIsTooSmallForAFullSpiralTurn()
    {
        MineMapSurvey survey = LegacySurvey(Guid.NewGuid(), signal: 4) with { LocationRadiusMeters = 3_000 };
        IReadOnlyList<SurfaceCoordinate> waypoints = MineMapService.CreateSurveyWaypoints(survey);

        Assert.NotEmpty(waypoints);
        AssertSurveyRouteCoverage(survey, waypoints);
    }

    private static void AssertSurveyRouteCoverage(MineMapSurvey survey, IReadOnlyList<SurfaceCoordinate> waypoints)
    {
        SurfaceCoordinate[] route = [survey.Center, .. waypoints];
        for (int index = 1; index < route.Length; index++)
        {
            Assert.InRange(
                SurfaceNavigation.GetDistance(route[index - 1], route[index], survey.PlanetRadiusMeters),
                0,
                MineMapService.SurveyScannerRadiusMeters + 0.01
            );
        }

        List<SurfaceCoordinate> scanLocations = SampleDrivenRoute(route, survey.PlanetRadiusMeters);
        for (double radius = 0; radius <= survey.LocationRadiusMeters; radius += 250)
        {
            for (int bearing = 0; bearing < 360; bearing += 5)
            {
                SurfaceCoordinate sample = MineMapService.GetDestination(
                    survey.Center,
                    bearing,
                    radius,
                    survey.PlanetRadiusMeters
                );
                double nearest = scanLocations.Min(location =>
                    SurfaceNavigation.GetDistance(sample, location, survey.PlanetRadiusMeters)
                );
                Assert.InRange(nearest, 0, MineMapService.SurveyScannerRadiusMeters);
            }
        }
    }

    private static List<SurfaceCoordinate> SampleDrivenRoute(SurfaceCoordinate[] route, double planetRadiusMeters)
    {
        var samples = new List<SurfaceCoordinate> { route[0] };
        for (int index = 1; index < route.Length; index++)
        {
            SurfaceCoordinate start = route[index - 1];
            SurfaceCoordinate end = route[index];
            double distance = SurfaceNavigation.GetDistance(start, end, planetRadiusMeters);
            double bearing = SurfaceNavigation.GetBearing(start, end);
            int segmentCount = Math.Max(1, (int)Math.Ceiling(distance / 50));
            for (int segment = 1; segment <= segmentCount; segment++)
            {
                samples.Add(
                    MineMapService.GetDestination(start, bearing, distance * segment / segmentCount, planetRadiusMeters)
                );
            }
        }

        return samples;
    }

    [Fact]
    public async Task ContextActivatesSurveyInsideItsBorderAndUnloadsItAfterExit()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext border = Context(new SurfaceCoordinate(0, 0));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 90 6.44 4", border)).Succeeded);
        MineMapSurvey survey = Assert.Single(service.Surveys);

        service.UpdateContext(null);
        Assert.Null(service.ActiveSurvey);

        service.UpdateContext(border with { PlayerLocation = survey.Center });
        Assert.Equal(survey.Id, service.ActiveSurvey?.Id);

        SurfaceCoordinate outside = MineMapService.GetDestination(
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
        MineMapCommandContext border = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", border)).Succeeded);
        MineMapSurvey original = service.ActiveSurvey!;
        Assert.True(
            (
                await service.ExecuteAsync(".mine ruby high/low here", border with { PlayerLocation = original.Center })
            ).Succeeded
        );
        original = service.ActiveSurvey!;
        MineMapMarker marker = Assert.Single(original.Markers);
        SurfaceCoordinate newCenter = MineMapService.GetDestination(
            original.Center,
            90,
            500,
            original.PlanetRadiusMeters
        );

        MineMapCommandResult result = await service.ExecuteAsync(
            ".MiNiNg CeNtEr HeRe",
            border with
            {
                PlayerLocation = newCenter,
            }
        );

        Assert.True(result.Succeeded);
        Assert.Contains("preserved", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original.Id, service.ActiveSurvey?.Id);
        Assert.Equal(original.LocationRadiusMeters, service.ActiveSurvey?.LocationRadiusMeters);
        Assert.Equal(newCenter, service.ActiveSurvey?.Center);
        MineMapMarker preserved = Assert.Single(service.ActiveSurvey!.Markers);
        Assert.Equal(marker.Id, preserved.Id);
        Assert.Equal(marker.Location, preserved.Location);

        using var reloaded = new MineMapService(directory.Path);
        MineMapSurvey persisted = Assert.Single(reloaded.Surveys);
        Assert.Equal(newCenter, persisted.Center);
        Assert.Equal(marker.Location, Assert.Single(persisted.Markers).Location);
    }

    [Fact]
    public async Task SplatTraceClosesAtItsStartAndCreatesSeparatedRigSuggestions()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext border = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", border)).Succeeded);
        SurfaceCoordinate deposit = service.ActiveSurvey!.Center;
        Assert.True(
            (
                await service.ExecuteAsync(".mine ruby high/medium here", border with { PlayerLocation = deposit })
            ).Succeeded
        );
        const double splatRadius = 100;
        SurfaceCoordinate start = MineMapService.GetDestination(deposit, 0, splatRadius, border.PlanetRadiusMeters);
        string? notification = null;
        service.NotificationRequested += message => notification = message;

        MineMapCommandResult started = await service.ExecuteAsync(
            ".MiNe SpLaT",
            border with
            {
                PlayerLocation = start,
            }
        );

        Assert.True(started.Succeeded);
        Assert.True(Assert.Single(service.ActiveSurvey.Markers).IsSplatTraceActive);
        for (int bearing = 30; bearing <= 360; bearing += 30)
        {
            SurfaceCoordinate point = MineMapService.GetDestination(
                deposit,
                bearing % 360,
                splatRadius,
                border.PlanetRadiusMeters
            );
            service.UpdateContext(border with { PlayerLocation = point });
        }

        MineMapMarker traced = Assert.Single(service.ActiveSurvey.Markers);
        Assert.False(traced.IsSplatTraceActive);
        Assert.True(traced.SplatBoundary.Count >= 8);
        Assert.NotEmpty(traced.SuggestedRigLocations);
        Assert.Contains("boundary complete", notification, StringComparison.OrdinalIgnoreCase);
        for (int first = 0; first < traced.SuggestedRigLocations.Count; first++)
        {
            for (int second = first + 1; second < traced.SuggestedRigLocations.Count; second++)
            {
                Assert.InRange(
                    SurfaceNavigation.GetDistance(
                        traced.SuggestedRigLocations[first],
                        traced.SuggestedRigLocations[second],
                        border.PlanetRadiusMeters
                    ),
                    SurfaceMiningGeometry.ExclusionDistanceMeters - 0.01,
                    double.MaxValue
                );
            }
        }

        using var reloaded = new MineMapService(directory.Path);
        MineMapMarker persisted = Assert.Single(Assert.Single(reloaded.Surveys).Markers);
        Assert.Equal(traced.SplatBoundary, persisted.SplatBoundary);
        Assert.Equal(traced.SuggestedRigLocations, persisted.SuggestedRigLocations);
    }

    [Fact]
    public async Task SplatTraceCanBeCancelledWithoutChangingTheDepositOrRigCount()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        SurfaceCoordinate deposit = service.ActiveSurvey!.Center;
        context = context with { PlayerLocation = deposit };
        Assert.True((await service.ExecuteAsync(".mine ruby high/medium here", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine rigs 3", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine splat", context)).Succeeded);

        MineMapCommandResult cancelled = await service.ExecuteAsync(".mine splat cancel", context);

        Assert.True(cancelled.Succeeded);
        MineMapMarker marker = Assert.Single(service.ActiveSurvey.Markers);
        Assert.Equal(deposit, marker.Location);
        Assert.Equal(3, marker.RigCount);
        Assert.False(marker.IsSplatTraceActive);
        Assert.Empty(marker.SplatBoundary);
        Assert.Empty(marker.SuggestedRigLocations);
    }

    [Fact]
    public void SplatPlannerRejectsAnUnreasonablyLargeBoundary()
    {
        const double planetRadiusMeters = 1_000_000;
        var origin = new SurfaceCoordinate(0, 0);
        SurfaceCoordinate[] boundary =
        [
            origin,
            MineMapService.GetDestination(origin, 90, 30_000, planetRadiusMeters),
            MineMapService.GetDestination(origin, 180, 30_000, planetRadiusMeters),
        ];

        IReadOnlyList<SurfaceCoordinate> suggestions = SurfaceMiningSplatPlanner.CreateRigLayout(
            boundary,
            planetRadiusMeters,
            SurfaceMiningGeometry.ExclusionDistanceMeters
        );

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task MineCommandsAddRelativeAndHereMarkersAndDeleteNearestHere()
    {
        using var directory = new TemporaryDirectory();
        var border = Context(new SurfaceCoordinate(1, 2));
        var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", border)).Succeeded);
        SurfaceCoordinate center = service.ActiveSurvey!.Center;
        SurfaceCoordinate observationPoint = MineMapService.GetDestination(center, 90, 500, border.PlanetRadiusMeters);

        var relative = await service.ExecuteAsync(
            ".mine 15 ruby 1.24 medium/low",
            border with
            {
                PlayerLocation = observationPoint,
            }
        );
        Assert.True(relative.Succeeded);
        MineMapMarker first = Assert.Single(service.ActiveSurvey.Markers);
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
        MineMapMarker second = Assert.Single(
            service.ActiveSurvey.Markers,
            marker => marker.Material == "Low Temperature Diamonds"
        );
        Assert.Equal(MineMapRating.Low, second.MineralAmount);
        Assert.Equal(MineMapRating.High, second.Density);

        var deleted = await service.ExecuteAsync(".mine delete here", border with { PlayerLocation = here });
        Assert.True(deleted.Succeeded);
        Assert.Single(service.ActiveSurvey.Markers);
        Assert.Contains("removed", deleted.Message, StringComparison.OrdinalIgnoreCase);

        SurfaceCoordinate outside = MineMapService.GetDestination(center, 270, 3_500, border.PlanetRadiusMeters);
        MineMapCommandResult outsideResult = await service.ExecuteAsync(
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
        MineMapCommandContext context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 ruby 1.00 high/low", context)).Succeeded);

        MineMapCommandResult duplicate = await service.ExecuteAsync(".mine 180 ruby 1.05 medium/high", context);

        Assert.False(duplicate.Succeeded);
        Assert.Contains("100 m", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("here", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        MineMapMarker first = Assert.Single(service.ActiveSurvey!.Markers);

        MineMapCommandResult differentCommodity = await service.ExecuteAsync(
            ".mine 180 gold 1.05 medium/high",
            context
        );
        Assert.True(differentCommodity.Succeeded);

        MineMapCommandResult overlap = await service.ExecuteAsync(
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
        MineMapCommandContext context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 haematite 1.00 high/low", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 haematite 1.50 medium/high", context)).Succeeded);
        MineMapMarker first = service.ActiveSurvey!.Markers[0];
        MineMapMarker second = service.ActiveSurvey.Markers[1];
        SurfaceCoordinate playerLocation = MineMapService.GetDestination(
            first.Location,
            90,
            50,
            service.ActiveSurvey.PlanetRadiusMeters
        );

        MineMapCommandResult moved = await service.ExecuteAsync(
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

        MineMapCommandResult tooFar = await service.ExecuteAsync(
            ".mine move haematite here",
            context with
            {
                PlayerLocation = service.ActiveSurvey.Center,
            }
        );
        Assert.False(tooFar.Succeeded);
        Assert.Contains("200 m", tooFar.Message, StringComparison.OrdinalIgnoreCase);

        using var reloaded = new MineMapService(directory.Path);
        MineMapSurvey persisted = Assert.Single(reloaded.Surveys);
        Assert.Equal(playerLocation, persisted.Markers[0].Location);
        Assert.Equal(second.Location, persisted.Markers[1].Location);
    }

    [Fact]
    public async Task MineRigsSetsNearestMarkerCapacityAndPersistsIt()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 ruby 0.50 high/low", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine 180 gold 1.00 medium/high", context)).Succeeded);
        MineMapMarker ruby = service.ActiveSurvey!.Markers[0];
        MineMapMarker gold = service.ActiveSurvey.Markers[1];
        SurfaceCoordinate playerLocation = MineMapService.GetDestination(
            gold.Location,
            90,
            25,
            service.ActiveSurvey.PlanetRadiusMeters
        );

        MineMapCommandResult result = await service.ExecuteAsync(
            ".MiNe RiGs 4",
            context with
            {
                PlayerLocation = playerLocation,
            }
        );

        Assert.True(result.Succeeded);
        Assert.Contains("Gold", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 rigs", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(service.ActiveSurvey.Markers.Single(marker => marker.Id == ruby.Id).RigCount);
        Assert.Equal(4, service.ActiveSurvey.Markers.Single(marker => marker.Id == gold.Id).RigCount);

        using var reloaded = new MineMapService(directory.Path);
        MineMapSurvey persisted = Assert.Single(reloaded.Surveys);
        Assert.Equal(4, persisted.Markers.Single(marker => marker.Id == gold.Id).RigCount);
    }

    [Theory]
    [InlineData(".mine rigs 0")]
    [InlineData(".mine rigs -1")]
    [InlineData(".mine rigs many")]
    [InlineData(".mine rigs 2 extra")]
    public async Task MineRigsRejectsInvalidCounts(string command)
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);
        Assert.True((await service.ExecuteAsync(".mine ruby high/low here", context)).Succeeded);

        MineMapCommandResult result = await service.ExecuteAsync(command, context);

        Assert.False(result.Succeeded);
        Assert.Contains("positive number", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.Single(service.ActiveSurvey!.Markers).RigCount);
    }

    [Fact]
    public async Task MineRigsRequiresAnExistingMarker()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext context = Context(new SurfaceCoordinate(1, 2));
        using var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".mining 180 3.25 7", context)).Succeeded);

        MineMapCommandResult result = await service.ExecuteAsync(".mine rigs 2", context);

        Assert.False(result.Succeeded);
        Assert.Contains("Add a mine marker", result.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task ExtremeFiniteKilometerValuesAreRejectedBeforeProjection()
    {
        using var directory = new TemporaryDirectory();
        MineMapCommandContext context = Context(new SurfaceCoordinate(10, 20));
        var service = new MineMapService(directory.Path);
        string extreme = double.MaxValue.ToString("R", global::System.Globalization.CultureInfo.InvariantCulture);

        MineMapCommandResult surveyResult = await service.ExecuteAsync($".mining 120 {extreme} 4", context);

        Assert.False(surveyResult.Succeeded);
        Assert.Empty(service.Surveys);
        Assert.True((await service.ExecuteAsync(".mining 120 6.44 4", context)).Succeeded);

        MineMapCommandResult markerResult = await service.ExecuteAsync($".mine 15 ruby {extreme} high/low", context);

        Assert.False(markerResult.Succeeded);
        Assert.Empty(service.ActiveSurvey!.Markers);
    }

    [Fact]
    public async Task MineCommandCanonicalizesCaseAndRejectsNamesOutsideHotspotList()
    {
        using var directory = new TemporaryDirectory();
        var context = Context(new SurfaceCoordinate(10, 20));
        var service = new MineMapService(directory.Path);
        Assert.True((await service.ExecuteAsync(".MINING 120 6.44 4", context)).Succeeded);

        MineMapCommandResult accepted = await service.ExecuteAsync(
            ".MINE 15 pErIcLaSe DuNiTe 1.24 HIGH/MEDIUM",
            context
        );
        MineMapCommandResult rejected = await service.ExecuteAsync(".mine 15 unobtainium 1.24 high/low", context);

        Assert.True(accepted.Succeeded);
        MineMapMarker marker = Assert.Single(service.ActiveSurvey!.Markers);
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
