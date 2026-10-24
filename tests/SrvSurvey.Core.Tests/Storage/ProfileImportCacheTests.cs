using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class ProfileImportCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-import-cache-tests-{Guid.NewGuid():N}");

    /// <summary>Verifies that downloaded image caches are excluded before hashing, backup, and activation.</summary>
    [Theory]
    [InlineData("codexImages")]
    [InlineData("CODEXIMAGES")]
    public async Task ImportSkipsDownloadedCodexImagesButKeepsCommanderDataAndLocalPhotos(string cacheName)
    {
        string source = Path.Combine(root, "source");
        string destination = Path.Combine(root, "current");
        Directory.CreateDirectory(Path.Combine(source, cacheName, "nested"));
        Directory.CreateDirectory(Path.Combine(source, "localFlora"));
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(source, cacheName, "nested", "2100201.jpg"), "downloaded reference");
        await File.WriteAllTextAsync(Path.Combine(source, "localFlora", "my-photo.jpg"), "personal photo");
        await using var lockedCache = new FileStream(
            Path.Combine(source, cacheName, "nested", "2100201.jpg"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None
        );

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(root, "backups")
        );

        Assert.Empty(result.Warnings);
        Assert.DoesNotContain(
            result.Manifest.Entries,
            entry => entry.RelativePath.StartsWith(cacheName, StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            result.Manifest.RelativeDirectories,
            path => path.StartsWith(cacheName, StringComparison.OrdinalIgnoreCase)
        );
        Assert.False(Directory.Exists(Path.Combine(destination, cacheName)));
        Assert.False(Directory.Exists(Path.Combine(result.BackupDirectory, "profile", cacheName)));
        Assert.False(Directory.Exists(Path.Combine(result.BackupDirectory, "import-payload", cacheName)));
        Assert.True(File.Exists(Path.Combine(source, cacheName, "nested", "2100201.jpg")));
        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(destination, "F123-live.json")));
        Assert.Equal(
            "personal photo",
            await File.ReadAllTextAsync(Path.Combine(destination, "localFlora", "my-photo.jpg"))
        );
    }

    /// <summary>Retains an existing destination cache because only the incoming download cache is excluded.</summary>
    [Fact]
    public async Task ImportPreservesCurrentImageCacheAndItsBackup()
    {
        string source = Path.Combine(root, "source");
        string destination = Path.Combine(root, "current");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(destination, "codexImages"));
        await File.WriteAllTextAsync(Path.Combine(source, "F123-live.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(destination, "codexImages", "saved.jpg"), "existing reference");

        ProfileImportResult result = await new LegacyProfileImporter().ImportAsync(
            source,
            destination,
            Path.Combine(root, "backups")
        );

        Assert.Equal(
            "existing reference",
            await File.ReadAllTextAsync(Path.Combine(destination, "codexImages", "saved.jpg"))
        );
        Assert.Equal(
            "existing reference",
            await File.ReadAllTextAsync(
                Path.Combine(result.BackupDirectory, "previous-destination", "codexImages", "saved.jpg")
            )
        );
        Assert.Contains(
            result.Manifest.PreviousDestinationEntries,
            entry => entry.RelativePath == "codexImages/saved.jpg"
        );
    }

    /// <summary>Removes only the synthetic profile used by this test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
