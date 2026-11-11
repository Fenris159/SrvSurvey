using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CockpitOverlaySettingsTests : IDisposable
{
    [Fact]
    public void EditorAngleControlsFollowOptInAndDetachWhenTheirPreferencesAreRemoved()
    {
        using var editor = new OverlayInteractionViewModel(
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows)
        );
        Assert.False(editor.IsCockpitAngleEditingEnabled);
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(SettingsPath));
        editor.CockpitPanels = model;
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        model.Enabled = true;
        Assert.True(editor.IsCockpitAngleEditingEnabled);
        Assert.Contains(nameof(OverlayInteractionViewModel.IsCockpitAngleEditingEnabled), changed);
        editor.CockpitPanels = null;
        Assert.False(editor.IsCockpitAngleEditingEnabled);
        changed.Clear();
        model.Enabled = false;
        Assert.Empty(changed);
        editor.CockpitPanels = model;
        editor.Dispose();
        changed.Clear();
        model.Enabled = true;
        Assert.Empty(changed);
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cockpit-tests-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(directory, "settings.json");

    [Fact]
    public void OptInDefaultsOffAndPreservesUnrelatedSettings()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{\"Keep\":42}");
        var store = new CockpitOverlaySettingsStore(SettingsPath);
        Assert.False(store.Load().Enabled);
        store.Save(
            new CockpitOverlayPreferences(
                true,
                new Dictionary<string, CockpitPanelPose> { ["PlotJumpInfo"] = new(15, -10, 20, 1.5) }
            )
        );
        CockpitOverlayPreferences saved = store.Load();
        Assert.True(saved.Enabled);
        Assert.Equal(new CockpitPanelPose(15, -10, 20, 1.5), saved.Panels["PlotJumpInfo"]);
        Assert.Equal(42, JsonNode.Parse(File.ReadAllText(SettingsPath))!["Keep"]!.GetValue<int>());
    }

    [Fact]
    public void CancellingEditorRestoresAnglesAndSavingKeepsPanelsIndependent()
    {
        var store = new CockpitOverlaySettingsStore(SettingsPath);
        var model = new CockpitOverlayViewModel(store);
        model.BeginEdit();
        model.SelectPanel("PlotJumpInfo");
        model.Yaw = 35;
        model.Depth = 2;
        Assert.True(model.HasChanges);
        model.CancelEdit();
        Assert.Equal(new CockpitPanelPose(), model.Resolve("PlotJumpInfo"));
        Assert.Empty(store.Load().Panels);

        model.BeginEdit();
        model.Yaw = 25;
        model.Pitch = -15;
        model.Roll = 10;
        model.Depth = 1.5;
        model.SelectPanel("PlotMassacreMissions");
        model.Roll = -20;
        model.SaveEdit();
        model.CancelEdit();

        Assert.Equal(new CockpitPanelPose(25, -15, 10, 1.5), store.Load().Panels["PlotJumpInfo"]);
        Assert.Equal(new CockpitPanelPose(Roll: -20), store.Load().Panels["PlotMassacreMissions"]);
        Assert.False(model.HasChanges);
    }

    [Fact]
    public void EnablingDuringAnEditDoesNotPersistUnsavedAngles()
    {
        var store = new CockpitOverlaySettingsStore(SettingsPath);
        var model = new CockpitOverlayViewModel(store);
        model.BeginEdit();
        model.SelectPanel("PlotJumpInfo");
        model.Yaw = 40;
        model.Enabled = true;
        Assert.True(model.RestartRequired);
        Assert.True(store.Load().Enabled);
        Assert.Empty(store.Load().Panels);
        model.CancelEdit();
        Assert.Equal(0, model.Yaw);
        Assert.False(new CockpitOverlayViewModel(store).RestartRequired);
    }

    [Fact]
    public void InvalidStoredPosesAreSkippedAndInvalidEditsAreRejected()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            SettingsPath,
            "{\"CockpitPanelsPrototype\":{\"Panels\":{\"TooFar\":{\"Depth\":20},\"Broken\":\"bad\"}}}"
        );
        var store = new CockpitOverlaySettingsStore(SettingsPath);
        Assert.Empty(store.Load().Panels);
        var model = new CockpitOverlayViewModel(store);
        model.BeginEdit();
        model.SelectPanel("PlotJumpInfo");
        model.Pitch = double.NaN;
        model.Yaw = 86;
        Assert.Equal(new CockpitPanelPose(), model.Resolve("PlotJumpInfo"));
        Assert.Throws<ArgumentException>(() =>
            store.Save(
                new CockpitOverlayPreferences(
                    true,
                    new Dictionary<string, CockpitPanelPose> { ["Bad"] = new(Depth: 0) }
                )
            )
        );
    }

    [Fact]
    public void ResetAndCalibrationCommandsRespectAvailability()
    {
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(SettingsPath));
        Assert.False(model.ResetPanelCommand.CanExecute(null));
        Assert.False(model.CalibrateCommand.CanExecute(null));
        model.SelectPanel("PlotJumpInfo");
        model.Roll = 35;
        model.ResetPanelCommand.Execute(null);
        Assert.Equal(0, model.Roll);
        int requested = 0;
        model.CalibrationRequested += (_, _) => requested++;
        model.CalibrateCommand.Execute(null);
        Assert.Equal(0, requested);
        model.CanCalibrate = true;
        model.CalibrateCommand.Execute(null);
        Assert.Equal(1, requested);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
