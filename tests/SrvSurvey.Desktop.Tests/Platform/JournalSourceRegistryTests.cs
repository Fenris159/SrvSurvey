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
