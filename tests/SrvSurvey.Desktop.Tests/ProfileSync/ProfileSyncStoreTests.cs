using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ProfileSync;
using Xunit;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ProfileSyncStoreTests
{
    /// <summary>Backup and restore preserve distinct commander scale, exception, and position keys across one shared installation.</summary>
    [Fact]
    public void BackupRestoreKeepsBothCommanderOverlaySettingsSeparate()
    {
        using var source = new ProfileSyncTestRoot();
        source.Write(
            "config",
            "commanders/F123/cross-platform-ui.json",
            """{"OverlayScale":{"Index":1033},"OverlayVehicleAllowLists":{"Exploration":["python"]}}"""
        );
        source.Write(
            "config",
            "commanders/F456/cross-platform-ui.json",
            """{"OverlayScale":{"Index":1020},"OverlayVehicleAllowLists":{"Exploration":["anaconda"]}}"""
        );
        source.Write("data", "commanders/F123/overlays/plotters.json", """{"PlotJumpInfo":"left:120, top:80"}""");
        source.Write("data", "commanders/F456/overlays/plotters.json", """{"PlotJumpInfo":"left:450, top:80"}""");
        using var destination = new ProfileSyncTestRoot();

        var restored = new ProfileSyncStore(destination.Paths, () => source.MachineIdentity);
        restored.Apply(source.Store.Capture(), restoreMachine: true);

        AppDataPaths first = destination.Paths with { SettingsFrontierId = "F123" };
        AppDataPaths second = destination.Paths with { SettingsFrontierId = "F456" };
        Assert.Equal(
            65,
            OverlayScaleCatalog.GetPercent(new OverlayScaleSettingsStore(first.UiSettingsPath).Load().Index)
        );
        Assert.Equal(
            0,
            OverlayScaleCatalog.GetPercent(new OverlayScaleSettingsStore(second.UiSettingsPath).Load().Index)
        );
        Assert.Equal(
            ["python"],
            new OverlayVehicleSettingsStore(first.UiSettingsPath).Load(OverlaySettingsCategory.Exploration)
        );
        Assert.Equal(
            ["anaconda"],
            new OverlayVehicleSettingsStore(second.UiSettingsPath).Load(OverlaySettingsCategory.Exploration)
        );
        Assert.Equal(
            120,
            new LegacyOverlayLayoutStore(first.OverlaySettingsDirectory)
                .Load()
                .Placements["PlotJumpInfo"]
                .HorizontalOffset
        );
        Assert.Equal(
            450,
            new LegacyOverlayLayoutStore(second.OverlaySettingsDirectory)
                .Load()
                .Placements["PlotJumpInfo"]
                .HorizontalOffset
        );
    }

    /// <summary>Profile backups retain growth preferences with machine-specific positions rather than portable preferences.</summary>
    [Fact]
    public void GrowthOverridesStayWithMachineSpecificOverlayPositions()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "overlay-growth-overrides.json", "{\"PlotJumpInfo\":\"left\"}");
        ProfileSnapshot snapshot = root.Store.Capture();
        string key = Key("data", "overlay-growth-overrides.json", "/PlotJumpInfo");
        Assert.DoesNotContain(key, snapshot.Portable.Keys);
        Assert.Contains(key, snapshot.Machine.Keys);
    }

    /// <summary>Releases layout leases before processing large files while retaining the coherent layout bytes already captured.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeProfileCaptureDoesNotBlockLayoutSaves(bool deleteLayout)
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "plotters.json", "{\"PlotJumpInfo\":\"left:10,top:20\"}");
        root.Write("data", "systems/large.json", "{" + new string(' ', 1024 * 1024));
        bool inspected = false;
        var store = new ProfileSyncStore(
            root.Paths,
            log: message =>
            {
                if (!message.Contains("systems", StringComparison.Ordinal))
                {
                    return;
                }
                using FileStream lease = OverlayLayoutFileAccess.Acquire(root.Paths.DataDirectory);
                inspected = true;
                if (deleteLayout)
                {
                    File.Delete(root.Path("data", "plotters.json"));
                }
                else
                {
                    root.Write("data", "plotters.json", "{\"PlotJumpInfo\":\"left:999,top:999\"}");
                }
            }
        );

        ProfileSnapshot snapshot = store.Capture();

        Assert.True(inspected);
        Assert.Equal(
            "left:10,top:20",
            snapshot.Machine[Key("data", "plotters.json", "/PlotJumpInfo")]!.GetValue<string>()
        );
    }

    /// <summary>Verifies that restore conflicts cannot bypass an untrusted pending snapshot's machine identity.</summary>
    [Fact]
    public void PendingRestoreValidatesHardwareIdentityBeforeOfferingConflictChoices()
    {
        using var remote = new ProfileSyncTestRoot();
        remote.Write("config", "cross-platform-ui.json", "{\"Theme\":\"remote\"}");
        remote.Write("data", "plotters.json", "{\"Panel\":\"left:10,top:20\"}");
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"local\"}");
        File.WriteAllBytes(root.Store.PendingPath, remote.Store.Capture().Serialize());

        Assert.Throws<InvalidOperationException>(() => root.Store.ApplyPending());

        Assert.Contains("local", File.ReadAllText(root.Paths.UiSettingsPath));
        Assert.False(File.Exists(root.Path("data", "plotters.json")));
    }

    /// <summary>Verifies that malformed files retain baseline values and deletions while other files are captured.</summary>
    [Fact]
    public void MalformedFilesRetainBaselineValuesAndDeletionsWhileOtherFilesAreCaptured()
    {
        var messages = new List<string>();
        using var root = new ProfileSyncTestRoot(messages.Add);
        root.Write("data", "systems/example.json", "{\"Name\":\"Sol\",\"Removed\":true}");
        root.Store.SaveBaseline(root.Store.Capture());
        root.Write("data", "systems/example.json", "{\"Name\":\"Sol\"}");
        ProfileSnapshot baseline = root.Store.Capture();
        root.Store.SaveBaseline(baseline);
        root.Write("data", "systems/example.json", "{truncated");
        root.Write("data", "systems/new.json", "{malformed");
        root.Write("data", "theme.json", "{\"orange\":\"#abc\"}");

        ProfileSnapshot captured = root.Store.Capture();

        foreach ((string key, SyncValue value) in baseline.Portable)
        {
            Assert.Equal(value.Deleted, captured.Portable[key].Deleted);
            Assert.True(JsonNode.DeepEquals(value.Value, captured.Portable[key].Value));
            Assert.Equal(value.Clock, captured.Portable[key].Clock);
        }
        Assert.Contains(Key("data", "theme.json", "/orange"), captured.Portable.Keys);
        Assert.DoesNotContain(
            captured.Portable.Keys,
            key => key.Contains("systems/new.json", StringComparison.Ordinal)
        );
        Assert.Contains(
            messages,
            message => message.Contains(root.Path("data", "systems/example.json"), StringComparison.Ordinal)
        );
        Assert.Contains(
            messages,
            message => message.Contains(root.Path("data", "systems/new.json"), StringComparison.Ordinal)
        );
        root.Store.SaveBackup(captured);
    }

    /// <summary>Verifies that high volume capture reports the portable key limit before saving.</summary>
    [Fact]
    public void HighVolumeCaptureReportsThePortableKeyLimitBeforeSaving()
    {
        using var root = new ProfileSyncTestRoot();
        var content = new JsonObject();
        for (int index = 0; index <= 100_000; index++)
        {
            content[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = index;
        }
        root.Write("data", "systems/large.json", content.ToJsonString());

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => root.Store.Capture());

        Assert.Contains("100000", failure.Message);
        Assert.Contains("portable", failure.Message);
        Assert.Contains(root.Path("data", "systems/large.json"), failure.Message);
        Assert.False(Directory.Exists(System.IO.Path.Combine(root.Store.StateDirectory, "backups")));
    }

    /// <summary>Verifies that deleted history counts toward the portable limit and the limit itself is allowed.</summary>
    [Fact]
    public void DeletedHistoryCountsTowardThePortableLimitAndTheLimitItselfIsAllowed()
    {
        using var root = new ProfileSyncTestRoot();
        var content = new JsonObject();
        for (int index = 0; index < ProfileSnapshot.MaximumValues; index++)
        {
            content[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = index;
        }
        root.Write("data", "systems/large.json", content.ToJsonString());
        ProfileSnapshot baseline = root.Store.Capture();
        Assert.Equal(ProfileSnapshot.MaximumValues, baseline.Portable.Count);
        root.Store.SaveBaseline(baseline);
        File.Delete(root.Path("data", "systems/large.json"));
        root.Write("data", "theme.json", "{\"orange\":\"#abc\"}");

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => root.Store.Capture());

        Assert.Contains("100000", failure.Message);
        Assert.Contains("deletion history", failure.Message);
    }

    /// <summary>Verifies that excessive machine values have a clear capture limit.</summary>
    [Fact]
    public void ExcessiveMachineValuesHaveAClearCaptureLimit()
    {
        using var root = new ProfileSyncTestRoot();
        var content = new JsonObject();
        for (int index = 0; index <= ProfileSnapshot.MaximumValues; index++)
        {
            content[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = index;
        }
        root.Write("data", "plotters.json", content.ToJsonString());

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => root.Store.Capture());

        Assert.Contains("machine", failure.Message);
        Assert.Contains("plotters.json", failure.Message);
    }

    /// <summary>Verifies that pending restore preserves edits to values that were not changed by the restore.</summary>
    [Fact]
    public void PendingRestorePreservesEditsToValuesThatWereNotChangedByTheRestore()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"old\",\"Workspace\":{\"Navigation\":\"travel\"}}");
        ProfileSnapshot old = root.Store.Capture();
        root.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"current\",\"Workspace\":{\"Navigation\":\"travel\"}}"
        );
        root.Store.StageExplicitRestore(old, false);
        root.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"current\",\"Workspace\":{\"Navigation\":\"mining\"}}"
        );

        Assert.True(root.Store.ApplyPending());

        JsonNode settings = JsonNode.Parse(File.ReadAllText(root.Paths.UiSettingsPath))!;
        Assert.Equal("old", settings["Theme"]!.GetValue<string>());
        Assert.Equal("mining", settings["Workspace"]!["Navigation"]!.GetValue<string>());
        Assert.Equal(
            "mining",
            root.Store.LoadBaseline()!
                .Portable[Key("config", "cross-platform-ui.json", "/Workspace/Navigation")]
                .Value!.GetValue<string>()
        );
    }

    /// <summary>Verifies that capture separates machine settings and excludes secrets and operational state.</summary>
    [Fact]
    public void CaptureSeparatesMachineSettingsAndExcludesSecretsAndOperationalState()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write(
            "config",
            "cross-platform-ui.json",
            """
            {"Version":1,"Theme":"blue-dark","OverlayBehavior":{"KeepWhenGameLosesFocus":true,"PreferredMonitor":"DP-1","BypassWindowManagement":true},
             "WaylandCapture":{"Enabled":true},"SurfaceMining":{"Detection":{"Width":100},"AutoClearRigsOnShipBoarding":true},
             "Input":{"Bindings":{"map":"ALT M"},"ControllerDeviceId":"device"},
             "Colonization":{"Enabled":true,"ApiKey":"secret","PendingContributions":[1]},"Inara":{"ApiKey":"secret"},"Journal":{"Directories":["/journal"]}}
            """
        );
        root.Write("data", "bookmarks.json", "[{\"Name\":\"Sol\",\"Screenshots\":[\"/local/image.png\"]}]");
        root.Write("data", "frontier-auth.json", "{\"token\":\"secret\"}");
        root.Write("data", "eddn-outbox-v1.json", "{}");
        root.Write("data", "theme.json", "{\"orange\":\"#ff0000\"}");
        root.Write("data", "plotters.json", "{\"Panel\":\"left:10,top:20\"}");
        root.Write("data", "mining-provider-cache/anything.json", "{}");
        ProfileSnapshot snapshot = root.Store.Capture();
        string text = Encoding.UTF8.GetString(snapshot.Serialize());
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("/journal", text);
        Assert.DoesNotContain("/local/image.png", text);
        Assert.DoesNotContain("eddn-outbox", text);
        Assert.Contains(Key("config", "cross-platform-ui.json", "/Theme"), snapshot.Portable.Keys);
        Assert.Contains(Key("config", "cross-platform-ui.json", "/Input/Bindings/map"), snapshot.Portable.Keys);
        Assert.Contains(Key("config", "cross-platform-ui.json", "/WaylandCapture/Enabled"), snapshot.Machine.Keys);
        Assert.Contains(Key("data", "plotters.json", "/Panel"), snapshot.Machine.Keys);
        ProfileSyncStore.ValidateScope(snapshot);
    }

    /// <summary>Verifies that restore merges portable preferences and preserves machine and credential fields.</summary>
    [Fact]
    public void RestoreMergesPortablePreferencesAndPreservesMachineAndCredentialFields()
    {
        using var first = new ProfileSyncTestRoot();
        first.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"gold\",\"OverlayBehavior\":{\"PreferredMonitor\":\"A\",\"KeepWhenGameLosesFocus\":true}}"
        );
        first.Write("data", "bookmarks.json", "[{\"Name\":\"Sol\"}]");
        using var second = new ProfileSyncTestRoot();
        second.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"blue\",\"OverlayBehavior\":{\"PreferredMonitor\":\"B\"},\"Inara\":{\"ApiKey\":\"private\"}}"
        );
        second.Store.Apply(first.Store.Capture());
        JsonNode settings = JsonNode.Parse(File.ReadAllText(second.Path("config", "cross-platform-ui.json")))!;
        Assert.Equal("gold", settings["Theme"]!.GetValue<string>());
        Assert.Equal("B", settings["OverlayBehavior"]!["PreferredMonitor"]!.GetValue<string>());
        Assert.Equal("private", settings["Inara"]!["ApiKey"]!.GetValue<string>());
        Assert.True(File.Exists(second.Path("data", "bookmarks.json")));
        Assert.Single(
            Directory.EnumerateFiles(
                System.IO.Path.Combine(second.Store.StateDirectory, "restore-backups"),
                "manifest.json",
                SearchOption.AllDirectories
            )
        );
    }

    /// <summary>Verifies that hardware restore requires original machine and operating system.</summary>
    [Fact]
    public void HardwareRestoreRequiresOriginalMachineAndOperatingSystem()
    {
        using var first = new ProfileSyncTestRoot();
        first.Write("data", "plotters.json", "{\"Panel\":\"left:10,top:20\"}");
        ProfileSnapshot snapshot = first.Store.Capture();
        using var second = new ProfileSyncTestRoot();
        Assert.Throws<InvalidOperationException>(() => second.Store.Apply(snapshot, restoreMachine: true));
        Assert.Throws<InvalidOperationException>(() => second.Store.StageRestore(snapshot, restoreMachine: true));
        Assert.Throws<InvalidOperationException>(() =>
            first.Store.Apply(snapshot with { Platform = "Other" }, restoreMachine: true)
        );
        first.Write("data", "plotters.json", "{\"Panel\":\"left:90,top:90\"}");
        first.Store.StageRestore(snapshot, restoreMachine: true);
        Assert.True(first.Store.ApplyPending());
        Assert.Contains("left:10", File.ReadAllText(first.Path("data", "plotters.json")));
        Assert.False(first.Store.ApplyPending());
        Assert.NotNull(first.Store.LoadBaseline());
    }

    /// <summary>Verifies that same machine can recover hardware after its local sync identity is lost.</summary>
    [Fact]
    public void SameMachineCanRecoverHardwareAfterItsLocalSyncIdentityIsLost()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "plotters.json", "{\"Panel\":\"left:10,top:20\"}");
        var snapshot = ProfileSnapshot.Parse(root.Store.Capture().Serialize());
        Directory.Delete(root.Store.StateDirectory, recursive: true);
        var reinstalled = new ProfileSyncStore(root.Paths, () => root.MachineIdentity);
        Assert.NotEqual(snapshot.DeviceId, reinstalled.Device.Id);
        Assert.Equal(snapshot.MachineFingerprint, reinstalled.Device.MachineHash);
        root.Write("data", "plotters.json", "{\"Panel\":\"left:90,top:90\"}");
        reinstalled.StageExplicitRestore(snapshot, restoreMachine: true);
        Assert.True(reinstalled.ApplyPending());
        Assert.Contains("left:10", File.ReadAllText(root.Path("data", "plotters.json")));
    }

    /// <summary>Verifies that missing OS identity does not authorize hardware restore on another installation.</summary>
    [Fact]
    public void MissingOsIdentityDoesNotAuthorizeHardwareRestoreOnAnotherInstallation()
    {
        using var first = new ProfileSyncTestRoot();
        using var second = new ProfileSyncTestRoot();
        var source = new ProfileSyncStore(first.Paths, () => "");
        var destination = new ProfileSyncStore(second.Paths, () => "");
        first.Write("data", "plotters.json", "{\"Panel\":\"left:10,top:20\"}");
        ProfileSnapshot snapshot = source.Capture();
        Assert.Empty(snapshot.MachineFingerprint);
        Assert.Throws<InvalidOperationException>(() => destination.StageRestore(snapshot, restoreMachine: true));
        source.StageRestore(snapshot, restoreMachine: true);
        Assert.True(source.ApplyPending());
    }

    /// <summary>Verifies that portable pending restore can be applied on another machine.</summary>
    [Fact]
    public void PortablePendingRestoreCanBeAppliedOnAnotherMachine()
    {
        using var first = new ProfileSyncTestRoot();
        first.Write("data", "theme.json", "{\"orange\":\"#abc\"}");
        using var second = new ProfileSyncTestRoot();
        second.Store.StageRestore(first.Store.Capture(), restoreMachine: false);
        Assert.True(second.Store.ApplyPending());
        Assert.Contains("#abc", File.ReadAllText(second.Path("data", "theme.json")));
    }

    /// <summary>Verifies that untrusted backup cannot write outside allowed files.</summary>
    [Theory]
    [InlineData("../bookmarks.json")]
    [InlineData("/bookmarks.json")]
    [InlineData("a/../../bookmarks.json")]
    [InlineData("C:/bookmarks.json")]
    [InlineData("a\\bookmarks.json")]
    [InlineData("frontier-auth.json")]
    [InlineData("mining/attachments/file.json")]
    [InlineData("routes/.trash/deleted.json")]
    [InlineData("logs/log.json")]
    [InlineData("bookmarks.txt")]
    public void UntrustedBackupCannotWriteOutsideAllowedFiles(string relative)
    {
        using var root = new ProfileSyncTestRoot();
        ProfileSnapshot malicious = root.Store.Capture() with
        {
            Portable = new()
            {
                [Key("data", relative, "")] = new(JsonValue.Create("unsafe"), false, new() { ["A"] = 1 }),
            },
        };
        Assert.Throws<InvalidDataException>(() => root.Store.Apply(malicious));
    }

    /// <summary>Verifies that keys must match their declared scope.</summary>
    [Theory]
    [InlineData("invalid")]
    [InlineData("data/#")]
    [InlineData("config/cross-platform-ui.json#/Inara/ApiKey")]
    [InlineData("config/cross-platform-ui.json#/WaylandCapture/Enabled")]
    [InlineData("config/cross-platform-ui.json#Theme")]
    [InlineData("config/cross-platform-ui.json#/Theme//value")]
    public void KeysMustMatchTheirDeclaredScope(string key)
    {
        Assert.Throws<InvalidDataException>(() => ProfileSyncPolicy.ParseKey(key, machine: false));
    }

    /// <summary>Verifies that deleted values remove existing fields and files.</summary>
    [Fact]
    public void DeletedValuesRemoveExistingFieldsAndFiles()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "theme.json", "{\"orange\":\"#abc\"}");
        root.Write("data", "bookmarks.json", "[]");
        ProfileSnapshot snapshot = root.Store.Capture();
        root.Store.SaveBaseline(snapshot);
        File.Delete(root.Path("data", "theme.json"));
        File.Delete(root.Path("data", "bookmarks.json"));
        ProfileSnapshot deleted = root.Store.Capture();
        root.Write("data", "theme.json", "{\"orange\":\"#old\"}");
        root.Write("data", "bookmarks.json", "[]");
        root.Store.Apply(deleted);
        Assert.False(File.Exists(root.Path("data", "theme.json")));
        Assert.False(File.Exists(root.Path("data", "bookmarks.json")));
    }

    /// <summary>Verifies that deleted saved routes stay absent instead of returning as empty catalog entries.</summary>
    [Fact]
    public async Task DeletedSavedRoutesStayAbsentInsteadOfReturningAsEmptyCatalogEntries()
    {
        using var root = new ProfileSyncTestRoot();
        string relative = "Routes/F123/Sol.json";
        const string content = "{\"name\":\"Sol\",\"hops\":[],\"details\":{\"notes\":\"mine\"}}";
        root.Write("data", relative, content);
        var routes = new SrvSurvey.Core.Routes.FollowRouteStore(root.Paths.DataDirectory);
        Assert.Single(await routes.ListAsync("F123"));
        root.Store.SaveBaseline(root.Store.Capture());
        File.Delete(root.Path("data", relative));
        ProfileSnapshot deleted = root.Store.Capture();
        root.Write("data", relative, content);
        root.Store.Apply(deleted);
        Assert.False(File.Exists(root.Path("data", relative)));
        root.Store.Apply(deleted);
        Assert.False(File.Exists(root.Path("data", relative)));
        Assert.Empty(await routes.ListAsync("F123"));
    }

    /// <summary>Verifies that device identity and preferences persist and backup history is bounded.</summary>
    [Fact]
    public void DeviceIdentityAndPreferencesPersistAndBackupHistoryIsBounded()
    {
        using var root = new ProfileSyncTestRoot();
        ProfileSyncDevice identity = root.Store.Device;
        var reopened = new ProfileSyncStore(root.Paths, () => root.MachineIdentity);
        Assert.Equal(identity, reopened.Device);
        Assert.False(root.Store.LoadPreferences().Automatic);
        root.Store.SavePreferences(new(true, "client", "secret"));
        Assert.Equal("client", root.Store.LoadPreferences().ClientId);
        for (int index = 0; index < 12; index++)
        {
            root.Store.SaveBackup(root.Store.Capture());
        }

        Assert.Equal(
            10,
            Directory.EnumerateFiles(System.IO.Path.Combine(root.Store.StateDirectory, "backups"), "*.json").Count()
        );
    }

    /// <summary>Verifies that JSON pointers handle property names and malformed files do not get overwritten.</summary>
    [Fact]
    public void JsonPointersHandlePropertyNamesAndMalformedFilesDoNotGetOverwritten()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "theme.json", "{/* comment */\"a/b~c\":\"#abc\",}");
        ProfileSnapshot snapshot = root.Store.Capture();
        Assert.Contains(Key("data", "theme.json", "/a~1b~0c"), snapshot.Portable.Keys);
        root.Store.Apply(snapshot);
        Assert.Equal(
            "#abc",
            JsonNode.Parse(
                File.ReadAllText(root.Path("data", "theme.json")),
                documentOptions: new()
                {
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }
            )!["a/b~c"]!.GetValue<string>()
        );
        root.Write("data", "theme.json", "{broken");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => root.Store.Apply(snapshot));
        Assert.Equal("{broken", File.ReadAllText(root.Path("data", "theme.json")));
    }

    /// <summary>Verifies that capture and restore refuse symbolic links.</summary>
    [Fact]
    public void CaptureAndRestoreRefuseSymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new ProfileSyncTestRoot();
        using var external = new ProfileSyncTestRoot();
        external.Write("data", "bookmarks.json", "[]");
        File.CreateSymbolicLink(root.Path("data", "bookmarks.json"), external.Path("data", "bookmarks.json"));
        Assert.Empty(root.Store.Capture().Portable);
        Assert.Throws<InvalidDataException>(() => root.Store.Apply(external.Store.Capture()));
    }

    /// <summary>Verifies that an older explicit restore supersedes current counters and removes newer portable values.</summary>
    [Fact]
    public void AnOlderExplicitRestoreSupersedesCurrentCountersAndRemovesNewerPortableValues()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"old\"}");
        ProfileSnapshot old = root.Store.Capture();
        root.Store.SaveBaseline(old);
        root.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"new\",\"Workspace\":{\"Navigation\":\"mining\"},\"Inara\":{\"ApiKey\":\"retained\"}}"
        );
        ProfileSnapshot current = root.Store.Capture();
        root.Store.SaveBaseline(current);
        root.Store.StageExplicitRestore(old, false);
        Assert.True(root.Store.ApplyPending());
        ProfileSnapshot restored = root.Store.Capture();
        SyncValue theme = restored.Portable[Key("config", "cross-platform-ui.json", "/Theme")];
        Assert.Equal("old", theme.Value!.GetValue<string>());
        Assert.True(theme.Includes(current.Portable[Key("config", "cross-platform-ui.json", "/Theme")]));
        Assert.True(restored.Portable[Key("config", "cross-platform-ui.json", "/Workspace/Navigation")].Deleted);
        Assert.Contains("retained", File.ReadAllText(root.Paths.UiSettingsPath));
        Assert.Empty(ProfileSyncMerge.Merge(restored.Portable, current.Portable).Conflicts);
    }

    /// <summary>Verifies that search filters retain their plain text keys and commander scoped settings remain independent.</summary>
    [Fact]
    public void SearchFiltersRetainTheirPlainTextKeysAndCommanderScopedSettingsRemainIndependent()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "mining-search-cache/powerplay-last.txt", "filter-key");
        root.Write(
            "data",
            "mining-search-cache/powerplay-example.json",
            "{\"Version\":1,\"Value\":{\"Filters\":{\"Power\":\"A\"}}}"
        );
        root.Write("config", "commanders/F123/cross-platform-ui.json", "{\"Theme\":\"gold\"}");
        root.Write("data", "commanders/F123/overlays/theme.json", "{\"orange\":\"#abc\"}");
        ProfileSnapshot snapshot = root.Store.Capture();
        using var destination = new ProfileSyncTestRoot();
        destination.Store.Apply(snapshot);
        Assert.Equal(
            "filter-key",
            File.ReadAllText(destination.Path("data", "mining-search-cache/powerplay-last.txt"))
        );
        Assert.Contains("gold", File.ReadAllText(destination.Path("config", "commanders/F123/cross-platform-ui.json")));
        Assert.Contains("#abc", File.ReadAllText(destination.Path("data", "commanders/F123/overlays/theme.json")));
    }

    /// <summary>Verifies that malformed preferences and copied hardware identity do not reuse another machine's identity.</summary>
    [Fact]
    public void MalformedPreferencesAndCopiedHardwareIdentityDoNotReuseAnotherMachinesIdentity()
    {
        using var root = new ProfileSyncTestRoot();
        File.WriteAllText(System.IO.Path.Combine(root.Store.StateDirectory, "preferences.json"), "{broken");
        Assert.False(root.Store.LoadPreferences().Automatic);
        var copied = new ProfileSyncStore(root.Paths, () => "different-machine");
        Assert.NotEqual(root.Store.Device.Id, copied.Device.Id);
        File.WriteAllText(System.IO.Path.Combine(root.Store.StateDirectory, "device.json"), "broken");
        var recovered = new ProfileSyncStore(root.Paths, () => "different-machine");
        Assert.NotEmpty(recovered.Device.Id);
    }

    /// <summary>Verifies that an interrupted restore recovers the complete previous files before startup.</summary>
    [Fact]
    public void AnInterruptedRestoreRecoversTheCompletePreviousFilesBeforeStartup()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[{\"Name\":\"before\"}]");
        ProfileSnapshot before = root.Store.Capture();
        root.Write("data", "bookmarks.json", "[{\"Name\":\"before\"},{\"Name\":\"new\"}]");
        root.Store.Apply(before);
        string rollback = Directory
            .EnumerateDirectories(System.IO.Path.Combine(root.Store.StateDirectory, "restore-backups"))
            .Single();
        File.WriteAllText(
            System.IO.Path.Combine(root.Store.StateDirectory, "active-restore.json"),
            System.Text.Json.JsonSerializer.Serialize(System.IO.Path.GetFileName(rollback))
        );
        root.Write("data", "bookmarks.json", "[{\"Name\":\"partially restored\"}]");
        root.Store.RecoverInterruptedRestore();
        Assert.Contains("new", File.ReadAllText(root.Path("data", "bookmarks.json")));
        Assert.False(File.Exists(System.IO.Path.Combine(root.Store.StateDirectory, "active-restore.json")));
        root.Store.RecoverInterruptedRestore();
        File.WriteAllText(System.IO.Path.Combine(root.Store.StateDirectory, "active-restore.json"), "\"../escape\"");
        Assert.Throws<InvalidDataException>(root.Store.RecoverInterruptedRestore);
    }

    /// <summary>Verifies that directory at a file path cannot destroy existing data.</summary>
    [Fact]
    public void DirectoryAtAFilePathCannotDestroyExistingData()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[]");
        ProfileSnapshot snapshot = root.Store.Capture();
        using var destination = new ProfileSyncTestRoot();
        Directory.CreateDirectory(destination.Path("data", "bookmarks.json"));
        Assert.Throws<InvalidDataException>(() => destination.Store.Apply(snapshot));
        Assert.True(Directory.Exists(destination.Path("data", "bookmarks.json")));
    }

    /// <summary>Verifies that recovery works when Windows data is nested inside the configuration folder.</summary>
    [Fact]
    public void RecoveryWorksWhenWindowsDataIsNestedInsideTheConfigurationFolder()
    {
        using var root = new ProfileSyncTestRoot();
        AppDataPaths paths = root.Paths with
        {
            DataDirectory = System.IO.Path.Combine(root.Paths.ConfigDirectory, "cross-platform"),
        };
        Directory.CreateDirectory(paths.DataDirectory);
        string file = System.IO.Path.Combine(paths.DataDirectory, "bookmarks.json");
        File.WriteAllText(file, "[]");
        var store = new ProfileSyncStore(paths, () => "test-hardware");
        ProfileSnapshot before = store.Capture();
        File.WriteAllText(file, "[{\"Name\":\"retained\"}]");
        store.Apply(before);
        string rollback = Directory
            .EnumerateDirectories(System.IO.Path.Combine(store.StateDirectory, "restore-backups"))
            .Single();
        File.WriteAllText(
            System.IO.Path.Combine(store.StateDirectory, "active-restore.json"),
            System.Text.Json.JsonSerializer.Serialize(System.IO.Path.GetFileName(rollback))
        );
        store.RecoverInterruptedRestore();
        Assert.Contains("retained", File.ReadAllText(file));
    }

    /// <summary>Verifies that portable collection changes keep existing local screenshot links and journey counters.</summary>
    [Fact]
    public void PortableCollectionChangesKeepExistingLocalScreenshotLinksAndJourneyCounters()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write(
            "data",
            "bookmarks.json",
            "[{\"Id\":\"same\",\"Notes\":\"old\",\"Screenshots\":[\"/local/photo.png\"]},{\"Id\":\"other\",\"Screenshots\":[\"/other/photo.png\"]}]"
        );
        using var remote = new ProfileSyncTestRoot();
        remote.Write(
            "data",
            "bookmarks.json",
            "[{\"Id\":\"same\",\"Notes\":\"changed\",\"Screenshots\":[\"/remote/photo.png\"]}]"
        );
        remote.Write(
            "data",
            "journey/F123/example.json",
            "{\"counts\":{\"screenshots\":7},\"visits\":[{\"counts\":{\"screenshots\":3}}]}"
        );
        ProfileSnapshot snapshot = remote.Store.Capture();
        root.Store.Apply(snapshot);
        string bookmarks = File.ReadAllText(root.Path("data", "bookmarks.json"));
        Assert.Contains("/local/photo.png", bookmarks);
        Assert.DoesNotContain("/remote/photo.png", bookmarks);
        Assert.DoesNotContain("/other/photo.png", bookmarks);
        Assert.Contains("changed", bookmarks);
        JsonNode journey = JsonNode.Parse(File.ReadAllText(root.Path("data", "journey/F123/example.json")))!;
        Assert.Equal(7, journey["counts"]!["screenshots"]!.GetValue<int>());
        Assert.Equal(3, journey["visits"]![0]!["counts"]!["screenshots"]!.GetValue<int>());
        Assert.DoesNotContain(
            "/local/photo.png",
            System.Text.Encoding.UTF8.GetString(root.Store.Capture().Serialize())
        );
    }

    /// <summary>Verifies that empty containers do not introduce parent keys that can overwrite newer children.</summary>
    [Fact]
    public void EmptyContainersDoNotIntroduceParentKeysThatCanOverwriteNewerChildren()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Workspace\":{\"Navigation\":\"mining\"}}");
        root.Store.SaveBaseline(root.Store.Capture());
        root.Write("config", "cross-platform-ui.json", "{\"Workspace\":{}}");
        ProfileSnapshot deleted = root.Store.Capture();
        Assert.Single(deleted.Portable);
        Assert.True(deleted.Portable.Values.Single().Deleted);
        Assert.DoesNotContain(Key("config", "cross-platform-ui.json", "/Workspace"), deleted.Portable.Keys);
        root.Write("data", "surface-mining-survey-progress.json", "{\"Phase\":1,\"WaypointIndex\":3}");
        Assert.Contains(
            Key("data", "surface-mining-survey-progress.json", "/WaypointIndex"),
            root.Store.Capture().Portable.Keys
        );
    }

    /// <summary>Builds a scoped profile key using the production file and JSON-pointer encoding.</summary>
    private static string Key(string area, string relative, string pointer) =>
        ProfileSyncPolicy.Key(area, relative, pointer);
}

