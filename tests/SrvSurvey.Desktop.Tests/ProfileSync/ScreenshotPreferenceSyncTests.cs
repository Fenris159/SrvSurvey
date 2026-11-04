using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ScreenshotPreferenceSyncTests
{
    [Theory]
    [InlineData("cross-platform-ui.json")]
    [InlineData("commanders/F123/cross-platform-ui.json")]
    public void ScreenshotFoldersRequireAnExplicitRestoreOnTheirOwnMachineWhileProcessingOptionsArePortable(string file)
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

        ScreenshotProcessingPreferences portableRestore = sourceSettings.Load();
        AssertFolders(currentFolders, portableRestore);
        Assert.True(portableRestore.Enabled);
        Assert.True(portableRestore.ProcessSteamScreenshots);
        Assert.True(portableRestore.AddBanner);
        Assert.True(portableRestore.DeleteOriginal);

        Assert.Throws<InvalidOperationException>(() => destination.Store.Apply(backup, restoreMachine: true));
        AssertFolders(destinationFolders, destinationSettings.Load());
        Assert.False(destinationSettings.Load().ProcessSteamScreenshots);

        destination.Store.Apply(backup, restoreMachine: false);

        ScreenshotProcessingPreferences otherMachine = destinationSettings.Load();
        AssertFolders(destinationFolders, otherMachine);
        Assert.True(otherMachine.ProcessSteamScreenshots);
        Assert.True(otherMachine.AddBanner);
        Assert.True(otherMachine.DeleteOriginal);

        source.Store.Apply(backup, restoreMachine: true);

        AssertFolders(backupFolders, sourceSettings.Load());
    }

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

    private static void AssertFolders(ScreenshotProcessingPreferences expected, ScreenshotProcessingPreferences actual)
    {
        Assert.Equal(expected.SourceFolder, actual.SourceFolder);
        Assert.Equal(expected.SteamSourceFolder, actual.SteamSourceFolder);
        Assert.Equal(expected.TargetFolder, actual.TargetFolder);
    }
}
