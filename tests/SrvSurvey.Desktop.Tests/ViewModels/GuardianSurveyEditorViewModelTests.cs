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
        editor.Load(
            "F123",
            isOdyssey: true,
            initial with { Path = path },
            CreateTemplate());

        Assert.True(editor.IsAvailable);
        Assert.Equal(3, editor.Points.Count);
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
        editor.Load(
            "F123",
            isOdyssey: true,
            initial with { Path = path },
            CreateTemplate());
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
        var editor = new GuardianSurveyEditorViewModel(
            new GuardianCommanderSurveyStore(temporaryDirectory),
            (_, _) =>
            {
                callbackCount++;
                return Task.CompletedTask;
            });

        editor.Load("F123", isOdyssey: true, survey: null, CreateTemplate());
        await editor.SaveAsync();

        Assert.False(editor.IsAvailable);
        Assert.Empty(editor.Points);
        Assert.Equal(0, callbackCount);
        Assert.Contains("Visit the selected site", editor.StatusMessage);
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
            },
            [new GuardianObelisk("A01", "H1", true, ["ca"])],
            new HashSet<char> { 'A' });
    }

    private static GuardianSiteTemplate CreateTemplate()
    {
        return new GuardianSiteTemplate(
            "Beta",
            "Beta",
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
            [],
            new Dictionary<string, GuardianMapPoint>
            {
                ["A"] = new GuardianMapPoint(0, 20),
                ["B"] = new GuardianMapPoint(180, 20),
            });
    }
}
