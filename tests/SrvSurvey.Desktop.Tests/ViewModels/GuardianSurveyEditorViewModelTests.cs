using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianSurveyEditorViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-guardian-editor-tests-{Guid.NewGuid():N}");

    public GuardianSurveyEditorViewModelTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public async Task SavesHeadingsNotesPoiStatesRelicsAndObeliskGroups()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        GuardianCommanderSiteSurvey? callbackPrevious = null;
        GuardianCommanderSiteSurvey? callbackSaved = null;
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (previous, saved) =>
            {
                callbackPrevious = previous;
                callbackSaved = saved;
                return Task.CompletedTask;
            });
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate()));

        Assert.True(editor.IsAvailable);
        Assert.Equal(3, editor.Points.Count);
        Assert.False(editor.Points.Single(point => point.Name == "c1")
            .CanEditComponentMaterials);
        Assert.Equal(2, editor.ObeliskGroups.Count);
        editor.SiteHeading = 123;
        editor.RelicTowerHeading = 45;
        editor.Notes = "updated note";
        editor.Points.Single(point => point.Name == "p1").Status =
            GuardianPoiStatus.Empty;
        editor.Points.Single(point => point.Name == "c1").Status =
            GuardianPoiStatus.Absent;
        var relic = editor.Points.Single(point => point.Name == "t1");
        relic.Status = GuardianPoiStatus.Present;
        relic.RelicHeading = 222;
        editor.ObeliskGroups.Single(group => group.Name == 'A').IsSelected = false;
        editor.ObeliskGroups.Single(group => group.Name == 'B').IsSelected = true;

        await editor.SaveAsync();

        Assert.NotNull(callbackPrevious);
        Assert.NotNull(callbackSaved);
        Assert.Contains("Saved Guardian survey", editor.StatusMessage);
        var data = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync("F123", isOdyssey: true);
        var saved = Assert.Single(data.Surveys);
        Assert.Equal(123, saved.Survey.SiteHeading);
        Assert.Equal(45, saved.Survey.RelicTowerHeading);
        Assert.Equal("updated note", saved.Notes);
        Assert.Equal(GuardianPoiStatus.Empty, saved.Survey.PoiStatuses["p1"]);
        Assert.Equal(GuardianPoiStatus.Absent, saved.Survey.PoiStatuses["c1"]);
        Assert.Equal(GuardianPoiStatus.Present, saved.Survey.PoiStatuses["t1"]);
        Assert.Equal(222, saved.Survey.RelicHeadings["t1"]);
        Assert.DoesNotContain('A', saved.ObeliskGroups);
        Assert.Contains('B', saved.ObeliskGroups);
        Assert.Single(saved.ActiveObelisks);
        Assert.True(saved.ActiveObelisks[0].Scanned);
    }

    [Fact]
    public async Task RejectsInvalidStatusWithoutChangingPersistedSurvey()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var callbackCount = 0;
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) =>
            {
                callbackCount++;
                return Task.CompletedTask;
            });
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate()));
        editor.Points.Single(point => point.Name == "c1").Status =
            GuardianPoiStatus.Empty;

        await editor.SaveAsync();

        Assert.Equal(0, callbackCount);
        Assert.Contains("cannot be marked empty", editor.StatusMessage);
        var data = await new GuardianCommanderDataReader(temporaryDirectory)
            .ReadAsync("F123", isOdyssey: true);
        var saved = Assert.Single(data.Surveys);
        Assert.Equal(GuardianPoiStatus.Present, saved.Survey.PoiStatuses["c1"]);
    }

    [Fact]
    public async Task ReferenceOnlySelectionRemainsReadOnly()
    {
        var callbackCount = 0;
        var template = CreateTemplate();
        var projection = new GuardianSiteMapProjector().Project(
            template,
            CreateSurvey().Survey,
            [new GuardianObelisk("A01", "H1", true, ["ca"])]);
        var editor = new GuardianSurveyEditorViewModel(
            new GuardianCommanderSurveyStore(temporaryDirectory),
            (_, _) =>
            {
                callbackCount++;
                return Task.CompletedTask;
            });

        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            null,
            template)
        {
            ReferenceProjection = projection,
        });
        editor.SelectedPointName = "c1";

        Assert.True(editor.HasSelectedMapMarker);
        Assert.False(editor.IsMapSummaryVisible);
        Assert.True(editor.HasSelectedPoint);
        Assert.True(editor.IsSelectedPointReadOnly);
        Assert.False(editor.CanEditSelectedPoint);
        Assert.Equal(GuardianPoiStatus.Present, editor.SelectedPoint!.Status);
        Assert.True(editor.SelectedPoint.IsReferenceOnly);
        Assert.True(editor.SelectedPoint.HasComponentRecord);

        editor.SelectedPointName = null;

        Assert.False(editor.HasSelectedMapMarker);
        Assert.True(editor.IsMapSummaryVisible);
        await editor.SaveAsync();

        Assert.False(editor.IsAvailable);
        Assert.Empty(editor.Points);
        Assert.Equal(0, callbackCount);
        Assert.Contains("Visit the selected site", editor.StatusMessage);
    }

    [Fact]
    public void ReferenceSelectionBecomesEditableWhenSurveyAppears()
    {
        var template = CreateTemplate();
        var survey = CreateSurvey();
        var projection = new GuardianSiteMapProjector().Project(
            template,
            survey.Survey,
            survey.ActiveObelisks);
        var editor = new GuardianSurveyEditorViewModel(
            new GuardianCommanderSurveyStore(temporaryDirectory),
            (_, _) => Task.CompletedTask);
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            null,
            template)
        {
            ReferenceProjection = projection,
        });
        editor.SelectedPointName = "c1";

        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            survey,
            template)
        {
            ReferenceProjection = projection,
        });

        Assert.Equal("c1", editor.SelectedPointName);
        Assert.True(editor.HasSelectedMapMarker);
        Assert.True(editor.CanEditSelectedPoint);
        Assert.False(editor.IsSelectedPointReadOnly);
        Assert.False(editor.SelectedPoint!.IsReferenceOnly);
    }

    [Fact]
    public async Task EditsLegacyComponentTowersAndDestructiblePanels()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) => Task.CompletedTask);
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate())
        {
            ShowComponentMaterials = true,
        });

        var tower = editor.Points.Single(point => point.Name == "c1");
        Assert.True(tower.CanEditComponentMaterials);
        Assert.True(tower.SupportsMultipleComponentMaterials);
        Assert.Equal(
            GuardianComponentMaterial.Cell,
            tower.TopComponentMaterial);
        tower.MiddleComponentMaterial = GuardianComponentMaterial.Conduit;
        var panel = editor.Points.Single(point => point.Name == "d1");
        Assert.True(panel.SupportsComponentMaterials);
        Assert.False(panel.SupportsMultipleComponentMaterials);
        panel.TopComponentMaterial = GuardianComponentMaterial.Tech;

        await editor.SaveAsync();

        var saved = Assert.Single(
            (await new GuardianCommanderDataReader(temporaryDirectory)
                .ReadAsync("F123", isOdyssey: true)).Surveys);
        Assert.Equal(
            GuardianComponentMaterial.Conduit,
            saved.Survey.ComponentMaterials["c1"].GetItem(1));
        Assert.Equal(
            GuardianComponentMaterial.Tech,
            saved.Survey.ComponentMaterials["d1"].GetItem(0));
    }

    [Fact]
    public async Task AddsAndRemovesMeasuredRawPointsWithoutRedundantStatusData()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) => Task.CompletedTask);
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate()));
        editor.NewRawPointType = GuardianPoiType.Orb;
        editor.UpdateLiveMeasurement(new GuardianSurveyMeasurement(
            123.4,
            45.6,
            78));

        await editor.AddRawPointAsync();

        var raw = Assert.IsType<GuardianSurveyPoiViewModel>(editor.SelectedPoint);
        Assert.True(raw.IsRaw);
        Assert.Equal("x1", raw.Name);
        Assert.Equal(GuardianPoiStatus.Present, raw.Status);
        Assert.Contains("123.4 m", raw.PositionText);

        await editor.AddRawPointAsync();

        Assert.Single(editor.Points, point => point.IsRaw);
        Assert.Contains("too close", editor.StatusMessage);

        editor.SelectedPoint = null;
        editor.SelectedPointName = "x1";
        Assert.Same(raw, editor.SelectedPoint);
        raw.Type = GuardianPoiType.Tablet;
        raw.RawDistance = 45.678m;
        raw.RawAngle = 123.456m;
        raw.RawRotation = 210.5m;

        await editor.SaveAsync();

        var saved = Assert.Single(
            (await new GuardianCommanderDataReader(temporaryDirectory)
                .ReadAsync("F123", isOdyssey: true)).Surveys);
        var savedRaw = Assert.Single(saved.Survey.RawPointsOfInterest!);
        Assert.Equal("x1", savedRaw.Name);
        Assert.Equal(GuardianPoiType.Tablet, savedRaw.Type);
        Assert.Equal(45.678, savedRaw.Distance, 3);
        Assert.Equal(123.456, savedRaw.Angle, 3);
        Assert.Equal(210.5, savedRaw.Rotation, 3);
        Assert.DoesNotContain("x1", saved.Survey.PoiStatuses.Keys);

        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            saved,
            CreateTemplate()));
        editor.SelectedPoint = editor.Points.Single(point => point.IsRaw);
        await editor.RemoveSelectedRawPointAsync();
        await editor.SaveAsync();

        var afterRemoval = Assert.Single(
            (await new GuardianCommanderDataReader(temporaryDirectory)
                .ReadAsync("F123", isOdyssey: true)).Surveys);
        Assert.Null(afterRemoval.Survey.RawPointsOfInterest);
    }

    [Fact]
    public async Task RepairsSiteIdentityOriginAndActiveObeliskMetadata()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var beta = CreateTemplate();
        var gamma = CreateTemplate("Gamma");
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) => Task.CompletedTask);
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            beta)
        {
            TemplateCatalog = new GuardianSiteTemplateCatalog([beta, gamma]),
        });

        editor.SiteType = "Gamma";
        editor.SurfaceLatitude = -12.345678m;
        editor.SurfaceLongitude = 98.765432m;
        editor.SelectedPointName = "A01";
        Assert.Null(editor.SelectedPoint);
        Assert.Equal("A01", editor.SelectedActiveObelisk?.Name);
        var selectionNotifications = new List<string?>();
        editor.PropertyChanged += (_, args) =>
            selectionNotifications.Add(args.PropertyName);
        await editor.AddActiveObeliskAsync();
        var added = Assert.IsType<GuardianActiveObeliskViewModel>(
            editor.SelectedActiveObelisk);
        Assert.Contains(nameof(editor.HasSelectedMapMarker), selectionNotifications);
        Assert.Contains(nameof(editor.IsMapSummaryVisible), selectionNotifications);
        Assert.Contains(nameof(editor.CanEditSelectedPoint), selectionNotifications);
        Assert.Contains(nameof(editor.IsSelectedPointReadOnly), selectionNotifications);
        added.Name = "B03";
        added.LogCode = "H12";
        added.ArtifactCodes = "ca, or";
        added.Scanned = true;

        await editor.SaveAsync();

        var saved = Assert.Single(
            (await new GuardianCommanderDataReader(temporaryDirectory)
                .ReadAsync("F123", isOdyssey: true)).Surveys);
        Assert.Equal("Gamma", saved.SiteType);
        Assert.Equal("Gamma", saved.Survey.SiteType);
        Assert.Equal(-12.345678, saved.Survey.Location!.Value.Latitude, 6);
        Assert.Equal(98.765432, saved.Survey.Location.Value.Longitude, 6);
        var obelisk = Assert.Single(
            saved.ActiveObelisks,
            item => item.Name == "B03");
        Assert.Equal("H12", obelisk.LogCode);
        Assert.Equal(["ca", "or"], obelisk.ItemCodes);
        Assert.True(obelisk.Scanned);
    }

    [Fact]
    public async Task ResetCoordinatesRestoresLastSavedSurfaceOrigin()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) => Task.CompletedTask);
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate()));

        editor.SurfaceLatitude = 12.345678m;
        editor.SurfaceLongitude = -98.765432m;

        Assert.True(editor.ResetCoordinatesCommand.CanExecute(null));
        editor.ResetCoordinatesCommand.Execute(null);

        Assert.Equal(1m, editor.SurfaceLatitude);
        Assert.Equal(2m, editor.SurfaceLongitude);
        Assert.False(editor.ResetCoordinatesCommand.CanExecute(null));

        editor.SurfaceLatitude = 3m;
        editor.SurfaceLongitude = 4m;
        await editor.SaveAsync();
        editor.SurfaceLatitude = 5m;
        editor.SurfaceLongitude = 6m;
        editor.ResetCoordinatesCommand.Execute(null);

        Assert.Equal(3m, editor.SurfaceLatitude);
        Assert.Equal(4m, editor.SurfaceLongitude);
    }

    [Fact]
    public async Task RejectsIncompleteOriginAndDuplicateActiveObeliskNames()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var callbackCount = 0;
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) =>
            {
                callbackCount++;
                return Task.CompletedTask;
            });
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate()));
        editor.SurfaceLatitude = 10;
        editor.SurfaceLongitude = null;

        await editor.SaveAsync();

        Assert.Contains("both latitude and longitude", editor.StatusMessage);
        editor.SurfaceLongitude = 20;
        await editor.AddActiveObeliskAsync();
        editor.SelectedActiveObelisk!.Name = "a01";

        await editor.SaveAsync();

        Assert.Equal(0, callbackCount);
        Assert.Contains("duplicated", editor.StatusMessage);
        var saved = Assert.Single(
            (await new GuardianCommanderDataReader(temporaryDirectory)
                .ReadAsync("F123", isOdyssey: true)).Surveys);
        Assert.Equal(1, saved.Survey.Location!.Value.Latitude);
        Assert.Single(saved.ActiveObelisks);
    }

    [Fact]
    public async Task PreservesExplicitLegacyRawPointOverrides()
    {
        var store = new GuardianCommanderSurveyStore(temporaryDirectory);
        var initial = CreateSurvey();
        var initialSurvey = initial.Survey;
        initial = initial with
        {
            Survey = new GuardianSurveyData
            {
                SiteType = initialSurvey.SiteType,
                SiteHeading = initialSurvey.SiteHeading,
                RelicTowerHeading = initialSurvey.RelicTowerHeading,
                Location = initialSurvey.Location,
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>(
                    initialSurvey.PoiStatuses)
                {
                    ["x7"] = GuardianPoiStatus.Absent,
                },
                RelicHeadings = new Dictionary<string, int>(
                    initialSurvey.RelicHeadings)
                {
                    ["x7"] = 55,
                },
                RawPointsOfInterest =
                [
                    new GuardianPointOfInterest(
                        "x7",
                        GuardianPoiType.Relic,
                        10,
                        20,
                        30),
                ],
            },
        };
        var path = await store.SaveAsync("F123", isOdyssey: true, initial);
        var editor = new GuardianSurveyEditorViewModel(
            store,
            (_, _) => Task.CompletedTask);
        editor.Load(new GuardianSurveyEditorLoadContext(
            "F123",
            true,
            initial with { Path = path },
            CreateTemplate()));
        editor.SelectedPoint = editor.Points.Single(point => point.IsRaw);
        editor.SelectedPoint.RelicHeading = 123;

        await editor.SaveAsync();

        var saved = Assert.Single(
            (await new GuardianCommanderDataReader(temporaryDirectory)
                .ReadAsync("F123", isOdyssey: true)).Surveys);
        Assert.Equal(GuardianPoiStatus.Absent, saved.Survey.PoiStatuses["x7"]);
        Assert.Equal(123, saved.Survey.RelicHeadings["x7"]);
        Assert.Equal(123, Assert.Single(saved.Survey.RawPointsOfInterest!).Rotation);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static GuardianCommanderSiteSurvey CreateSurvey()
    {
        return new GuardianCommanderSiteSurvey(
            string.Empty,
            "$Ancient:#index=1;",
            "Ancient Ruins (1)",
            "Drew",
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-02T10:00:00Z"),
            "Beta",
            1,
            42,
            "Test",
            7,
            "Test A 1",
            "original note",
            false,
            new GuardianSurveyData
            {
                SiteType = "Beta",
                SiteHeading = 10,
                RelicTowerHeading = 20,
                Location = new GuardianSurfaceLocation(1, 2),
                PoiStatuses = new Dictionary<string, GuardianPoiStatus>
                {
                    ["c1"] = GuardianPoiStatus.Present,
                },
                RelicHeadings = new Dictionary<string, int>(),
                ComponentMaterials = new Dictionary<
                    string,
                    GuardianComponentLoadout>
                {
                    ["c1"] = new GuardianComponentLoadout(
                        "c1",
                        [
                            GuardianComponentMaterial.Cell,
                            GuardianComponentMaterial.Unknown,
                            GuardianComponentMaterial.Tech,
                        ]),
                },
            },
            [new GuardianObelisk("A01", "H1", true, ["ca"])],
            new HashSet<char> { 'A' });
    }

    private static GuardianSiteTemplate CreateTemplate(string siteType = "Beta")
    {
        return new GuardianSiteTemplate(
            siteType,
            siteType,
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "p1",
                    GuardianPoiType.Orb,
                    0,
                    10,
                    0),
                new GuardianPointOfInterest(
                    "t1",
                    GuardianPoiType.Relic,
                    90,
                    20,
                    0),
                new GuardianPointOfInterest(
                    "c1",
                    GuardianPoiType.Component,
                    180,
                    30,
                    0),
                new GuardianPointOfInterest(
                    "A01",
                    GuardianPoiType.Obelisk,
                    270,
                    40,
                    0),
            ],
            [
                new GuardianPointOfInterest(
                    "d1",
                    GuardianPoiType.DestructiblePanel,
                    45,
                    35,
                    0),
            ],
            new Dictionary<string, GuardianMapPoint>
            {
                ["A"] = new GuardianMapPoint(0, 20),
                ["B"] = new GuardianMapPoint(180, 20),
            });
    }
}
