using System.Text.Json;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class JournalSourceRegistryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-raven-sources-" + Guid.NewGuid().ToString("N")
    );

    /// <summary>Two distinct live commander bindings permit only their own isolated cargo folders.</summary>
    [Fact]
    public void SeparateSourcesRequireAllDetectedClients()
    {
        using var first = new JournalSourceRegistry(root);
        using var second = new JournalSourceRegistry(root);
        Assert.True(first.CanUseSource(1));
        Assert.False(first.CanUseSource(2));
        Assert.True(first.Bind(Journal("first"), "F1"));
        Assert.False(first.CanUseSource(2));
        Assert.True(second.Bind(Journal("second"), "F2"));
        Assert.True(first.CanUseSource(2));
        Assert.True(second.CanUseSource(2));
        Assert.False(first.CanUseSource(3));
        second.Dispose();
        Assert.False(first.CanUseSource(2));
    }

    /// <summary>A shared directory or duplicated commander never becomes a safe multi-client source.</summary>
    [Theory]
    [InlineData("first", "F2")]
    [InlineData("second", "f1")]
    public void SharedSourcesOrCommanderIdentitiesStayBlocked(string folder, string commander)
    {
        using var first = new JournalSourceRegistry(root);
        using var second = new JournalSourceRegistry(root);
        first.Bind(Journal("first"), "F1");
        second.Bind(Journal(folder), commander);
        Assert.False(first.CanUseSource(2));
    }

    /// <summary>Known peers still prevent shared-folder attribution when native window enumeration undercounts clients.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void RegisteredPeersRemainCheckedWhenWindowCountUnderreports(int windowCount, bool distinctFolders)
    {
        using var first = new JournalSourceRegistry(root);
        using var second = new JournalSourceRegistry(root);
        first.Bind(Journal("first"), "F1");
        second.Bind(Journal(distinctFolders ? "second" : "first"), "F2");
        Assert.Equal(distinctFolders, first.CanUseSource(windowCount));
        Assert.Equal(distinctFolders, second.CanUseSource(windowCount));
    }

    /// <summary>Directory aliases cannot count as independent writers.</summary>
    [Fact]
    public void DirectoryLinkIsTheSamePhysicalSource()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var first = new JournalSourceRegistry(root);
        using var second = new JournalSourceRegistry(root);
        string journal = Journal("first");
        string link = Path.Combine(root, "alias");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(journal)!);
        first.Bind(journal, "F1");
        second.Bind(Path.Combine(link, Path.GetFileName(journal)), "F2");
        Assert.False(first.CanUseSource(2));
    }

    /// <summary>Unreadable metadata fails closed, while crashed-process records do not act as live bindings.</summary>
    [Theory]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    [InlineData("{", false)]
    [InlineData("", false)]
    [InlineData("stale", true)]
    public void InvalidOrStalePeerRecordsDoNotAuthorizeCargo(string contents, bool expected)
    {
        using var first = new JournalSourceRegistry(root);
        using var second = new JournalSourceRegistry(root);
        first.Bind(Journal("first"), "F1");
        second.Bind(Journal("second"), "F2");
        using var malformedOwner = new FileStream(
            Path.Combine(root, nameof(JournalSourceRegistry), "unknown.json"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read
        );
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(
            contents == "stale"
                ? JsonSerializer.Serialize(
                    new
                    {
                        ProcessId = int.MaxValue,
                        StartTime = 1,
                        FrontierId = "F3",
                        Directory = root,
                    }
                )
                : contents
        );
        malformedOwner.Write(payload);
        malformedOwner.Flush();
        Assert.Equal(expected, first.CanUseSource(2));
    }

    /// <summary>Commander loss retires the old binding rather than reusing its source.</summary>
    [Fact]
    public void MissingIdentityReleasesTheBinding()
    {
        using var registry = new JournalSourceRegistry(root);
        Assert.True(registry.Bind(Journal("first"), "F1"));
        Assert.True(registry.Bind(Journal("first"), "F1"));
        Assert.False(registry.Bind(null, "F1"));
        Assert.False(registry.Bind(Journal("first"), null));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, nameof(JournalSourceRegistry)), "*.json"));
    }

    /// <summary>Exclusive per-commander leases block concurrent claims, ignore unlocked stale files, and support atomic handoff.</summary>
    [Fact]
    public void CommanderClaimsEnforceExclusiveOwnershipAndAtomicSwitchHandoff()
    {
        using var first = new CommanderClaimRegistry(root);
        using var second = new CommanderClaimRegistry(root);
        Assert.False(first.IsClaimedByOtherInstance("F13228986"));
        Assert.False(first.IsClaimedByOtherInstance(" "));
        Assert.False(first.TryReserve(" "));

        Assert.True(first.Claim("f13228986"));
        Assert.True(first.Claim("F13228986"));
        Assert.False(first.IsClaimedByOtherInstance("F13228986"));
        Assert.True(second.IsClaimedByOtherInstance("F13228986"));
        Assert.False(second.Claim("F13228986"));
        Assert.False(second.TryReserve("F13228986"));

        // An unlocked leftover claim file from a crashed process does not block ownership.
        string claimsDir = Path.Combine(root, "CommanderClaims");
        File.WriteAllText(Path.Combine(claimsDir, "F888.lock"), "{}");
        Assert.False(first.IsClaimedByOtherInstance("F888"));
        Assert.True(second.Claim("F888"));
        Assert.True(first.IsClaimedByOtherInstance("F888"));

        // Releasing F888 frees it immediately while unsafe names hash safely within CommanderClaims.
        Assert.False(second.Claim(null));
        Assert.False(first.IsClaimedByOtherInstance("F888"));
        Assert.True(second.Claim("../unsafe/id"));
        Assert.True(first.IsClaimedByOtherInstance("../unsafe/id"));
        Assert.False(second.Claim(null));

        // Reserving F14004033 holds BOTH commanders exclusively until the outgoing registry detaches and disposes.
        Assert.True(first.TryReserve("F14004033"));
        Assert.True(first.TryReserve("F14004033"));
        Assert.True(first.TryReserve("F13228986"));
        Assert.False(first.IsClaimedByOtherInstance("F14004033"));
        Assert.True(second.IsClaimedByOtherInstance("F14004033"));
        Assert.False(second.Claim("F14004033"));

        // Mismatched detach disposes the reserved lease; matching detach preserves the OS lock across outgoing disposal.
        Assert.Null(first.DetachReservedLease("F99999999"));
        Assert.False(second.IsClaimedByOtherInstance("F14004033"));
        Assert.True(first.TryReserve("F14004033"));
        using CommanderClaimLease? handoff = first.DetachReservedLease("F14004033");
        Assert.NotNull(handoff);
        first.Dispose();

        // Outgoing F13228986 is now free, while F14004033 remains exclusively locked until the replacement adopts and disposes it.
        Assert.False(second.IsClaimedByOtherInstance("F13228986"));
        Assert.True(second.IsClaimedByOtherInstance("F14004033"));
        Assert.False(second.Claim("F14004033"));

        using var replacement = new CommanderClaimRegistry(root);
        replacement.Adopt(handoff);
        replacement.Adopt(handoff);
        Assert.False(replacement.IsClaimedByOtherInstance("F14004033"));
        Assert.True(second.IsClaimedByOtherInstance("F14004033"));

        // Promoting a reserved lease via Claim switches ownership without dropping the lease.
        Assert.True(replacement.TryReserve("F77777777"));
        Assert.True(replacement.Claim("F77777777"));
        Assert.False(second.IsClaimedByOtherInstance("F14004033"));
        Assert.True(second.IsClaimedByOtherInstance("F77777777"));
    }

    /// <summary>Missing lease paths report no live lease while an active exclusive handle blocks peers.</summary>
    [Fact]
    public void CommanderClaimsIgnoreMissingPathsAndDetectExternalExclusiveHandles()
    {
        Assert.False(ProcessLeaseFile.HasLiveLease(Path.Combine(root, "does-not-exist.lock")));

        using var first = new CommanderClaimRegistry(root);
        Assert.True(first.Claim("F13228986"));
        string claimsDir = Path.Combine(root, "CommanderClaims");
        string peerPath = Path.Combine(claimsDir, "F14004033.lock");
        using (var externalLock = new FileStream(peerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(first.IsClaimedByOtherInstance("F14004033"));
            Assert.False(first.TryReserve("F14004033"));
        }

        Assert.False(first.IsClaimedByOtherInstance("F14004033"));
        Assert.True(first.TryReserve("F14004033"));
    }

    /// <summary>Confirmed commander checkpoints persist monotonically per journal path and ignore malformed files.</summary>
    [Fact]
    public void CommanderJournalCheckpointStorePersistsConfirmedOffsetsAndIgnoresMalformedFiles()
    {
        Assert.Empty(CommanderJournalCheckpointStore.TryLoad(root, null));
        Assert.Empty(CommanderJournalCheckpointStore.TryLoad(root, "F14004033"));
        Assert.False(
            CommanderJournalCheckpointStore.TrySave(
                root,
                " ",
                new SrvSurvey.Core.Journal.JournalCheckpoint(Journal("first"), 100)
            )
        );
        Assert.False(
            CommanderJournalCheckpointStore.TrySave(
                root,
                "F14004033",
                new SrvSurvey.Core.Journal.JournalCheckpoint(Journal("first"), 0)
            )
        );

        string firstJournal = Journal("first");
        string secondJournal = Journal("second");
        Assert.True(
            CommanderJournalCheckpointStore.TrySave(
                root,
                "f14004033",
                new SrvSurvey.Core.Journal.JournalCheckpoint(firstJournal, 512)
            )
        );
        // Smaller offset must never regress a previously confirmed checkpoint for the same journal.
        Assert.True(
            CommanderJournalCheckpointStore.TrySave(
                root,
                "F14004033",
                new SrvSurvey.Core.Journal.JournalCheckpoint(firstJournal, 256)
            )
        );
        Assert.True(
            CommanderJournalCheckpointStore.TrySave(
                root,
                "F14004033",
                new SrvSurvey.Core.Journal.JournalCheckpoint(secondJournal, 1024)
            )
        );

        IReadOnlyDictionary<string, long> loaded = CommanderJournalCheckpointStore.TryLoad(root, "F14004033");
        Assert.Equal(2, loaded.Count);
        Assert.Equal(512, loaded[firstJournal]);
        Assert.Equal(1024, loaded[secondJournal]);

        // Corrupted checkpoint files fail closed to an empty map without throwing.
        string corruptPath = Path.Combine(root, "CommanderClaims", "F999.checkpoint.json");
        File.WriteAllText(corruptPath, "{not-valid-json");
        Assert.Empty(CommanderJournalCheckpointStore.TryLoad(root, "F999"));
    }

    /// <summary>Creates a physical folder for a source fixture.</summary>
    private string Journal(string folder)
    {
        string path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        return Path.Combine(path, "Journal.2026-10-14T120000.01.log");
    }

    /// <summary>Removes isolated source metadata and alias fixtures.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