internal sealed class ProfileSyncTestRoot : IDisposable
{
    private readonly string root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "srv-profile-sync-tests",
        Guid.NewGuid().ToString("N")
    );

    /// <summary>Creates isolated configuration, data, and cache roots with a stable test machine identity.</summary>
    internal ProfileSyncTestRoot(Action<string>? log = null)
    {
        Paths = new(
            System.IO.Path.Combine(root, "config"),
            System.IO.Path.Combine(root, "data"),
            System.IO.Path.Combine(root, "cache"),
            []
        );
        Directory.CreateDirectory(Paths.DataDirectory);
        Store = new(Paths, () => MachineIdentity, log);
    }

    internal AppDataPaths Paths { get; }
    internal ProfileSyncStore Store { get; }
    internal string MachineIdentity => root;

    /// <summary>Resolves a fixture path within its root using the host platform's directory separators.</summary>
    internal string Path(string area, string relative) =>
        System.IO.Path.Combine(
            area == "config" ? Paths.ConfigDirectory : Paths.DataDirectory,
            relative.Replace('/', System.IO.Path.DirectorySeparatorChar)
        );

    /// <summary>Creates parent directories and writes the requested profile fixture.</summary>
    internal void Write(string area, string relative, string text)
    {
        string path = Path(area, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Removes all files belonging to the isolated profile fixture.</summary>
    public void Dispose() => Directory.Delete(root, recursive: true);
}
