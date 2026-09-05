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
