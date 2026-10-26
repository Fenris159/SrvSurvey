using System.Text.Json.Nodes;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CrossPlatformUiSettingsImporterTests : IDisposable
{
    private static readonly string[] PortableSections = ["Colonization", "Localization", "Theme", "Version"];

    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-current-ui-import-tests-{Guid.NewGuid():N}"
    );

    [Theory]
    [InlineData(null, true)]
    [InlineData("{}", true)]
    [InlineData("{\"ShowInformation\":true}", true)]
    [InlineData("{\"ShowInformation\":false}", false)]
    public async Task ImportPreservesCarrierVisibilityIncludingUnsavedDefaults(string? currentSection, bool expected)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string source = Path.Combine(temporaryDirectory, "incoming.json");
        string destination = Path.Combine(temporaryDirectory, "current.json");
        await File.WriteAllTextAsync(source, """{"Version":1,"CarrierNextJump":{"ShowInformation":false}}""");
        var current = new JsonObject { ["Version"] = 1 };
        if (currentSection is not null)
        {
            current["CarrierNextJump"] = JsonNode.Parse(currentSection);
        }
        await File.WriteAllTextAsync(destination, current.ToJsonString());
        await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backup")
        );
        Assert.Equal(expected, new CarrierNextJumpSettingsStore(destination).Load());
        JsonNode activated = JsonNode.Parse(await File.ReadAllTextAsync(destination))!;
        Assert.Equal(current["CarrierNextJump"]?.ToJsonString(), activated["CarrierNextJump"]?.ToJsonString());
    }

    [Fact]
    public async Task ImportKeepsCurrentSettingsAndBacksUpBothVersions()
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(
            source,
            "{\"Version\":1,\"Theme\":\"orange-dark\",\"JumpInfo\":{\"AutoShow\":false}}"
        );
        await File.WriteAllTextAsync(destination, "{\"Version\":1,\"Theme\":\"blue-dark\"}");

        CrossPlatformUiSettingsImportResult result = await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            backup
        );

        Assert.True(result.Imported);
        Assert.Equal("blue-dark", new ThemePreferenceStore(destination).LoadThemeKey());
        Assert.True(new JumpInfoSettingsStore(destination).Load().AutoShow);
        Assert.Equal(
            await File.ReadAllBytesAsync(source),
            await File.ReadAllBytesAsync(Path.Combine(backup, "incoming-cross-platform-ui.json"))
        );
        Assert.Equal(
            "{\"Version\":1,\"Theme\":\"blue-dark\"}",
            await File.ReadAllTextAsync(Path.Combine(backup, LegacyUiSettingsMigrator.BackupFileName))
        );
        Assert.True(File.Exists(Path.Combine(backup, CrossPlatformUiSettingsImporter.CompletionSignalFileName)));
    }

    /// <summary>Missing overlay settings must keep XP defaults, even in a brand-new profile.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportSkipsOverlaySectionsAndRetainsPortablePlayerPreferences(bool hasCurrentSettings)
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(
            source,
            """
            {
              "Version": 1,
              "Theme": "orange-dark",
              "Localization": { "Language": "de" },
              "JumpInfo": { "AutoShow": false },
              "SystemSurvey": { "AutoShowBodyInfo": false },
              "GalaxyMap": { "AutoShow": false },
              "ExplorationPois": { "AutoShow": false },
              "BiologyPredictions": { "CurrentBodyOnly": true },
              "BiologyRewards": { "BucketOneMillions": 5 },
              "Combat": { "AutoShowFootCombat": false },
              "GuardianGestures": { "BlinkTrigger": 42 },
              "HumanSite": { "AutoShow": false },
              "MineMap": { "OnlyShowWhileOnGround": true },
              "Notifications": { "Enabled": false },
              "StationInfo": { "AutoShow": false },
              "Streaming": { "JoinedOverlayEnabled": true },
              "SurfaceMining": { "Detection": { "Enabled": false } },
              "VirtualReality": { "Enabled": true },
              "WaylandCapture": { "Enabled": false },
              "FirstFootfallInference": { "Enabled": false },
              "Input": { "KeyboardEnabled": false, "Bindings": { "showJumpInfo": "ALT J" } },
              "OverlayBehavior": { "HideInDominatorSuit": true },
              "OverlayScale": { "Index": 22 },
              "OverlayPanelVisibility": { "PlotJumpInfo": false },
              "FutureOverlay": { "Enabled": false },
              "Colonization": {
                "Enabled": true,
                "Overlay": { "AutoShow": false },
                "BuildSiteRepairVisits": [{ "MarketId": 123, "StationKey": "test" }]
              }
            }
            """
        );
        if (hasCurrentSettings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, """{"Version":1,"Theme":"blue-dark"}""");
        }

        await CrossPlatformUiSettingsImporter.ImportAsync(source, destination, backup);

        JsonObject imported = Assert.IsType<JsonObject>(JsonNode.Parse(await File.ReadAllTextAsync(destination)));
        Assert.Equal(hasCurrentSettings ? "blue-dark" : "orange-dark", imported["Theme"]?.GetValue<string>());
        Assert.Equal("de", imported["Localization"]?["Language"]?.GetValue<string>());
        Assert.True(new JumpInfoSettingsStore(destination).Load().AutoShow);
        Assert.True(new SystemSurveySettingsStore(destination).Load().AutoShowBodyInfo);
        Assert.False(new StreamOverlaySettingsStore(destination).LoadEnabled());
        Assert.False(new VrOverlaySettingsStore(destination).Load().Enabled);
        Assert.False(new OverlayBehaviorSettingsStore(destination).Load().HideInDominatorSuit);
        Assert.Equal(OverlayScalePreferences.Default, new OverlayScaleSettingsStore(destination).Load());
        Assert.Equal(PortableSections, imported.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Null(imported["Colonization"]?["Overlay"]);
        Assert.True(new ColonizationSettingsStore(destination).LoadEnabled());
        Assert.Single(new ColonizationSettingsStore(destination).LoadBuildSiteRepairVisits());
        Assert.Equal(
            await File.ReadAllBytesAsync(source),
            await File.ReadAllBytesAsync(Path.Combine(backup, "incoming-cross-platform-ui.json"))
        );
    }

    /// <summary>Existing choices win in both directions, and partial sections must not gain foreign defaults.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportLeavesSavedOverlaySettingsByteForByteUnchanged(bool autoShow)
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(
            source,
            """
            {"Version":1,"JumpInfo":{"AutoShow":false,"Minimal":true},
             "Streaming":{"JoinedOverlayEnabled":true},"VirtualReality":{"Enabled":true},
             "OverlayScale":{"Index":22},"Colonization":{"Overlay":{"AutoShow":false}}}
            """
        );
        string current = $$"""
            { "Version": 1, "JumpInfo": { "AutoShow": {{autoShow.ToString().ToLowerInvariant()}} },
              "Streaming": { "JoinedOverlayEnabled": false }, "Colonization": { "Overlay": { "FutureSetting": 42 } } }
            """;
        await File.WriteAllTextAsync(destination, current);

        await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "backup")
        );
        await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            Path.Combine(temporaryDirectory, "repeat-backup")
        );

        Assert.Equal(current, await File.ReadAllTextAsync(destination));
        Assert.Equal(autoShow, new JumpInfoSettingsStore(destination).Load().AutoShow);
        Assert.False(new JumpInfoSettingsStore(destination).Load().Minimal);
        Assert.True(new ColonizationSettingsStore(destination).LoadOverlayPreferences().AutoShow);
    }

    [Fact]
    public async Task InvalidSourcePreservesCurrentSettings()
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(source, "{invalid");
        await File.WriteAllTextAsync(destination, "{\"Version\":1}");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CrossPlatformUiSettingsImporter.ImportAsync(source, destination, backup)
        );

        Assert.Equal("{\"Version\":1}", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task ImportingPreferencesKeepsCurrentOverlayPositionReferences()
    {
        string source = Path.Combine(temporaryDirectory, "source", "cross-platform-ui.json");
        string destination = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        string backup = Path.Combine(temporaryDirectory, "backup");
        string data = Path.Combine(temporaryDirectory, "data");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(
            source,
            """
            {
              "Version": 1,
              "DesktopBehavior": {
                "ApplicationWindowPosition": { "X": 32, "Y": 342, "Monitor": "bounds:0,0,3840,2160" }
              }
            }
            """
        );
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(
            destination,
            """{"DesktopBehavior":{"ApplicationWindowPosition":{"Monitor":"bounds:0,0,2560,1440"}}}"""
        );
        await File.WriteAllTextAsync(Path.Combine(data, "plotters.json"), """{"PlotJumpInfo":"left:100, top:200"}""");
        new LegacyOverlayLayoutStore(data).SaveImportedPositionReferences(new OverlayPositionReference(1920, 1080));
        await File.WriteAllTextAsync(
            Path.Combine(data, "plotters.json"),
            """{"PlotJumpInfo":"left:100, top:200","PlotBodyInfo":"left:960, top:540"}"""
        );

        CrossPlatformUiSettingsImportResult result = await CrossPlatformUiSettingsImporter.ImportAsync(
            source,
            destination,
            backup
        );
        LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(data).Load();

        Assert.Equal(new OverlayPositionReference(1920, 1080), layout.Placements["PlotJumpInfo"].PositionReference);
        Assert.True(result.Imported);
        Assert.Null(layout.Placements["PlotBodyInfo"].PositionReference);
        Assert.Equal(
            new PixelPoint(960, 540),
            layout.GetPosition("PlotBodyInfo", new PixelRect(0, 0, 2560, 1440), new PixelSize(300, 120))
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
