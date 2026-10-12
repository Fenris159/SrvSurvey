using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

/// <summary>Exercises process leases, crash recovery, damaged-file repair, and concurrent field merging.</summary>
public sealed class OverlayLayoutRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-layout-recovery-" + Guid.NewGuid().ToString("N")
    );
    private static readonly LegacyOverlayPlacement Original = new(
        LegacyHorizontalAnchor.Left,
        10,
        LegacyVerticalAnchor.Top,
        20,
        .8
    );

    /// <summary>Creates an isolated layout folder.</summary>
    public OverlayLayoutRecoveryTests() => Directory.CreateDirectory(root);

    /// <summary>Removes all isolated files and retained recovery copies.</summary>
    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>Resolves a file in this fixture.</summary>
    private string FilePath(string name) => Path.Combine(root, name);

    /// <summary>Seeds one valid position and opacity.</summary>
    private LegacyOverlayLayoutStore Seed()
    {
        var store = new LegacyOverlayLayoutStore(root);
        store.Save(new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Original });
        return store;
    }

    /// <summary>A failed rollback preserves the original exception and journal for a later leased recovery.</summary>
    [Fact]
    public void RollbackFailureDoesNotReplaceOriginalSaveError()
    {
        LegacyOverlayLayoutStore store = Seed();
        var originalError = new InvalidDataException("Original verification failure");
        Exception? error = Record.Exception(
            (Action)(
                () =>
                {
                    using FileStream lease = OverlayLayoutFileAccess.Acquire(root);
                    using OverlayLayoutFileAccess.Transaction transaction = OverlayLayoutFileAccess.BeginTransaction(
                        root
                    );
                    File.Delete(FilePath("plotters.json"));
                    Directory.CreateDirectory(FilePath("plotters.json"));
                    throw originalError;
                }
            )
        );

        Assert.Same(originalError, error);
        Assert.True(File.Exists(FilePath("overlay-layout-pending.json")));
        Directory.Delete(FilePath("plotters.json"));
        Assert.Equal(Original, store.Load().Placements["PlotJumpInfo"]);
        Assert.False(File.Exists(FilePath("overlay-layout-pending.json")));
    }

    /// <summary>Rolls back only opacity, retaining unrelated changes even when settings did not exist at transaction start.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransactionPreservesUnrelatedSharedSettings(bool existed)
    {
        LegacyOverlayLayoutStore store = Seed();
        if (existed)
        {
            File.WriteAllText(FilePath("settings.json"), "{\"plotterOpacity\":55,\"theme\":\"original\"}");
        }
        using (FileStream lease = OverlayLayoutFileAccess.Acquire(root))
        using (OverlayLayoutFileAccess.Transaction transaction = OverlayLayoutFileAccess.BeginTransaction(root))
        {
            File.WriteAllText(
                FilePath("settings.json"),
                "{\"plotterOpacity\":90,\"theme\":\"updated\",\"newFlag\":true}"
            );
        }

        Assert.True(File.Exists(FilePath("settings.json")));
        JsonObject settings = JsonNode.Parse(File.ReadAllText(FilePath("settings.json")))!.AsObject();
        Assert.Equal("updated", settings["theme"]!.GetValue<string>());
        Assert.True(settings["newFlag"]!.GetValue<bool>());
        Assert.Equal(existed ? .55 : (double?)null, store.Load().DefaultOpacity);
    }

    /// <summary>Journals contain only the overlay-owned property from the shared settings file.</summary>
    [Fact]
    public void JournalExcludesUnrelatedSharedSettings()
    {
        Seed();
        File.WriteAllText(FilePath("settings.json"), "{\"plotterOpacity\":55,\"theme\":\"private setting\"}");
        using FileStream lease = OverlayLayoutFileAccess.Acquire(root);
        using OverlayLayoutFileAccess.Transaction transaction = OverlayLayoutFileAccess.BeginTransaction(root);

        Dictionary<string, byte[]?> originals = JsonSerializer.Deserialize<Dictionary<string, byte[]?>>(
            File.ReadAllBytes(FilePath("overlay-layout-pending.json"))
        )!;
        JsonObject settings = JsonNode.Parse(originals["settings.json"]!)!.AsObject();
        Assert.Single(settings);
        Assert.Equal(55, settings["plotterOpacity"]!.GetValue<int>());
    }

    /// <summary>Older whole-file journals also restore only opacity instead of overwriting newer shared preferences.</summary>
    [Fact]
    public void LegacyJournalPreservesNewerSharedSettings()
    {
        LegacyOverlayLayoutStore store = Seed();
        var originals = OverlayLayoutFileAccess
            .FileNames.Where(name => name != "settings.json")
            .ToDictionary(name => name, name => File.Exists(FilePath(name)) ? File.ReadAllBytes(FilePath(name)) : null);
        originals["settings.json"] = Encoding.UTF8.GetBytes("{\"plotterOpacity\":55,\"theme\":\"original\"}");
        File.WriteAllBytes(FilePath("overlay-layout-pending.json"), JsonSerializer.SerializeToUtf8Bytes(originals));
        File.WriteAllText(FilePath("settings.json"), "{\"plotterOpacity\":90,\"theme\":\"updated\"}");

        Assert.Equal(.55, store.Load().DefaultOpacity);
        JsonObject settings = JsonNode.Parse(File.ReadAllText(FilePath("settings.json")))!.AsObject();
        Assert.Equal("updated", settings["theme"]!.GetValue<string>());
    }

    /// <summary>Explicit opacity repair restores a valid field or defaults it without replacing unrelated settings.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpacityRepairPreservesUnrelatedSharedSettings(bool backup)
    {
        LegacyOverlayLayoutStore store = Seed();
        File.WriteAllText(FilePath("settings.json"), "{\"plotterOpacity\":\"bad\",\"theme\":\"current\"}");
        if (backup)
        {
            Directory.CreateDirectory(FilePath("overlay-layout-backups"));
            File.WriteAllText(
                FilePath("overlay-layout-backups/settings-old.json"),
                "{\"plotterOpacity\":55,\"theme\":\"old\"}"
            );
        }

        string preserved = store.RecoverInvalidLayout();

        Assert.True(File.Exists(FilePath("settings.json")));
        JsonObject settings = JsonNode.Parse(File.ReadAllText(FilePath("settings.json")))!.AsObject();
        Assert.Equal("current", settings["theme"]!.GetValue<string>());
        Assert.Equal(backup ? .55 : (double?)null, store.Load().DefaultOpacity);
        Assert.True(File.Exists(Path.Combine(preserved, "settings.json")));
    }

    /// <summary>Unreadable shared JSON remains intact because an overlay repair cannot reconstruct unrelated properties.</summary>
    [Fact]
    public void MalformedSharedSettingsAreNeverDeletedByOverlayRepair()
    {
        LegacyOverlayLayoutStore store = Seed();
        const string damaged = "{\"theme\":\"unfinished\"";
        File.WriteAllText(FilePath("settings.json"), damaged);

        Assert.ThrowsAny<JsonException>(() => store.RecoverInvalidLayout());
        Assert.Equal(damaged, File.ReadAllText(FilePath("settings.json")));
    }

    /// <summary>Emulates termination between layout replacements and verifies automatic byte-exact recovery on load.</summary>
    [Fact]
    public void InterruptedGenerationIsRecoveredBeforeLoading()
    {
        LegacyOverlayLayoutStore store = Seed();
        byte[] original = File.ReadAllBytes(FilePath("plotters.json"));
        using (FileStream lease = OverlayLayoutFileAccess.Acquire(root))
        {
            _ = OverlayLayoutFileAccess.BeginTransaction(root); // An abruptly terminated process never disposes this transaction.
            File.WriteAllText(FilePath("plotters.json"), "{\"PlotJumpInfo\":\"left:999,top:999\"}");
            File.WriteAllText(
                FilePath("overlay-size-overrides.json"),
                "{\"PlotJumpInfo\":{\"width\":999,\"height\":999}}"
            );
        }
        Assert.Equal(Original, store.Load().Placements["PlotJumpInfo"]);
        Assert.Equal(original, File.ReadAllBytes(FilePath("plotters.json")));
        Assert.False(File.Exists(FilePath("overlay-size-overrides.json")));
        Assert.False(File.Exists(FilePath("overlay-layout-pending.json")));
    }

    /// <summary>A failed operation restores originals while a committed generation survives disposal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransactionOnlyKeepsCommittedGeneration(bool commit)
    {
        Seed();
        using FileStream lease = OverlayLayoutFileAccess.Acquire(root);
        using (OverlayLayoutFileAccess.Transaction transaction = OverlayLayoutFileAccess.BeginTransaction(root))
        {
            File.WriteAllText(FilePath("plotters.json"), "new generation");
            if (commit)
            {
                transaction.Commit();
            }
        }
        Assert.Equal(commit, File.ReadAllText(FilePath("plotters.json")) == "new generation");
    }

    /// <summary>Refuses malformed or path-traversing recovery manifests without writing any referenced destination.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"../outside.json\":null}")]
    public void InvalidRecoveryManifestCannotModifyLayout(string content)
    {
        Seed();
        string original = File.ReadAllText(FilePath("plotters.json"));
        File.WriteAllText(FilePath("overlay-layout-pending.json"), content);
        Assert.Throws<InvalidDataException>(() => OverlayLayoutFileAccess.Recover(root));
        Assert.Equal(original, File.ReadAllText(FilePath("plotters.json")));
        Assert.NotNull(new LegacyOverlayLayoutStore(root).Load().Error);
        Assert.True(File.Exists(FilePath("overlay-layout-pending.json")));
    }

    /// <summary>Rejects oversized journals before deserializing them.</summary>
    [Fact]
    public void OversizedRecoveryManifestIsRejected()
    {
        using (FileStream stream = File.Create(FilePath("overlay-layout-pending.json")))
        {
            stream.SetLength(65 * 1024 * 1024);
        }
        Assert.Throws<InvalidDataException>(() => OverlayLayoutFileAccess.Recover(root));
    }

    /// <summary>A busy lease fails within its bounded wait, and acquisition works once the owner releases it.</summary>
    [Fact]
    public void BusyLeaseCanBeRetriedAfterRelease()
    {
        using (FileStream first = OverlayLayoutFileAccess.Acquire(root))
        {
            Assert.Throws<IOException>(() => OverlayLayoutFileAccess.Acquire(root));
        }
        using FileStream retried = OverlayLayoutFileAccess.Acquire(root);
        Assert.True(retried.CanWrite);
    }

    /// <summary>Releases earlier folder locks when acquiring a later folder fails.</summary>
    [Fact]
    public void MultipleFolderAcquisitionUnwindsOnFailure()
    {
        string first = FilePath("a");
        string blocked = FilePath("z");
        File.WriteAllText(blocked, "occupied by a file");
        Assert.Throws<IOException>(() =>
            OverlayLayoutFileAccess.AcquireForFiles([
                Path.Combine(first, "plotters.json"),
                Path.Combine(blocked, "plotters.json"),
            ])
        );
        using FileStream retried = OverlayLayoutFileAccess.Acquire(first);
        Assert.True(retried.CanWrite);
    }

    /// <summary>Repairs a damaged entry from a verified prior backup while preserving the damaged bytes.</summary>
    [Fact]
    public void RecoveryRestoresValidBackupAndPreservesDamagedOriginal()
    {
        LegacyOverlayLayoutStore store = Seed();
        store.Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = Original with { HorizontalOffset = 99 },
            }
        );
        const string damaged = "{\"UnusedPanel\":\"broken\"}";
        File.WriteAllText(FilePath("plotters.json"), damaged);
        string preserved = store.RecoverInvalidLayout();
        Assert.Equal(damaged, File.ReadAllText(Path.Combine(preserved, "plotters.json")));
        Assert.Equal(Original, store.Load().Placements["PlotJumpInfo"]);
    }

    /// <summary>Defaults only a damaged file with no valid backup; valid positions survive.</summary>
    [Fact]
    public void RecoveryDefaultsOnlyTheDamagedSidecar()
    {
        LegacyOverlayLayoutStore store = Seed();
        File.WriteAllText(FilePath("overlay-size-overrides.json"), "{\"UnusedPanel\":{\"width\":-1,\"height\":20}}");
        var vm = new OverlayLayoutSettingsViewModel(store, store.Load());
        Assert.True(vm.CanRecoverLayout);
        vm.RecoverCommand.Execute(null);
        Assert.False(vm.CanRecoverLayout);
        Assert.False(File.Exists(FilePath("overlay-size-overrides.json")));
        Assert.Equal(Original, store.Load().Placements["PlotJumpInfo"]);
        Assert.Contains("preserved", vm.StatusMessage);
    }

    /// <summary>Explicit repair quarantines a malformed transaction manifest rather than following untrusted paths.</summary>
    [Fact]
    public void RecoveryPreservesInvalidManifest()
    {
        LegacyOverlayLayoutStore store = Seed();
        File.WriteAllText(FilePath("overlay-layout-pending.json"), "{}");
        string preserved = store.RecoverInvalidLayout();
        Assert.Equal("{}", File.ReadAllText(Path.Combine(preserved, "overlay-layout-pending.json")));
        Assert.Null(store.Load().Error);
    }

    /// <summary>A failed commander initialization never silently redirects overlay writes to the shared profile.</summary>
    [Fact]
    public void CommanderInitializationFailureRetainsCommanderSaveDirectory()
    {
        var paths = new SrvSurvey.Core.Storage.AppDataPaths(
            FilePath("config"),
            FilePath("data"),
            FilePath("cache"),
            []
        );
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.SharedUiSettingsPath, "{}");
        File.WriteAllText(Path.Combine(paths.ConfigDirectory, "commanders"), "occupied by a file");
        var log = new SrvSurvey.Core.Diagnostics.ApplicationLogService(paths.DataDirectory);
        System.Reflection.MethodInfo method = typeof(SrvSurvey.Desktop.Runtime.DesktopRuntime).GetMethod(
            "PrepareCommanderSettings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        )!;
        SrvSurvey.Core.Storage.AppDataPaths actual = Assert.IsType<SrvSurvey.Core.Storage.AppDataPaths>(
            method.Invoke(null, [paths, "F123", false, null, log])
        );
        Assert.Equal("F123", actual.SettingsFrontierId);
        Assert.NotEqual(paths.DataDirectory, actual.OverlaySettingsDirectory);
        Assert.Contains(log.Entries, entry => entry.Contains("not be redirected", StringComparison.Ordinal));
    }

    /// <summary>Refuses same-field conflicts without overwriting either writer's data.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameFieldConflictDoesNotOverwriteNewerEdit(bool globalOpacity)
    {
        LegacyOverlayLayoutStore store = Seed();
        var session = new OverlayPositionEditSession(store.Load());
        if (globalOpacity)
        {
            session.SetDefaultOpacity(.5);
            store.Save(new Dictionary<string, LegacyOverlayPlacement>(), .25, updateDefaultOpacity: true);
        }
        else
        {
            session.SetOpacityOverride("PlotJumpInfo", .5);
            store.Save(
                new Dictionary<string, LegacyOverlayPlacement> { ["PlotJumpInfo"] = Original with { Opacity = .25 } }
            );
        }
        string original = File.ReadAllText(FilePath("plotters.json"));
        Assert.Throws<InvalidDataException>(() => store.SaveChanges(session));
        Assert.Equal(original, File.ReadAllText(FilePath("plotters.json")));
        Assert.Equal(.25, globalOpacity ? store.Load().DefaultOpacity : store.Load().GetOpacity("PlotJumpInfo"));
    }
}
