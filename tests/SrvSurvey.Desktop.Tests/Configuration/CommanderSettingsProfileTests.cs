using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CommanderSettingsProfileTests
{
    [Fact]
    public void RejectsInvalidCommanderIdBeforeCreatingSettingsPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-settings-invalid-{Guid.NewGuid():N}");
        var paths = new AppDataPaths(Path.Combine(root, "config"), Path.Combine(root, "data"), root, [])
        {
            SettingsFrontierId = "../other",
        };

        Assert.Null(CommanderSettingsProfile.NormalizeFrontierId(paths.SettingsFrontierId));
        Assert.Throws<ArgumentException>(() => CommanderSettingsProfile.Prepare(paths));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void CopiesExistingSettingsOnceAndKeepsCommanderOverlaysSeparate()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-settings-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppDataPaths(Path.Combine(root, "config"), Path.Combine(root, "data"), root, []);
            Directory.CreateDirectory(paths.ConfigDirectory);
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(paths.SharedUiSettingsPath, "{\"Overlay\":1}");
            string[] legacyOverlayFiles =
            [
                "plotters.json",
                "settings.json",
                "overlay-scale-overrides.json",
                "overlay-typography-overrides.json",
                "overlay-size-overrides.json",
                "overlay-position-references.json",
                "theme.json",
                "overlay-theme-states.json",
            ];
            foreach (string fileName in legacyOverlayFiles)
            {
                File.WriteAllText(Path.Combine(paths.DataDirectory, fileName), fileName);
            }

            AppDataPaths first = paths with { SettingsFrontierId = "F123" };
            AppDataPaths second = paths with { SettingsFrontierId = "F456" };
            CommanderSettingsProfile.Prepare(first);
            CommanderSettingsProfile.Prepare(second);
            File.WriteAllText(first.UiSettingsPath, "{\"Overlay\":2}");
            File.WriteAllText(Path.Combine(first.OverlaySettingsDirectory, "plotters.json"), "changed layout");
            CommanderSettingsProfile.Prepare(first);

            Assert.Equal("{\"Overlay\":1}", File.ReadAllText(paths.SharedUiSettingsPath));
            Assert.Equal("{\"Overlay\":2}", File.ReadAllText(first.UiSettingsPath));
            Assert.Equal("{\"Overlay\":1}", File.ReadAllText(second.UiSettingsPath));
            Assert.Equal(
                "changed layout",
                File.ReadAllText(Path.Combine(first.OverlaySettingsDirectory, "plotters.json"))
            );
            foreach (string fileName in legacyOverlayFiles)
            {
                Assert.Equal(fileName, File.ReadAllText(Path.Combine(second.OverlaySettingsDirectory, fileName)));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AutomaticSettingsCommanderComesFromLatestJournal()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-settings-journal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string journal = Path.Combine(root, "Journal.2026-09-25T000000.01.log");
            File.WriteAllLines(
                journal,
                [
                    "{\"timestamp\":\"2026-09-25T00:00:00Z\",\"event\":\"Fileheader\",\"gameversion\":\"4.1\"}",
                    "{\"timestamp\":\"2026-09-25T00:00:01Z\",\"event\":\"Commander\",\"FID\":\"F456\",\"Name\":\"Second\"}",
                ]
            );

            Assert.Equal(
                "F456",
                CommanderSettingsProfile.FindLatestJournalFrontierId(Path.Combine(root, "settings.json"), root)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
