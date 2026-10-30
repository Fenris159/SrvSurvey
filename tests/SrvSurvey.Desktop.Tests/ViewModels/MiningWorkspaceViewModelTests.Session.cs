using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MiningWorkspaceViewModelTests
{
    [Fact]
    public void CodeRabbitOverlayPreferencesCannotBeEditedWithoutACommander()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory)
            );
            workspace.SessionYieldSortIndex = 1;
            workspace.SessionYieldLimit = 9;
            workspace.HideProspectsBelowThreshold = true;
            Assert.Equal(0, workspace.SessionYieldSortIndex);
            Assert.Equal(5, workspace.SessionYieldLimit);
            Assert.False(workspace.HideProspectsBelowThreshold);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>Applies sorting before truncation, updates live, and recovers each commander's overlay choices.</summary>
    [Fact]
    public void SessionOverviewSortAndLimitPersistWithoutTruncatingMiningData()
    {
        using var fixture = new MiningMissionFixture();
        MiningWorkspaceViewModel workspace = fixture.Workspace;
        workspace.StartCommand.Execute(null);
        using var overview = new MiningSessionOverlayViewModel(workspace);
        string[] minerals = ["Platinum", "Osmium", "Gold", "Silver", "Palladium", "Praseodymium", "Samarium"];
        int second = 0;
        foreach (string mineral in minerals.Concat(["Osmium", "Samarium", "Samarium"]))
        {
            int percentage = 70 - Array.IndexOf(minerals, mineral) * 10;
            fixture.Feed(
                $$"""{"timestamp":"{{fixture.Now.AddSeconds(++second):O}}","event":"ProspectedAsteroid","Materials":[{"Name":"{{mineral}}","Proportion":{{percentage}}}],"Remaining":100}"""
            );
        }

        Assert.Equal(minerals.Take(5), overview.ProspectingYields.Select(row => row.Name));
        var changed = new List<string?>();
        overview.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        workspace.SessionYieldLimit = 3;
        workspace.SessionYieldSortIndex = 1;
        Assert.Equal(["Samarium", "Osmium", "Platinum"], overview.ProspectingYields.Select(row => row.Name));
        Assert.Contains(nameof(MiningSessionOverlayViewModel.ProspectingYields), changed);
        Assert.Equal(7, workspace.Materials.Count);
        Assert.Equal(10, workspace.Current!.Prospects.Count);
        string backup = workspace.Backup();

        workspace.SessionYieldSortIndex = -1;
        workspace.SessionYieldSortIndex = 2;
        workspace.SessionYieldSortIndex = 1;
        Assert.Equal(1, workspace.SessionYieldSortIndex);
        workspace.SessionYieldLimit = 0;
        workspace.SessionYieldLimit = 0;
        Assert.Equal("Samarium", Assert.Single(overview.ProspectingYields).Name);
        workspace.SessionYieldLimit = 101;
        workspace.SessionYieldLimit = 101;
        Assert.Equal(100, workspace.SessionYieldLimit);
        Assert.Equal(7, overview.ProspectingYields.Count);
        Assert.True(workspace.Restore(backup));
        Assert.Equal(3, workspace.SessionYieldLimit);
        Assert.Equal(1, workspace.SessionYieldSortIndex);

        fixture.Feed("""{"event":"LoadGame","FID":"F2","Commander":"Other","Ship":"Python"}""");
        Assert.Equal(0, workspace.SessionYieldSortIndex);
        Assert.Equal(5, workspace.SessionYieldLimit);
        fixture.Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}""");
        Assert.Equal(1, workspace.SessionYieldSortIndex);
        Assert.Equal(3, workspace.SessionYieldLimit);
        Assert.Equal(["Samarium", "Osmium", "Platinum"], overview.ProspectingYields.Select(row => row.Name));
        Assert.Equal(7, workspace.Materials.Count);
        MiningPreferences recovered = new MiningStore(fixture.Directory).Load("F1").Settings;
        Assert.Equal(MiningSessionYieldSort.Finds, recovered.SessionYieldSort);
        Assert.Equal(3, recovered.SessionYieldLimit);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MonitoredCompletedJumpEndsTheSessionAcrossJournalLocations(bool configured, bool replayedFirst)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new Clock();
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: clock
            );
            string journalDirectory = Path.Combine(
                directory,
                configured ? "custom-journals" : "Saved Games/Frontier Developments/Elite Dangerous"
            );
            Directory.CreateDirectory(journalDirectory);
            string journal = Path.Combine(journalDirectory, "Journal.2026-09-06T120000.01.log");
            await File.WriteAllTextAsync(
                journal,
                """{"timestamp":"2026-09-06T12:00:00Z","event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}"""
                    + "\n"
            );
            IReadOnlyList<string> folders = [Path.Combine(directory, "other-provider"), journalDirectory];
            if (configured)
            {
                var settings = new JournalSettingsStore(Path.Combine(directory, "ui.json"));
                settings.Save(new(journalDirectory));
                folders = settings.Load().Directories;
            }
            var monitor = new JournalDirectoryMonitor(folders, "F1");
            var context = new JournalSessionState();
            var status = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Apply(JournalMonitorUpdate update)
            {
                foreach (JournalEventEnvelope entry in update.JournalEvents)
                {
                    context.Apply(entry);
                }
                workspace.Apply(update, context, null, status);
            }
            Apply(await monitor.PollAsync());
            workspace.StartCommand.Execute(null);
            Assert.NotNull(workspace.Current);
            clock.Now = clock.Now.AddMinutes(1);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-09-06T12:00:30Z","event":"SupercruiseEntry","StarSystem":"Sol"}""" + "\n"
            );
            JournalMonitorUpdate cruise = await monitor.PollAsync();
            if (replayedFirst)
            {
                Apply(cruise with { IsBootstrapRead = true });
                Assert.Null(workspace.Current.PausedAt);
            }
            Guid sessionId = workspace.Current.Id;
            using var overlay = new MiningSessionOverlayViewModel(workspace);
            Apply(cruise);
            Assert.NotNull(workspace.Current.PausedAt);
            Assert.Equal("Paused", overlay.State);
            Assert.True(workspace.ShouldShowSession);
            Assert.Empty(workspace.History);
            Assert.NotNull(new MiningStore(directory).Load("F1").Current!.PausedAt);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-09-06T12:00:45Z","event":"MiningRefined","Type":"platinum"}""" + "\n"
            );
            Apply(await monitor.PollAsync());
            Assert.Equal(sessionId, workspace.Current.Id);
            Assert.Null(workspace.Current.PausedAt);
            Assert.Equal("Active", overlay.State);
            Assert.Equal(TimeSpan.FromSeconds(15), workspace.Current.PausedDuration);
            Assert.Equal(1, workspace.Current.RefinedTons);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-09-06T12:01:00Z","event":"StartJump","JumpType":"Hyperspace","StarSystem":"Beta"}"""
                    + "\n"
            );
            Apply(await monitor.PollAsync());
            Assert.NotNull(workspace.Current);
            clock.Now = clock.Now.AddMinutes(1);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-09-06T12:02:00Z","event":"FSDJump","StarSystem":"Beta","SystemAddress":2,"StarPos":[1,2,3]}"""
                    + "\n"
            );
            JournalMonitorUpdate completed = await monitor.PollAsync();
            Assert.False(completed.IsBootstrapRead);
            Assert.Contains(completed.JournalEvents, entry => entry.EventName == "FSDJump");
            if (replayedFirst)
            {
                Apply(completed with { IsBootstrapRead = true });
                Assert.NotNull(workspace.Current);
            }
            Apply(completed);
            Assert.Null(workspace.Current);
            Assert.Equal(clock.Now, Assert.Single(workspace.History).Ended);
            Assert.Equal(TimeSpan.FromSeconds(105), workspace.History[0].ActiveDuration);
            Assert.Null(new MiningStore(directory).Load("F1").Current);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ConfirmedFsdArrivalEndsSessionButStartingAndCancellingAJumpDoesNot()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new Clock();
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: clock
            );
            var context = new JournalSessionState();
            var status = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json, bool bootstrap = false)
            {
                System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                node["timestamp"] = clock.Now.ToString("O");
                JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(node.ToJsonString());
                context.Apply(entry);
                workspace.Apply(new(null, [entry], status, null, null, null, [], bootstrap), context, null, status);
            }
            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}""", true);
            workspace.StartCommand.Execute(null);
            Assert.NotNull(workspace.Current);
            clock.Now = clock.Now.AddMinutes(1);
            status = new EliteStatus { Flags = StatusFlags.InMainShip | StatusFlags.FsdCharging };
            Feed("""{"event":"FSDTarget","Name":"Beta","SystemAddress":2,"StarClass":"K"}""");
            status = new EliteStatus { Flags = StatusFlags.InMainShip };
            Feed("""{"event":"FSDTarget","Name":"Gamma","SystemAddress":3,"StarClass":"N"}""");
            Assert.NotNull(workspace.Current);
            Feed("""{"event":"StartJump","JumpType":"Supercruise","StarSystem":"Sol"}""");
            Assert.Null(workspace.Current.PausedAt);
            clock.Now = clock.Now.AddMinutes(1);
            Feed("""{"event":"StartJump","JumpType":"Hyperspace","StarSystem":"Beta"}""");
            Assert.NotNull(workspace.Current);
            clock.Now = clock.Now.AddSeconds(20);
            Feed("""{"event":"FSDJump","StarSystem":"Beta","SystemAddress":2,"StarPos":[1,2,3]}""");
            Assert.Null(workspace.Current);
            Assert.Equal(clock.Now, Assert.Single(workspace.History).Ended);
            Assert.Null(new MiningStore(directory).Load("F1").Current);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void SessionOverviewShowsLiveProspectingTotalsAndClearsThemWhenTheSessionEnds()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: new Clock(),
                announcementOutputs: new MiningAnnouncementOutputs(new SpeechRecorder(), new ChimeRecorder())
            );
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            void Feed(string json)
            {
                JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(json);
                context.Apply(entry);
                workspace.Apply(new(null, [entry], ship, null, null, null, [], false), context, null, ship);
            }

            Feed("""{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}""");
            workspace.StartCommand.Execute(null);
            workspace.Settings.Thresholds["Platinum"] = 30;
            workspace.Settings.Thresholds["Osmium"] = 20;
            using var overview = new MiningSessionOverlayViewModel(workspace);
            var changed = new List<string?>();
            overview.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.False(overview.HasProspectingYields);

            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:01Z","Materials":[{"Name":"Platinum","Proportion":40},{"Name":"Osmium","Proportion":10}],"Remaining":100}"""
            );
            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:02Z","Materials":[{"Name":"Platinum","Proportion":20},{"Name":"Osmium","Proportion":30}],"MotherlodeMaterial":"Monazite","Remaining":100}"""
            );
            Assert.True(overview.HasProspectingYields);
            Assert.Equal(workspace.Materials, overview.ProspectingYields);
            Assert.Equal(
                new MiningMaterialSummary("Platinum", 2, 1, 30, 40, 0),
                overview.ProspectingYields.Single(row => row.Name == "Platinum")
            );
            Assert.Equal(
                new MiningMaterialSummary("Osmium", 2, 1, 20, 30, 0),
                overview.ProspectingYields.Single(row => row.Name == "Osmium")
            );
            Assert.Equal(1, overview.ProspectingYields.Single(row => row.Name == "Monazite").QualityHits);
            Assert.Contains(nameof(overview.ProspectingYields), changed);
            Assert.Contains(nameof(overview.HasProspectingYields), changed);

            workspace.Settings.Thresholds["Platinum"] = 45;
            workspace.SaveSettingsCommand.Execute(null);
            Assert.Equal(0, overview.ProspectingYields.Single(row => row.Name == "Platinum").QualityHits);
            workspace.PauseCommand.Execute(null);
            MiningMaterialSummary[] paused = overview.ProspectingYields.ToArray();
            Feed(
                """{"event":"ProspectedAsteroid","timestamp":"2026-09-06T12:00:03Z","Materials":[{"Name":"Platinum","Proportion":60}],"Remaining":100}"""
            );
            Assert.Equal(paused, overview.ProspectingYields);
            workspace.StopCommand.Execute(null);
            Assert.NotNull(workspace.SelectedSession);
            Assert.Empty(overview.ProspectingYields);
            Assert.False(overview.HasProspectingYields);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ManualSessionControlsShareMasterBindingsAndUpdateTheOverview()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory)
            );
            var store = new GlobalInputSettingsStore(Path.Combine(directory, "ui.json"));
            var input = new GlobalInputSettingsViewModel(
                store,
                OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows),
                new SessionInputDevices()
            );
            workspace.UseSessionBindings(input);
            var availability = new OverlayPanelVisibilityViewModel(
                new OverlayPanelVisibilitySettingsStore(Path.Combine(directory, "ui.json")),
                input,
                new OverlayWindowRegistry()
            );
            OverlayPanelVisibilityEntryViewModel sessionPanel = availability
                .ForCategory(OverlaySettingsCategory.Mining)
                .Single(panel => panel.PlotterName == "PlotMiningSession");
            Assert.Same(
                input.Bindings.Single(binding =>
                    binding.Definition.Action == GlobalInputAction.ToggleMiningSessionVisibility
                ),
                sessionPanel.Shortcut
            );
            sessionPanel.Shortcut.Chord = "CTRL L";
            Assert.Equal("CTRL L", store.Load().Bindings[GlobalInputAction.ToggleMiningSessionVisibility]);
            Assert.Equal(3, workspace.SessionBindings.Count);
            foreach (InputBindingViewModel binding in workspace.SessionBindings)
            {
                Assert.Same(
                    input.Bindings.Single(item => item.Definition.Action == binding.Definition.Action),
                    binding
                );
            }
            InputBindingViewModel startBinding = workspace.SessionBindings.Single(item =>
                item.Definition.Action == GlobalInputAction.StartMiningSession
            );
            startBinding.Chord = "CTRL J";
            Assert.Equal("CTRL J", store.Load().Bindings[GlobalInputAction.StartMiningSession]);
            input.Bindings.Single(item => item.Definition.Action == GlobalInputAction.StartMiningSession).Chord =
                "CTRL K";
            Assert.Equal("CTRL K", startBinding.Chord);
            input.ResetBindingsCommand.Execute(null);
            Assert.Equal(string.Empty, startBinding.Chord);
            using var overview = new MiningSessionOverlayViewModel(workspace);
            Assert.False(overview.HasSession);
            Assert.Equal("No active session", overview.State);
            Assert.False(workspace.ExecuteSessionControl(GlobalInputAction.StartMiningSession));
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            JournalEventEnvelope entry = FiregroupsWorkspaceViewModelTests.Event(
                """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}"""
            );
            context.Apply(entry);
            workspace.Apply(new(null, [entry], ship, null, null, null, [], true), context, null, ship);
            int changes = 0;
            overview.PropertyChanged += (_, _) => changes++;
            Assert.True(workspace.ExecuteSessionControl(GlobalInputAction.StartMiningSession));
            Assert.True(overview.HasSession);
            Assert.Equal("Active", overview.State);
            Assert.True(workspace.ShouldShowSession);
            Assert.Contains("Python", overview.Location);
            Assert.NotEmpty(overview.Duration);
            Assert.Contains("0 t", overview.Yield);
            Assert.Contains("asteroids", overview.Activity);
            Assert.Contains("prospectors", overview.Limpets);
            Assert.Empty(overview.ProspectingYields);
            Assert.False(overview.HasProspectingYields);
            Assert.False(overview.HasNotes);
            Assert.True(workspace.ExecuteSessionControl(GlobalInputAction.PauseResumeMiningSession));
            Assert.Equal("Paused", overview.State);
            Assert.True(workspace.ExecuteSessionControl(GlobalInputAction.PauseResumeMiningSession));
            Assert.Equal("Active", overview.State);
            workspace.Settings.AutoStopAfterTravel = false;
            workspace.SaveSettingsCommand.Execute(null);
            Assert.False(new MiningStore(directory).Load("F1").Settings.AutoStopAfterTravel);
            Assert.True(workspace.ExecuteSessionControl(GlobalInputAction.EndMiningSession));
            Assert.False(overview.HasSession);
            Assert.False(workspace.ShouldShowSession);
            Assert.False(workspace.ExecuteSessionControl(GlobalInputAction.EndMiningSession));
            Assert.False(workspace.ExecuteSessionControl(GlobalInputAction.Track1));
            Assert.True(changes > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class SessionInputDevices : IControllerDeviceProvider
    {
        public ControllerDeviceDiscoveryResult Discover() => new([], null);
    }
}
