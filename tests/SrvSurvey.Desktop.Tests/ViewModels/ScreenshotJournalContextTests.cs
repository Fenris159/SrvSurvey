using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Tests.ProfileSync;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ScreenshotJournalContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapAndLiveConversionUseTheSelectedCommandersConfiguredProfile(bool linked)
    {
        if (linked && !OperatingSystem.IsLinux())
        {
            return;
        }
        using var temporary = new ProfileSyncTestRoot();
        string root = temporary.Paths.DataDirectory;
        string first = Path.Combine(
            root,
            "first",
            "drive_c",
            "users",
            "steamuser",
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous"
        );
        string secondProfile = Path.Combine(root, "second", "drive_c", "users", "steamuser");
        string second = Path.Combine(secondProfile, "Saved Games", "Frontier Developments", "Elite Dangerous");
        string physical = linked ? Path.Combine(root, "shared-journals") : second;
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(physical);
        if (linked)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            Directory.CreateSymbolicLink(second, physical);
        }
        string journal = Path.Combine(physical, "Journal.2026-11-03T050000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"2026-11-03T05:00:00Z","event":"Commander","FID":"F456","Name":"Second"}
            {"timestamp":"2026-11-03T05:00:01Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-11-03T05:00:02Z","event":"Screenshot","Filename":"\\ED_Pictures\\old.bmp","System":"Sol","Body":"Earth"}
            """ + "\n"
        );
        new JournalSettingsStore(temporary.Paths.SharedUiSettingsPath).Save(new(first, [second]));
        new UiSettingsDocumentStore(temporary.Paths.UiSettingsPath).Update(settings =>
            settings["Screenshots"] = new System.Text.Json.Nodes.JsonObject
            {
                ["Enabled"] = true,
                ["AddBanner"] = false,
            }
        );
        string pictures = Path.Combine(secondProfile, "Pictures", "Frontier Developments", "Elite Dangerous");
        Directory.CreateDirectory(pictures);
        await File.WriteAllBytesAsync(
            Path.Combine(pictures, "source.bmp"),
            Convert.FromBase64String("Qk06AAAAAAAAADYAAAAoAAAAAQAAAAEAAAABABgAAAAAAAQAAAATCwAAEwsAAAAAAAAAAAAA/wAAAA==")
        );
        await using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(temporary.Paths).WithTargetFrontierId("F456")
        );
        await model.RefreshAsync();
        Assert.Equal(pictures, model.ScreenshotProcessing.SourceFolder);
        Assert.Equal(Path.Combine(pictures, "converted"), model.ScreenshotProcessing.TargetFolder);
        Assert.False(Directory.Exists(model.ScreenshotProcessing.TargetFolder));
        await File.AppendAllTextAsync(
            journal,
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\source.bmp","System":"Sol","Body":"Earth"}"""
                + "\n"
        );
        await model.RefreshAsync();
        Assert.Equal(pictures, model.ScreenshotProcessing.SourceFolder);
        Assert.True(
            File.Exists(Path.Combine(pictures, "converted", "Sol", "Earth (2026-11-03 050653).png")),
            model.ScreenshotProcessing.StatusMessage
        );
    }
}
