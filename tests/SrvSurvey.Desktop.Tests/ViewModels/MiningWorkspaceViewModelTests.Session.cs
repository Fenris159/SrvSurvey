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
            Assert.Contains("Cargo", overview.Cargo);
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
