using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ProfileSync;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ScreenshotPreferenceSyncTests
{
    [Theory]
    [InlineData("cross-platform-ui.json")]
    [InlineData("commanders/F123/cross-platform-ui.json")]
    public void ScreenshotConfigurationRequiresAnExplicitRestoreOnItsOriginalMachine(string file)
    {
        using var source = new ProfileSyncTestRoot();
        using var destination = new ProfileSyncTestRoot();
        var sourceSettings = new ScreenshotProcessingSettingsStore(source.Path("config", file));
        var destinationSettings = new ScreenshotProcessingSettingsStore(destination.Path("config", file));
        ScreenshotProcessingPreferences backupFolders = Preferences(source, "backup", enabled: true);
        ScreenshotProcessingPreferences currentFolders = Preferences(source, "current", enabled: false);
        ScreenshotProcessingPreferences destinationFolders = Preferences(destination, "destination", enabled: false);
        sourceSettings.Save(backupFolders, useAutomaticSource: false, useAutomaticTarget: false);
        ProfileSnapshot backup = source.Store.Capture();
        sourceSettings.Save(currentFolders, useAutomaticSource: false, useAutomaticTarget: false);
        destinationSettings.Save(destinationFolders, useAutomaticSource: false, useAutomaticTarget: false);

        source.Store.Apply(backup, restoreMachine: false);

        Assert.Equal(currentFolders, sourceSettings.Load());

        Assert.Throws<InvalidOperationException>(() => destination.Store.Apply(backup, restoreMachine: true));
        Assert.Equal(destinationFolders, destinationSettings.Load());

        destination.Store.Apply(backup, restoreMachine: false);

        Assert.Equal(destinationFolders, destinationSettings.Load());

        source.Store.Apply(backup, restoreMachine: true);

        Assert.Equal(backupFolders, sourceSettings.Load());
    }

    [Theory]
    [InlineData("cross-platform-ui.json", false)]
    [InlineData("commanders/F123/cross-platform-ui.json", false)]
    [InlineData("cross-platform-ui.json", true)]
    [InlineData("commanders/F123/cross-platform-ui.json", true)]
    public void LegacyScreenshotHistoryNoLongerBlocksBackupsOrCrossMachineRestores(string file, bool deleted)
    {
        using var source = new ProfileSyncTestRoot();
        using var destination = new ProfileSyncTestRoot();
        ProfileSnapshot legacy = LegacyBackup(source, file, deleted);
        File.WriteAllBytes(Path.Combine(source.Store.StateDirectory, "baseline.json"), legacy.Serialize());
        var sourceSettings = new ScreenshotProcessingSettingsStore(source.Path("config", file));
        ScreenshotProcessingPreferences current = Preferences(source, "current", enabled: false);
        sourceSettings.Save(current, useAutomaticSource: false, useAutomaticTarget: false);

        ProfileSnapshot captured = source.Store.Capture();
        Assert.DoesNotContain(captured.Portable.Keys, key => key.Contains("#/Screenshots", StringComparison.Ordinal));
        string backupPath = source.Store.SaveBackup(captured);
        var saved = ProfileSnapshot.Parse(File.ReadAllBytes(backupPath));
        Assert.Equal(current.SteamSourceFolder, saved.Machine[Prefix(file) + "/SteamSourceFolder"]!.GetValue<string>());

        var destinationSettings = new ScreenshotProcessingSettingsStore(destination.Path("config", file));
        ScreenshotProcessingPreferences other = Preferences(destination, "other", enabled: false);
        destinationSettings.Save(other, useAutomaticSource: false, useAutomaticTarget: false);
        destination.Store.StageExplicitRestore(legacy, restoreMachine: false);
        Assert.True(destination.Store.ApplyPending());
        Assert.Equal(other, destinationSettings.Load());

        source.Store.StageExplicitRestore(legacy, restoreMachine: true);
        Assert.True(source.Store.ApplyPending());
        ScreenshotProcessingPreferences restored = sourceSettings.Load();
        Assert.True(restored.Enabled);
        Assert.Equal(deleted ? current.SteamSourceFolder : "/old-computer/steam", restored.SteamSourceFolder);
        Assert.Contains(Prefix(file) + "/SteamSourceFolder", legacy.Portable.Keys);
    }

    [Fact]
    public async Task LegacyCloudScreenshotSettingsDoNotBlockStartupSyncOrShutdownBackup()
    {
        using var source = new ProfileSyncTestRoot();
        using var destination = new ProfileSyncTestRoot();
        var settings = new ScreenshotProcessingSettingsStore(destination.Paths.SharedUiSettingsPath);
        ScreenshotProcessingPreferences current = Preferences(destination, "current", enabled: false);
        settings.Save(current, useAutomaticSource: false, useAutomaticTarget: false);
        destination.Store.SavePreferences(new(Automatic: true));
        var cloud = new FakeProfileDrive();
        cloud.Add(LegacyBackup(source, "cross-platform-ui.json", deleted: false));
        using var service = new ProfileSyncService(destination.Store, cloud);

        await service.StartupAsync();

        Assert.Equal("Startup sync complete. Portable preferences and workspace data are up to date.", service.Status);
        Assert.Equal(current, settings.Load());
        JsonNode local = JsonNode.Parse(await File.ReadAllTextAsync(destination.Paths.SharedUiSettingsPath))!;
        Assert.Equal("gold", local["Theme"]!.GetValue<string>());

        await service.ShutdownAsync();

        ProfileSnapshot uploaded = Assert.Single(cloud.Uploaded);
        Assert.DoesNotContain(uploaded.Portable.Keys, key => key.Contains("#/Screenshots", StringComparison.Ordinal));
        Assert.Equal(
            current.SteamSourceFolder,
            uploaded.Machine["config/cross-platform-ui.json#/Screenshots/SteamSourceFolder"]!.GetValue<string>()
        );
    }

    [Fact]
    public void ExistingMachineScreenshotValuesTakePrecedenceOverLegacyPortableCopies()
    {
        using var root = new ProfileSyncTestRoot();
        ProfileSnapshot legacy = LegacyBackup(root, "cross-platform-ui.json", deleted: false);
        const string key = "config/cross-platform-ui.json#/Screenshots/SteamSourceFolder";
        legacy.Machine[key] = JsonValue.Create("/machine-specific/steam");

        root.Store.Apply(legacy, restoreMachine: true);

        var settings = new ScreenshotProcessingSettingsStore(root.Paths.SharedUiSettingsPath);
        Assert.Equal("/machine-specific/steam", settings.Load().SteamSourceFolder);
        Assert.Equal("/old-computer/steam", legacy.Portable[key].Value!.GetValue<string>());
    }

    [Fact]
    public void LegacyPortableRestoreStagedBeforeUpgradeDoesNotBecomeAMachineRestore()
    {
        using var source = new ProfileSyncTestRoot();
        using var destination = new ProfileSyncTestRoot();
        var settings = new ScreenshotProcessingSettingsStore(destination.Paths.SharedUiSettingsPath);
        ScreenshotProcessingPreferences current = Preferences(destination, "current", enabled: false);
        settings.Save(current, useAutomaticSource: false, useAutomaticTarget: false);
        destination.Store.SaveBaseline(destination.Store.Capture());
        ProfileSnapshot legacy = LegacyBackup(source, "cross-platform-ui.json", deleted: false) with
        {
            DeviceId = destination.Store.Device.Id,
            Platform = destination.Store.Device.Platform,
            MachineFingerprint = destination.Store.Device.MachineHash,
            Machine = [],
        };
        File.WriteAllBytes(destination.Store.PendingPath, legacy.Serialize());

        Assert.True(destination.Store.ApplyPending());

        Assert.Equal(current, settings.Load());
        JsonNode local = JsonNode.Parse(File.ReadAllText(destination.Paths.SharedUiSettingsPath))!;
        Assert.Equal("gold", local["Theme"]!.GetValue<string>());
        Assert.False(File.Exists(destination.Store.PendingPath));
    }

    [Fact]
    public void ScreenshotImagesAndConversionQueuesAreExcludedWhileConfigurationIsBackedUp()
    {
        using var root = new ProfileSyncTestRoot();
        var settings = new ScreenshotProcessingSettingsStore(root.Paths.SharedUiSettingsPath);
        settings.Save(Preferences(root, "systems", enabled: true));
        root.Write("data", "systems/capture.png", "private-png-image-content");
        root.Write("data", "systems/capture.bmp", "private-bmp-image-content");
        root.Write("data", "screenshot-conversion/pending.json", "{\"image\":\"private-queued-image\"}");

        ProfileSnapshot snapshot = root.Store.Capture();
        string backup = File.ReadAllText(root.Store.SaveBackup(snapshot));

        Assert.DoesNotContain("private-png-image-content", backup);
        Assert.DoesNotContain("private-bmp-image-content", backup);
        Assert.DoesNotContain("private-queued-image", backup);
        Assert.Equal("private-png-image-content", File.ReadAllText(root.Path("data", "systems/capture.png")));
        Assert.DoesNotContain(snapshot.Portable.Keys, key => key.Contains("#/Screenshots", StringComparison.Ordinal));
        Assert.True(snapshot.Machine["config/cross-platform-ui.json#/Screenshots/Enabled"]!.GetValue<bool>());
        Assert.DoesNotContain("capture.png", Encoding.UTF8.GetString(snapshot.Serialize()));
    }

    [Theory]
    [InlineData("config/..%2Fcross-platform-ui.json#/Screenshots/Enabled")]
    [InlineData("config/cross-platform-ui.json#/Screenshots//Enabled")]
    [InlineData("config/google-authorization.dat#/Screenshots/Enabled")]
    public void ScreenshotCompatibilityDoesNotPermitInvalidBackupKeys(string key)
    {
        using var root = new ProfileSyncTestRoot();
        ProfileSnapshot invalid = root.Store.Capture() with
        {
            Portable = new() { [key] = new(JsonValue.Create(true), false, new() { ["old"] = 1 }) },
        };
        Assert.Throws<InvalidDataException>(() => root.Store.Apply(invalid));
    }

    private static ProfileSnapshot LegacyBackup(ProfileSyncTestRoot root, string file, bool deleted)
    {
        string screenshot = Prefix(file);
        return root.Store.Capture() with
        {
            Portable = new()
            {
                [screenshot + "/Enabled"] = new(JsonValue.Create(true), false, new() { ["old"] = 1 }),
                [screenshot + "/SteamSourceFolder"] = new(
                    deleted ? null : JsonValue.Create("/old-computer/steam"),
                    deleted,
                    new() { ["old"] = 1 }
                ),
                [screenshot.Replace("/Screenshots", "/Theme", StringComparison.Ordinal)] = new(
                    JsonValue.Create("gold"),
                    false,
                    new() { ["old"] = 1 }
                ),
            },
        };
    }

    private static string Prefix(string file) =>
        file == "cross-platform-ui.json"
            ? "config/cross-platform-ui.json#/Screenshots"
            : "config/commanders%2FF123%2Fcross-platform-ui.json#/Screenshots";

    private static ScreenshotProcessingPreferences Preferences(ProfileSyncTestRoot root, string name, bool enabled) =>
        ScreenshotProcessingPreferences.CreateDefaults() with
        {
            SourceFolder = root.Path("data", name + "/bmp"),
            SteamSourceFolder = root.Path("data", name + "/steam"),
            TargetFolder = root.Path("data", name + "/png"),
            Enabled = enabled,
            ProcessSteamScreenshots = enabled,
            AddBanner = enabled,
            DeleteOriginal = enabled,
        };
}
