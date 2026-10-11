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
