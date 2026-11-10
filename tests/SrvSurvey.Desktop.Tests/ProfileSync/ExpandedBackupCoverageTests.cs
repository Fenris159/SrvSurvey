using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ExpandedBackupCoverageTests
{
    [Theory]
    [InlineData("cross-platform-ui.json")]
    [InlineData("commanders/F123/cross-platform-ui.json")]
    public void ColonizationPlansShoppingAndPreferencesFollowTheCommander(string file)
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        source.Write(
            "config",
            file,
            """
            {"ExplorationPois":{"AutoShow":false},"Colonization":{
              "PlanningCarrierSelections":{"BACKUP PILOT":[42,43]},"ProjectFocus":{"BACKUP PILOT":"plan-one"},
              "HiddenPlannedProjects":{"BACKUP PILOT":["hidden"]},"PendingContributions":[{"secret":"queued-upload"}]}}
            """
        );
        source.Write("config", file + ".plans.json", """{"BACKUP PILOT":[{"Name":"My planned system","Sites":[]}]}""");
        source.Write(
            "config",
            file + ".shopping.json",
            """
            {"Version":1,"BACKUP PILOT":{"ProjectIds":["build-one"],"Search":{
              "SelectedCarrierMarketIds":[42,43],"WishList":[{"MarketKey":"station","Commodity":"steel","Quantity":12}]}}}
            """
        );
        source.Write("config", file + ".local-projects.json", """{"BACKUP PILOT":{"BrowseEnabled":true}}""");
        source.Write("config", file + ".systems.json", """{"cache":"rebuildable-system-catalog"}""");

        ProfileSnapshot snapshot = source.Store.Capture();
        target.Store.Apply(snapshot);

        JsonNode settings = Read(target.Path("config", file));
        Assert.False(settings["ExplorationPois"]!["AutoShow"]!.GetValue<bool>());
        Assert.Equal("plan-one", settings["Colonization"]!["ProjectFocus"]!["BACKUP PILOT"]!.GetValue<string>());
        Assert.Equal(
            "[42,43]",
            settings["Colonization"]!["PlanningCarrierSelections"]!["BACKUP PILOT"]!.ToJsonString()
        );
        Assert.Equal(
            "My planned system",
            Read(target.Path("config", file + ".plans.json"))["BACKUP PILOT"]![0]!["Name"]!.GetValue<string>()
        );
        Assert.Equal(
            12,
            Read(target.Path("config", file + ".shopping.json"))["BACKUP PILOT"]!["Search"]!["WishList"]![0]![
                "Quantity"
            ]!.GetValue<int>()
        );
        Assert.True(
            Read(target.Path("config", file + ".local-projects.json"))["BACKUP PILOT"]![
                "BrowseEnabled"
            ]!.GetValue<bool>()
        );
        string exported = Encoding.UTF8.GetString(snapshot.Serialize());
        Assert.DoesNotContain("queued-upload", exported);
        Assert.DoesNotContain("rebuildable-system-catalog", exported);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("legacy")]
    public void CommanderPreferencesRestoreWithoutCopyingCredentialsOrLiveTotals(string mode)
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        string file = $"F123-{mode}.json";
        source.Write(
            "data",
            file,
            """
            {"fid":"F123","commander":"Backup Pilot","rccApiKey":"source-secret","inaraApiKey":"inara-secret","explRewards":900,
             "autoResetExplorationValue":true,"activeJourney":"trip.json",
             "boxelSearch":{"autoCopy":true,"lowMassCode":"d","completedSystems":["A"]},
             "sphereLimit":{"radius":250},"decodeTheRuins":["log-one"]}
            """
        );
        target.Write("data", file, """{"rccApiKey":"target-secret","explRewards":42}""");

        ProfileSnapshot snapshot = source.Store.Capture();
        target.Store.Apply(snapshot);

        JsonNode restored = Read(target.Path("data", file));
        Assert.Equal("Backup Pilot", restored["commander"]!.GetValue<string>());
        Assert.True(restored["autoResetExplorationValue"]!.GetValue<bool>());
        Assert.Equal("trip.json", restored["activeJourney"]!.GetValue<string>());
        Assert.True(restored["boxelSearch"]!["autoCopy"]!.GetValue<bool>());
        Assert.Equal(250, restored["sphereLimit"]!["radius"]!.GetValue<int>());
        Assert.Equal("log-one", restored["decodeTheRuins"]![0]!.GetValue<string>());
        Assert.Equal("target-secret", restored["rccApiKey"]!.GetValue<string>());
        Assert.Equal(42, restored["explRewards"]!.GetValue<int>());
        Assert.DoesNotContain("source-secret", Encoding.UTF8.GetString(snapshot.Serialize()));
        Assert.DoesNotContain("inara-secret", Encoding.UTF8.GetString(snapshot.Serialize()));
    }

    [Theory]
    [InlineData("vr/Supercruise.json")]
    [InlineData("commanders/F123/overlays/vr/vdxr/Default.json")]
    public void StartupAndVrConfigurationCanOnlyBeRecoveredOnTheirOriginalMachine(string calibration)
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        const string original =
            """{"CommanderPreference":{"PreferredFrontierId":"F123"},"Journal":{"Directories":["/original/journals"]}}""";
        const string other =
            """{"CommanderPreference":{"PreferredFrontierId":"F456"},"Journal":{"Directories":["/other/journals"]}}""";
        source.Write("config", "cross-platform-ui.json", original);
        source.Write("data", calibration, """{"Panel":"original calibration"}""");
        target.Write("config", "cross-platform-ui.json", other);
        target.Write("data", calibration, """{"Panel":"other calibration"}""");
        ProfileSnapshot snapshot = source.Store.Capture();

        target.Store.Apply(snapshot);

        Assert.Equal(
            "F456",
            Read(target.Paths.SharedUiSettingsPath)["CommanderPreference"]!["PreferredFrontierId"]!.GetValue<string>()
        );
        Assert.Equal("other calibration", Read(target.Path("data", calibration))["Panel"]!.GetValue<string>());
        source.Write("config", "cross-platform-ui.json", other);
        source.Write("data", calibration, """{"Panel":"changed calibration"}""");
        source.Store.Apply(snapshot, restoreMachine: true);
        Assert.Equal(
            "F123",
            Read(source.Paths.SharedUiSettingsPath)["CommanderPreference"]!["PreferredFrontierId"]!.GetValue<string>()
        );
        Assert.Equal(
            "/original/journals",
            Read(source.Paths.SharedUiSettingsPath)["Journal"]!["Directories"]![0]!.GetValue<string>()
        );
        Assert.Equal("original calibration", Read(source.Path("data", calibration))["Panel"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => target.Store.Apply(snapshot, restoreMachine: true));
    }

    [Fact]
    public void LocalQuestAndEmptyBoxelDataRestoreWithoutIncludingTheirRecoveryArchives()
    {
        using var source = new ProfileSyncTestRoot();
        using var target = new ProfileSyncTestRoot();
        source.Write("data", "quests/F123.json", """{"devRef":"owner:example","devQuest":{"chapters":["started"]}}""");
        source.Write("data", "quests/dev-example.json", """{"id":"example","title":"My local quest"}""");
        source.Write("data", "emptyBoxels/example.json", """["example AA-A c1"]""");
        source.Write("data", "quests/quest-state-backups/old.json", """{"old":"archive-marker"}""");

        ProfileSnapshot snapshot = source.Store.Capture();
        target.Store.Apply(snapshot);

        Assert.Equal(
            "My local quest",
            Read(target.Path("data", "quests/dev-example.json"))["title"]!.GetValue<string>()
        );
        Assert.Equal(
            "started",
            Read(target.Path("data", "quests/F123.json"))["devQuest"]!["chapters"]![0]!.GetValue<string>()
        );
        Assert.Equal("example AA-A c1", Read(target.Path("data", "emptyBoxels/example.json"))[0]!.GetValue<string>());
        Assert.DoesNotContain("archive-marker", Encoding.UTF8.GetString(snapshot.Serialize()));
    }

    private static JsonNode Read(string file) => JsonNode.Parse(File.ReadAllText(file))!;

    [Fact]
    public void BackupSkipsLinkedVrFoldersInsteadOfReadingExternalConfiguration()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", """{"Theme":"gold"}""");
        Directory.CreateDirectory(root.Paths.CacheDirectory);
        File.WriteAllText(Path.Combine(root.Paths.CacheDirectory, "private.json"), """{"secret":"external-secret"}""");
        Directory.CreateSymbolicLink(root.Path("data", "vr"), root.Paths.CacheDirectory);

        ProfileSnapshot snapshot = root.Store.Capture();

        Assert.Equal("gold", snapshot.Portable["config/cross-platform-ui.json#/Theme"].Value!.GetValue<string>());
        Assert.DoesNotContain("external-secret", Encoding.UTF8.GetString(snapshot.Serialize()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/rccApiKey")]
    [InlineData("/inaraApiKey")]
    [InlineData("/edsmApiKey")]
    [InlineData("/explRewards")]
    public void CommanderFileSupportDoesNotAuthorizeCredentialOrLiveTotalRestores(string jsonPath)
    {
        using var root = new ProfileSyncTestRoot();
        ProfileSnapshot invalid = root.Store.Capture() with
        {
            Portable = new()
            {
                ["data/F123-live.json#" + jsonPath] = new(JsonValue.Create("injected"), false, new() { ["A"] = 1 }),
            },
        };
        Assert.Throws<InvalidDataException>(() => root.Store.Apply(invalid));
    }
}
