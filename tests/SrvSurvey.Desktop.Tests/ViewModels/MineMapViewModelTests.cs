using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MineMapViewModelTests
{
    [Fact]
    public void EmptyCatalogDoesNotWriteFabricatedBookmarks()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );

        Assert.Null(viewModel.ActiveSurvey);
        Assert.Equal(["All"], viewModel.ContainsOptions);
        Assert.Equal(["All"], viewModel.BodyTypeOptions);
        Assert.Empty(viewModel.FilteredSurveys);
        Assert.Equal(37, viewModel.HotspotRows.Count);
        Assert.Equal(37, viewModel.SurfaceHuntRows.Count);
        Assert.Empty(new BookmarkCatalog(directory.Path).Items);
    }

    [Fact]
    public void BookmarkIdSelectsAndLoadsThePersistedSurvey()
    {
        using var directory = new TemporaryDirectory();
        SeedSurvey(directory.Path);
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        var row = Assert.Single(viewModel.FilteredSurveys);

        Assert.True(viewModel.SelectSurvey(row.Id));

        Assert.Equal(row.Id, viewModel.ActiveSurvey?.Id);
        Assert.Equal(1, viewModel.SelectedTab);
    }

    [Fact]
    public void SurfaceMapFavoritesPersistAndCanFilterTheCatalog()
    {
        using var directory = new TemporaryDirectory();
        SeedSurvey(directory.Path);
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        var row = Assert.Single(viewModel.FilteredSurveys);
        Assert.False(row.IsFavorite);

        row.ToggleFavoriteCommand.Execute(null);

        var favorite = Assert.Single(viewModel.FilteredSurveys);
        Assert.True(favorite.IsFavorite);
        Assert.Equal("★", favorite.FavoriteGlyph);
        Assert.True(Assert.Single(new BookmarkCatalog(directory.Path).Items).IsFavorite);
        viewModel.FavoritesOnly = true;
        Assert.Single(viewModel.FilteredSurveys);

        favorite.ToggleFavoriteCommand.Execute(null);

        Assert.Empty(viewModel.FilteredSurveys);
        viewModel.FavoritesOnly = false;
        Assert.False(Assert.Single(viewModel.FilteredSurveys).IsFavorite);
    }

    [Fact]
    public void OverviewMarkerLabelsDefaultOnAndPersistWhenDisabled()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "ui-settings.json");
        using (var viewModel = new MineMapViewModel(directory.Path, new MineMapSettingsStore(settingsPath), _ => { }))
        {
            Assert.True(viewModel.ShowMarkerLabelsInOverviewMap);
            viewModel.ShowMarkerLabelsInOverviewMap = false;
        }

        using var restored = new MineMapViewModel(directory.Path, new MineMapSettingsStore(settingsPath), _ => { });
        Assert.False(restored.ShowMarkerLabelsInOverviewMap);
    }

    [Fact]
    public void SelectingSurveyRefreshesVisibleMaterialsAndDetailedSelection()
    {
        using var directory = new TemporaryDirectory();
        SeedSurvey(directory.Path);
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        var row = Assert.Single(viewModel.FilteredSurveys);
        Assert.True(viewModel.SelectSurvey(row.Id));

        Assert.Contains(nameof(MineMapViewModel.VisibleMaterials), changes);
        Assert.Equal("LTT 4428", viewModel.SelectedMapSystem);
        Assert.Equal("E 5 a", viewModel.SelectedMapBody);
        Assert.Equal("4", viewModel.SelectedMapSignal);
        Assert.Equal("6.44 km", viewModel.SelectedMapRadius);
        var deposit = Assert.Single(row.Deposits);
        Assert.Equal("Ruby", deposit.Material);
        Assert.Equal("High", deposit.MineralAmount);
        Assert.Equal("Low", deposit.Density);
    }

    [Fact]
    public void MarkerRatingFiltersApplyPerDepositAndCombineWithCommodityVisibility()
    {
        using var directory = new TemporaryDirectory();
        SeedSurveyWithRatingVariants(directory.Path);
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        var row = Assert.Single(viewModel.FilteredSurveys);
        Assert.True(viewModel.SelectSurvey(row.Id));
        var survey = Assert.IsType<MineMapSurvey>(viewModel.ActiveSurvey);
        var highLowRuby = Assert.Single(
            survey.Markers,
            marker =>
                marker.Material == "Ruby"
                && marker.MineralAmount == MineMapRating.High
                && marker.Density == MineMapRating.Low
        );
        var mediumHighRuby = Assert.Single(
            survey.Markers,
            marker =>
                marker.Material == "Ruby"
                && marker.MineralAmount == MineMapRating.Medium
                && marker.Density == MineMapRating.High
        );

        Assert.Equal(["ALL", "HIGH", "MEDIUM", "LOW"], viewModel.MarkerRatingFilterOptions);
        Assert.True(viewModel.VisibleMarkerIds.SetEquals(survey.Markers.Select(marker => marker.Id)));

        viewModel.SelectedMineralAmountFilter = "medium";
        Assert.Equal("MEDIUM", viewModel.SelectedMineralAmountFilter);
        Assert.True(viewModel.VisibleMarkerIds.SetEquals([mediumHighRuby.Id]));

        viewModel.SelectedDensityFilter = "LOW";
        Assert.Empty(viewModel.VisibleMarkerIds);

        viewModel.SelectedMineralAmountFilter = "ALL";
        Assert.True(viewModel.VisibleMarkerIds.SetEquals([highLowRuby.Id]));

        Assert.Single(viewModel.MarkerFilters, filter => filter.Name == "Ruby").IsVisible = false;
        Assert.Empty(viewModel.VisibleMarkerIds);
    }

    [Fact]
    public void SurfaceMapHotspotAndSurfaceHuntSortersExposeTheirActiveDirections()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );

        viewModel.SortSurveysCommand.Execute("SystemName");
        viewModel.SortHotspotsCommand.Execute("Name");
        viewModel.SortSurfaceHuntCommand.Execute("PeakSellPriceValue");

        Assert.Equal("↑", viewModel.SurveySortIndicators["SystemName"]);
        Assert.Equal(string.Empty, viewModel.SurveySortIndicators["BodyType"]);
        Assert.Equal("↑", viewModel.HotspotSortIndicators["Name"]);
        Assert.Equal("↑", viewModel.SurfaceHuntSortIndicators["PeakSellPriceValue"]);
        Assert.Equal(1_931, viewModel.SurfaceHuntRows[0].PeakSellPriceValue);
        viewModel.SortHotspotsCommand.Execute("Name");
        Assert.Equal("↓", viewModel.HotspotSortIndicators["Name"]);
    }

    [Fact]
    public void HotspotSelectionsPersistAndBuildCompactMiningReferenceRows()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "ui-settings.json");
        using (var viewModel = new MineMapViewModel(directory.Path, new MineMapSettingsStore(settingsPath), _ => { }))
        {
            var gold = Assert.Single(viewModel.HotspotRows, row => row.Name == "Gold");
            gold.IsInOverlay = true;

            Assert.True(viewModel.ShouldShowMiningReference);
            var reference = Assert.Single(viewModel.MiningReferenceRows);
            Assert.Equal("HMC, MR, Rocky", reference.BodyTypes);
            Assert.Equal("48,005 CR/t", reference.AverageSellPrice);
        }

        using var restored = new MineMapViewModel(directory.Path, new MineMapSettingsStore(settingsPath), _ => { });
        Assert.True(Assert.Single(restored.HotspotRows, row => row.Name == "Gold").IsInOverlay);
        Assert.Single(restored.MiningReferenceRows);
    }

    [Fact]
    public void LegacyShortCommoditySelectionRestoresAgainstCanonicalName()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "ui-settings.json");
        var settings = new MineMapSettingsStore(settingsPath);
        settings.Save(new MineMapPreferences(false, ["Low Temp Diamonds"]));

        using var viewModel = new MineMapViewModel(directory.Path, settings, _ => { });

        Assert.True(Assert.Single(viewModel.HotspotRows, row => row.Name == "Low Temperature Diamonds").IsInOverlay);
    }

    [Fact]
    public void InstructionsGuideLinkUsesTheApplicationNavigationCallback()
    {
        using var directory = new TemporaryDirectory();
        var launches = 0;
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { },
            () => launches++
        );

        viewModel.OpenSurfaceMiningGuideCommand.Execute(null);

        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task AlignmentCommandTogglesTheSessionHelperAndPublishesStatus()
    {
        using var directory = new TemporaryDirectory();
        var messages = new List<string>();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            messages.Add
        );
        Assert.True(
            JournalEventEnvelope.TryParse("""{"event":"SendText","Message":"  .ALIGNMENT  "}""", out var command, out _)
        );

        await viewModel.ApplyUpdateAsync([command!], null, null, allowCommands: true);

        Assert.True(viewModel.ShouldShowAlignmentHelper);
        Assert.Equal("Alignment helper shown at the center of the Elite window.", Assert.Single(messages));

        await viewModel.ApplyUpdateAsync([command!], null, null, allowCommands: true);

        Assert.False(viewModel.ShouldShowAlignmentHelper);
        Assert.Equal("Alignment helper hidden.", messages[^1]);
    }

    [Fact]
    public async Task AlignmentCommandIsIgnoredDuringJournalBootstrap()
    {
        using var directory = new TemporaryDirectory();
        var messages = new List<string>();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            messages.Add
        );
        Assert.True(
            JournalEventEnvelope.TryParse("""{"event":"SendText","Message":".alignment"}""", out var command, out _)
        );

        await viewModel.ApplyUpdateAsync([command!], null, null, allowCommands: false);

        Assert.False(viewModel.ShouldShowAlignmentHelper);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task LiveCommandPublishesNotificationAndMakesGroundMapVisible()
    {
        using var directory = new TemporaryDirectory();
        var messages = new List<string>();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            messages.Add
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var command,
                out _
            )
        );

        await viewModel.ApplyUpdateAsync(
            [command!],
            Context(new SurfaceCoordinate(1, 2)),
            new EliteStatus { Flags = StatusFlags.InSrv | StatusFlags.HasLatLong, PlanetRadius = 855_573.1875m },
            allowCommands: true
        );

        Assert.True(viewModel.HasActiveSurvey);
        Assert.True(viewModel.ShouldShowOverlay);
        Assert.Contains(messages, message => message.Contains("center saved", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Mining Location Signal 4", viewModel.LiveMapTitle);
    }

    [Fact]
    public async Task LivePositionAndHeadingRefreshForBothMapPresentations()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var command,
                out _
            )
        );
        var initial = new SurfaceCoordinate(1, 2);
        var moved = new SurfaceCoordinate(1.001, 2.002);

        await viewModel.ApplyUpdateAsync(
            [command!],
            Context(initial),
            new EliteStatus
            {
                Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
                Heading = 25,
                PlanetRadius = 855_573.1875m,
            },
            allowCommands: true
        );
        await viewModel.ApplyUpdateAsync(
            [],
            Context(moved),
            new EliteStatus
            {
                Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
                Heading = 137,
                PlanetRadius = 855_573.1875m,
            },
            allowCommands: true
        );

        Assert.Equal(moved, viewModel.PlayerLocation);
        Assert.Equal(137, viewModel.PlayerHeading);
        Assert.True(viewModel.ShouldShowOverlay);
    }

    [Fact]
    public async Task SavedMapLoadsOnEntryAndUnloadsOnExit()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var command,
                out _
            )
        );
        var border = Context(new SurfaceCoordinate(1, 2));
        var currentStatus = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            PlanetRadius = 855_573.1875m,
        };
        await viewModel.ApplyUpdateAsync([command!], border, currentStatus, allowCommands: true);
        var survey = viewModel.ActiveSurvey!;
        var outside = MineMapService.GetDestination(
            survey.Center,
            270,
            survey.LocationRadiusMeters + 100,
            survey.PlanetRadiusMeters
        );

        await viewModel.ApplyUpdateAsync(
            [],
            border with
            {
                PlayerLocation = outside,
            },
            currentStatus,
            allowCommands: true
        );
        Assert.False(viewModel.HasActiveSurvey);
        Assert.False(viewModel.ShouldShowOverlay);

        await viewModel.ApplyUpdateAsync(
            [],
            border with
            {
                PlayerLocation = survey.Center,
            },
            currentStatus,
            allowCommands: true
        );
        Assert.Equal(survey.Id, viewModel.ActiveSurvey?.Id);
        Assert.True(viewModel.ShouldShowOverlay);
    }

    [Fact]
    public async Task SurfaceMapsUseSharedBookmarksAndBookmarkEditsRefreshTheMapCatalog()
    {
        using var directory = new TemporaryDirectory();
        var bookmarks = new BookmarksViewModel(directory.Path);
        Guid? editRequest = null;
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { },
            editBookmark: id => editRequest = id,
            bookmarkCatalog: bookmarks.Catalog
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var command,
                out _
            )
        );

        await viewModel.ApplyUpdateAsync(
            [command!],
            Context(new SurfaceCoordinate(1, 2)),
            new EliteStatus { Flags = StatusFlags.InSrv | StatusFlags.HasLatLong },
            allowCommands: true
        );

        var shared = Assert.Single(bookmarks.Items, item => item.System == "Wille");
        Assert.True(shared.IsSurfaceMiningMap);
        Assert.Equal("Surface Mining", shared.Category);
        Assert.Equal(shared.System, shared.SurfaceMiningMap!.SystemName);
        Assert.Equal(shared.Body, shared.SurfaceMiningMap.BodyName);
        Assert.Equal(shared.SurfaceMiningMap.Notes, shared.Notes);
        Assert.Equal($"Signal {shared.SurfaceMiningMap.LocationSignal}", shared.DisplayDetails);
        bookmarks.SelectBookmark(shared.Id);
        bookmarks.Notes = "Return with a Rhino";
        bookmarks.SurfaceSignal = 6;
        bookmarks.SaveCommand.Execute(null);

        var edited = Assert.Single(viewModel.FilteredSurveys, row => row.Id == shared.Id);
        Assert.Equal("6", edited.SignalNumber);
        Assert.Equal("Return with a Rhino", edited.Notes);
        Assert.NotEqual("—", edited.DistanceText);
        viewModel.SelectedSurveyRow = edited;
        Assert.Equal(0, viewModel.SelectedTab);
        edited.ToggleExpandedCommand.Execute(null);
        Assert.True(edited.IsExpanded);
        Assert.Equal(0, viewModel.SelectedTab);
        viewModel.SelectSurvey(edited);
        Assert.Equal(1, viewModel.SelectedTab);
        Assert.Equal(edited.Id, viewModel.ActiveSurvey?.Id);
        viewModel.EditSelectedBookmarkCommand.Execute(null);
        Assert.Equal(shared.Id, editRequest);

        bookmarks.DeleteCommand.Execute(null);
        Assert.DoesNotContain(viewModel.FilteredSurveys, row => row.Id == shared.Id);
    }

    [Fact]
    public void MineMapExceptionsContainOnlySupportedShipsAndGroundModes()
    {
        var entries = OverlayVehicleCatalog.ForCategory(OverlaySettingsCategory.MineMap);

        Assert.Contains(entries, entry => entry.Id == "mev_rhino");
        Assert.Contains(entries, entry => entry.Id == "on-foot");
        Assert.Contains(entries, entry => entry.Name == "Python");
        Assert.Contains(entries, entry => entry.Name == "Anaconda");
        Assert.Contains(entries, entry => entry.Id == "unknown");
        Assert.DoesNotContain(entries, entry => entry.Id == "testbuggy");
        Assert.DoesNotContain(entries, entry => entry.Id == "lander01");
        Assert.DoesNotContain(entries, entry => entry.Group == "Small");
    }

    [Fact]
    public void RemovingLastMatchingSurveyNotifiesFilterFallbacks()
    {
        using var directory = new TemporaryDirectory();
        SeedSurvey(directory.Path);
        var catalog = new BookmarkCatalog(directory.Path);
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { },
            bookmarkCatalog: catalog
        );
        var survey = Assert.Single(viewModel.FilteredSurveys);
        viewModel.SelectedContains = "Ruby";
        viewModel.SelectedBodyType = "Rocky body";
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        catalog.Delete(survey.Id);

        Assert.Equal("All", viewModel.SelectedContains);
        Assert.Equal("All", viewModel.SelectedBodyType);
        Assert.Contains(nameof(viewModel.SelectedContains), changed);
        Assert.Contains(nameof(viewModel.SelectedBodyType), changed);
    }

    [Fact]
    public async Task GroundOnlySettingHidesMapForAirborneShip()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var command,
                out _
            )
        );
        viewModel.OnlyShowWhileOnGround = true;

        await viewModel.ApplyUpdateAsync(
            [command!],
            Context(new SurfaceCoordinate(1, 2)),
            new EliteStatus { Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong, PlanetRadius = 855_573.1875m },
            allowCommands: true
        );

        Assert.False(viewModel.ShouldShowOverlay);
    }

    [Fact]
    public async Task InspectingSavedMapFromAnotherBodyDoesNotShowLivePlayerOrOverlay()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { }
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var firstCommand,
                out _
            )
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 80 3.2 2"}""",
                out var secondCommand,
                out _
            )
        );
        var currentStatus = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            PlanetRadius = 855_573.1875m,
        };

        await viewModel.ApplyUpdateAsync(
            [firstCommand!],
            Context(new SurfaceCoordinate(1, 2)),
            currentStatus,
            allowCommands: true
        );
        await viewModel.ApplyUpdateAsync(
            [secondCommand!],
            Context(new SurfaceCoordinate(3, 4), systemAddress: 987654321, bodyId: 8, bodyName: "Wille 4"),
            currentStatus,
            allowCommands: true
        );

        var firstSurvey = Assert.Single(
            viewModel.FilteredSurveys,
            row => row.SignalNumber == "4" && row.SystemName == "Wille"
        );
        viewModel.SelectSurvey(firstSurvey);

        Assert.Null(viewModel.PlayerLocation);
        Assert.False(viewModel.ShouldShowOverlay);
    }

    [Fact]
    public async Task DeclinedMineCommandPublishesItsReasonToStatusNotifications()
    {
        using var directory = new TemporaryDirectory();
        var messages = new List<string>();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            messages.Add
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 120 6.44 4"}""",
                out var createCommand,
                out _
            )
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mine 15 unobtainium 1.2 high/low"}""",
                out var invalidCommand,
                out _
            )
        );
        var context = Context(new SurfaceCoordinate(1, 2));
        var status = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            PlanetRadius = 855_573.1875m,
        };

        await viewModel.ApplyUpdateAsync([createCommand!, invalidCommand!], context, status, allowCommands: true);

        Assert.Contains(messages, message => message.Contains("Hotspot List", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Hotspot List", viewModel.StatusText);
    }

    [Fact]
    public async Task MoveMarkerCommandPublishesSuccessAndFailureToStatusNotifications()
    {
        using var directory = new TemporaryDirectory();
        var messages = new List<string>();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            messages.Add
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mining 180 3.25 7"}""",
                out var createCommand,
                out _
            )
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mine 180 haematite 1 high/low"}""",
                out var addCommand,
                out _
            )
        );
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"SendText","Message":".mine move haematite here"}""",
                out var moveCommand,
                out _
            )
        );
        var border = Context(new SurfaceCoordinate(1, 2));
        var status = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            PlanetRadius = 855_573.1875m,
        };
        await viewModel.ApplyUpdateAsync([createCommand!, addCommand!], border, status, allowCommands: true);
        var survey = Assert.IsType<MineMapSurvey>(viewModel.ActiveSurvey);
        var marker = Assert.Single(survey.Markers);
        var nearby = MineMapService.GetDestination(marker.Location, 90, 50, survey.PlanetRadiusMeters);
        messages.Clear();

        await viewModel.ApplyUpdateAsync(
            [moveCommand!],
            border with
            {
                PlayerLocation = nearby,
            },
            status,
            allowCommands: true
        );

        Assert.Contains("Moved the nearest Haematite marker", Assert.Single(messages));
        Assert.Equal(messages[0], viewModel.StatusText);
        messages.Clear();

        await viewModel.ApplyUpdateAsync(
            [moveCommand!],
            border with
            {
                PlayerLocation = viewModel.ActiveSurvey!.Center,
            },
            status,
            allowCommands: true
        );

        Assert.Contains("within 200 m", Assert.Single(messages));
        Assert.Equal(messages[0], viewModel.StatusText);
    }

    private static MineMapCommandContext Context(
        SurfaceCoordinate location,
        long systemAddress = 123456789,
        int bodyId = 5,
        string bodyName = "Wille 2 C"
    ) =>
        new(
            "F123",
            "Fenris",
            "Wille",
            systemAddress,
            new GalacticCoordinate(1, 2, 3),
            bodyId,
            bodyName,
            "Rocky Ice body",
            129.5,
            855_573.1875,
            location
        );

    private static void SeedSurvey(string directory)
    {
        var center = new SurfaceCoordinate(1.320611, 179.850861);
        var now = DateTimeOffset.UtcNow;
        var survey = new MineMapSurvey
        {
            Id = Guid.NewGuid(),
            FrontierId = "F123",
            CommanderName = "Fenris",
            SystemName = "LTT 4428",
            SystemAddress = 2_656_194_005_355,
            SystemPosition = new GalacticCoordinate(75.15625, 15.1875, 33.34375),
            BodyId = 30,
            BodyName = "LTT 4428 E 5 a",
            BodyType = "Rocky body",
            ArrivalDistanceLs = 20_215.818079,
            LocationSignal = 4,
            LocationRadiusMeters = 6_440,
            PlanetRadiusMeters = 855_573.1875,
            Center = center,
            CreatedAt = now,
            UpdatedAt = now,
            Markers =
            [
                new MineMapMarker
                {
                    Material = "Ruby",
                    MineralAmount = MineMapRating.High,
                    Density = MineMapRating.Low,
                    Location = MineMapService.GetDestination(center, 15, 1240, 855_573.1875),
                    CreatedAt = now,
                },
            ],
        };
        new BookmarkCatalog(directory).Save(
            new GalacticBookmark
            {
                Id = survey.Id,
                System = survey.SystemName,
                Body = survey.BodyName,
                Position = survey.SystemPosition,
                CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
                SurfaceMiningMap = survey,
                Updated = survey.UpdatedAt,
            }
        );
    }

    private static void SeedSurveyWithRatingVariants(string directory)
    {
        SeedSurvey(directory);
        var catalog = new BookmarkCatalog(directory);
        var bookmark = Assert.Single(catalog.Items);
        var survey = Assert.IsType<MineMapSurvey>(bookmark.SurfaceMiningMap);
        var firstMarker = Assert.Single(survey.Markers);
        catalog.Save(
            bookmark with
            {
                SurfaceMiningMap = survey with
                {
                    Markers =
                    [
                        firstMarker,
                        firstMarker with
                        {
                            Id = Guid.NewGuid(),
                            MineralAmount = MineMapRating.Medium,
                            Density = MineMapRating.High,
                            Location = MineMapService.GetDestination(survey.Center, 45, 900, survey.PlanetRadiusMeters),
                        },
                        firstMarker with
                        {
                            Id = Guid.NewGuid(),
                            Material = "Gold",
                            MineralAmount = MineMapRating.Low,
                            Density = MineMapRating.Medium,
                            Location = MineMapService.GetDestination(survey.Center, 90, 700, survey.PlanetRadiusMeters),
                        },
                    ],
                },
            }
        );
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SrvSurvey-MineMap-Desktop-" + Guid.NewGuid().ToString("N")
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
