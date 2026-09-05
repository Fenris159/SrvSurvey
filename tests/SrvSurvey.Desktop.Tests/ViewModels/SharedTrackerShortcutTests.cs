using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SharedTrackerShortcutTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"SrvSurvey-shared-trackers-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(1, false, false, true)]
    [InlineData(8, false, false, true)]
    [InlineData(1, true, false, true)]
    [InlineData(6, true, false, true)]
    [InlineData(7, true, false, false)]
    [InlineData(8, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(8, false, true, false)]
    public async Task ShortcutTogglesOnlyTheTrackersForTheCurrentActivity(
        int number, bool aboardRhino, bool parkedRhino, bool expectedHandled)
    {
        var paths = new AppDataPaths(Path.Combine(root, "config"),
            Path.Combine(root, "data"), Path.Combine(root, "cache"), []);
        using var viewModel = MainWindowViewModelTestBuilder.Create(
            Path.Combine(root, "journals"), builder => builder.WithAppDataPaths(paths));
        var status = new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | (parkedRhino ? StatusFlags.None : StatusFlags.InSrv),
            Flags2 = parkedRhino ? StatusFlags2.OnFoot | StatusFlags2.OnFootOnPlanet : StatusFlags2.None,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1000,
            BodyName = "Test System 1",
        };
        var srvType = aboardRhino ? EliteSrvTypes.Rhino : "testbuggy";
        var parkedType = parkedRhino ? EliteSrvTypes.Rhino : null;
        viewModel.SystemSurvey.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test System","SystemAddress":42}"""),
                Parse("""{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test System 1","BodyID":7,"PlanetClass":"Rocky body","Landable":true,"Radius":1000}"""),
            ],
            status, nextActiveSrvType: srvType, nextParkedSrvType: parkedType);
        var session = new SurfaceSurveySessionContext("F123", "Test Cmdr",
            "Test System", 42, null, BodyId: 7, BodyName: "Test System 1", BodyRadiusMeters: 1000);
        await viewModel.SurfaceSurvey.ApplyUpdateAsync(session, [], status, ExobiologySnapshot.Empty);
        await viewModel.Mining.ApplyUpdateAsync(session, viewModel.SystemSurvey.Snapshot,
            status, srvType, parkedSrvType: parkedType);

        Assert.Equal(expectedHandled, await viewModel.ToggleTrackerOrMiningRigAsync(number));
        Assert.Equal(aboardRhino && expectedHandled ? 1 : 0,
            viewModel.Mining.Rigs.Count(rig => rig.IsSet));
        Assert.Equal(!aboardRhino && !parkedRhino && expectedHandled ? 1 : 0,
            viewModel.SurfaceSurvey.QuickTrackerGroups.Count);
        if (expectedHandled)
        {
            Assert.True(await viewModel.ToggleTrackerOrMiningRigAsync(number));
            Assert.DoesNotContain(viewModel.Mining.Rigs, rig => rig.IsSet);
            Assert.Empty(viewModel.SurfaceSurvey.QuickTrackerGroups);
        }
    }

    [Fact]
    public async Task LiveChatClearsBothStoresButReplayedChatDoesNotClearSavedRigs()
    {
        var paths = new AppDataPaths(Path.Combine(root, "config"),
            Path.Combine(root, "data"), Path.Combine(root, "cache"), []);
        var journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        var journalPath = Path.Combine(journals, "Journal.2026-09-05T120000.01.log");
        await File.WriteAllTextAsync(journalPath,
            """
            {"timestamp":"2026-09-05T12:00:00Z","event":"Fileheader","gameversion":"4.1","Odyssey":true}
            {"timestamp":"2026-09-05T12:00:01Z","event":"LoadGame","Commander":"Test Cmdr","FID":"F123","Ship":"mev_rhino","ShipID":42,"Odyssey":true}
            {"timestamp":"2026-09-05T12:00:02Z","event":"Location","StarSystem":"Test System","SystemAddress":42,"Body":"Test System 1","BodyID":7,"BodyType":"Planet"}
            {"timestamp":"2026-09-05T12:00:03Z","event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test System 1","BodyID":7,"PlanetClass":"Rocky body","Landable":true,"Radius":1000}

            """);
        await File.WriteAllTextAsync(Path.Combine(journals, "Status.json"),
            $$"""
            {"timestamp":"2026-09-05T12:00:04Z","event":"Status","Flags":{{(long)(StatusFlags.InSrv | StatusFlags.HasLatLong)}},"Latitude":0,"Longitude":0,"PlanetRadius":1000,"BodyName":"Test System 1"}
            """);
        using (var viewModel = MainWindowViewModelTestBuilder.Create(journals,
            builder => builder.WithAppDataPaths(paths)))
        {
            await viewModel.RefreshAsync();
            viewModel.Mining.AutoClearRigsOnShipBoarding = false;
            Assert.True(await viewModel.Mining.ToggleRigAsync(1));
            Assert.True(await viewModel.SurfaceSurvey.ToggleQuickTrackerAsync(8));
            await File.AppendAllTextAsync(journalPath,
                "{\"timestamp\":\"2026-09-05T12:00:05Z\",\"event\":\"SendText\",\"Message\":\"+helium\"}\n");
            await viewModel.RefreshAsync();
            Assert.Single(viewModel.Mining.Resources);
            await File.AppendAllTextAsync(journalPath,
                "{\"timestamp\":\"2026-09-05T12:00:06Z\",\"event\":\"SendText\",\"Message\":\"---\"}\n");
            await viewModel.RefreshAsync();
            Assert.Empty(viewModel.SurfaceSurvey.TrackerGroups);
            Assert.Empty(viewModel.Mining.Resources);
            Assert.DoesNotContain(viewModel.Mining.Rigs, rig => rig.IsSet);
            Assert.True(await viewModel.Mining.ToggleRigAsync(2));
        }

        using var reopened = MainWindowViewModelTestBuilder.Create(journals,
            builder => builder.WithAppDataPaths(paths));
        await reopened.RefreshAsync();
        Assert.False(reopened.Mining.AutoClearRigsOnShipBoarding);
        Assert.True(reopened.Mining.Rigs[1].IsSet);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out var error), error);
        return Assert.IsType<JournalEventEnvelope>(entry);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
