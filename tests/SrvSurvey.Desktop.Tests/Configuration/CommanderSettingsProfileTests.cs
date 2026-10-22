using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ProfileSync;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CommanderSettingsProfileTests
{
    [Fact]
    public async Task PublishingImportedLayoutsFillsMissingPanelsWithoutReplacingCurrentCommanderPositions()
    {
        using var temp = new ProfileSyncTestRoot();
        AppDataPaths paths = temp.Paths with { SettingsFrontierId = "F123" };
        string source = Path.Combine(temp.Paths.CacheDirectory, "source", "cross-platform");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "plotters.json"),
            """{"PlotJumpInfo":"left:10, top:20","PlotBodyInfo":"left:30, top:40"}"""
        );
        Directory.CreateDirectory(paths.OverlaySettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(paths.OverlaySettingsDirectory, "plotters.json"),
            """{"PlotJumpInfo":"left:100, top:200"}"""
        );
        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            new ProfileImportSource(source, ProfileImportSourceKind.CrossPlatform, null),
            paths.DataDirectory,
            Path.Combine(temp.Paths.CacheDirectory, "backups")
        );

        CommanderSettingsProfile.PublishImportedOverlays(paths, result);

        JsonNode layout = JsonNode.Parse(
            await File.ReadAllTextAsync(Path.Combine(paths.OverlaySettingsDirectory, "plotters.json"))
        )!;
        Assert.Equal("left:100, top:200", layout["PlotJumpInfo"]!.GetValue<string>());
        Assert.Equal("left:30, top:40", layout["PlotBodyInfo"]!.GetValue<string>());
    }

    /// <summary>Legacy vehicle VR calibrations initialize private commander copies once, without carrying later edits to another commander.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VehicleVrCalibrationsMigrateOnceIntoEachCommanderProfile(bool existingProfile)
    {
        using var temp = new ProfileSyncTestRoot();
        temp.Write("data", "vr/testbuggy.json", """{"PlotJumpInfo":"{ s: 18, p: <1, 2, 3>, r: <4, 5, 6>}"}""");
        AppDataPaths first = temp.Paths with { SettingsFrontierId = "F123" };
        AppDataPaths second = temp.Paths with { SettingsFrontierId = "F456" };
        if (existingProfile)
        {
            Directory.CreateDirectory(first.OverlaySettingsDirectory);
            File.WriteAllText(Path.Combine(first.OverlaySettingsDirectory, ".profile-initialized"), "");
        }
        CommanderSettingsProfile.Prepare(first);
        CommanderSettingsProfile.Prepare(second);
        temp.Write("data", "vr/testbuggy.json", """{"PlotJumpInfo":"{ s: 25, p: <1, 2, 3>, r: <4, 5, 6>}"}""");
        CommanderSettingsProfile.Prepare(first);

        Assert.Equal(
            18,
            new VrOverlayCalibrationStore(first.OverlaySettingsDirectory)
                .Load()
                .Resolve("PlotJumpInfo", "testbuggy")
                ?.Scale
        );
        Assert.Equal(
            18,
            new VrOverlayCalibrationStore(second.OverlaySettingsDirectory)
                .Load()
                .Resolve("PlotJumpInfo", "testbuggy")
                ?.Scale
        );
    }

    /// <summary>Rejects unsafe commander identifiers before profile initialization creates any directories.</summary>
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

    /// <summary>Commander profiles inherit shared settings once while keeping independent overlay files and later edits.</summary>
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
                "overlay-growth-overrides.json",
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
