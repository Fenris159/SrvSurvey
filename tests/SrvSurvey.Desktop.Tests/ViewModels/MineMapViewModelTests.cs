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
    public async Task LiveCommandPublishesNotificationAndMakesGroundMapVisible()
    {
        using var directory = new TemporaryDirectory();
        var messages = new List<string>();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            messages.Add);
        Assert.True(JournalEventEnvelope.TryParse(
            """{"event":"SendText","Message":".mining 120 4 high/low"}""",
            out var command,
            out _));

        await viewModel.ApplyUpdateAsync(
            [command!],
            Context(new SurfaceCoordinate(1, 2)),
            new EliteStatus
            {
                Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
                PlanetRadius = 855_573.1875m,
            },
            allowCommands: true);

        Assert.True(viewModel.HasActiveSurvey);
        Assert.True(viewModel.ShouldShowOverlay);
        Assert.Contains(messages, message => message.Contains("center saved", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Mining Location Signal 4", viewModel.LiveMapTitle);
    }

    [Fact]
    public void MineMapExceptionsContainOnlySupportedShipsAndGroundModes()
    {
        var entries = OverlayVehicleCatalog.ForCategory(OverlaySettingsCategory.MineMap);

        Assert.Contains(entries, entry => entry.Id == "mev_rhino");
        Assert.Contains(entries, entry => entry.Id == "on-foot");
        Assert.Contains(entries, entry => entry.Name == "Python");
        Assert.Contains(entries, entry => entry.Name == "Anaconda");
        Assert.DoesNotContain(entries, entry => entry.Id == "testbuggy");
        Assert.DoesNotContain(entries, entry => entry.Id == "lander01");
        Assert.DoesNotContain(entries, entry => entry.Group == "Small");
    }

    [Fact]
    public async Task GroundOnlySettingHidesMapForAirborneShip()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { });
        Assert.True(JournalEventEnvelope.TryParse(
            """{"event":"SendText","Message":".mining 120 4 high/low"}""",
            out var command,
            out _));
        viewModel.OnlyShowWhileOnGround = true;

        await viewModel.ApplyUpdateAsync(
            [command!],
            Context(new SurfaceCoordinate(1, 2)),
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.HasLatLong,
                PlanetRadius = 855_573.1875m,
            },
            allowCommands: true);

        Assert.False(viewModel.ShouldShowOverlay);
    }

    [Fact]
    public async Task InspectingSavedMapFromAnotherBodyDoesNotShowLivePlayerOrOverlay()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MineMapViewModel(
            directory.Path,
            new MineMapSettingsStore(Path.Combine(directory.Path, "ui-settings.json")),
            _ => { });
        Assert.True(JournalEventEnvelope.TryParse(
            """{"event":"SendText","Message":".mining 120 4 high/low"}""",
            out var firstCommand,
            out _));
        Assert.True(JournalEventEnvelope.TryParse(
            """{"event":"SendText","Message":".mining 80 2 low/high"}""",
            out var secondCommand,
            out _));
        var currentStatus = new EliteStatus
        {
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            PlanetRadius = 855_573.1875m,
        };

        await viewModel.ApplyUpdateAsync(
            [firstCommand!],
            Context(new SurfaceCoordinate(1, 2)),
            currentStatus,
            allowCommands: true);
        await viewModel.ApplyUpdateAsync(
            [secondCommand!],
            Context(new SurfaceCoordinate(3, 4), systemAddress: 987654321, bodyId: 8, bodyName: "Wille 4"),
            currentStatus,
            allowCommands: true);

        var firstSurvey = Assert.Single(
            viewModel.FilteredSurveys,
            row => row.Name == "Mining Location Signal 4");
        viewModel.SelectSurvey(firstSurvey);

        Assert.Null(viewModel.PlayerLocation);
        Assert.False(viewModel.ShouldShowOverlay);
    }

    private static MineMapCommandContext Context(
        SurfaceCoordinate location,
        long systemAddress = 123456789,
        int bodyId = 5,
        string bodyName = "Wille 2 C") => new(
        "F123", "Fenris", "Wille", systemAddress,
        new GalacticCoordinate(1, 2, 3), bodyId, bodyName, "Rocky Ice body",
        129.5, 855_573.1875, location);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "SrvSurvey-MineMap-Desktop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
