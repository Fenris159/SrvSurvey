using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class SavedBoxelSearchStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-SavedBoxelSearchStoreTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateUpdateListLoadAndDeletePreserveFullProgress()
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var initial = new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 3,
            LowMassCode = 'c',
            CompletedSystems = ["Praea Euq IL-P c5-0"],
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 3,
            },
        };

        var created = await store.CreateAsync(
            "F123",
            "My boxel search",
            "Initial notes",
            initial);
        var renamed = await store.RenameAsync(
            "F123",
            created.FileName,
            "Return later");
        var noted = await store.SaveNotesAsync(
            "F123",
            created.FileName,
            "Updated notes");
        var favorite = await store.SetFavoriteAsync(
            "F123",
            created.FileName,
            true);
        var updated = await store.SaveProgressAsync(
            "F123",
            created.FileName,
            initial with
            {
                CompletedSystems =
                [
                    "Praea Euq IL-P c5-0",
                    "Praea Euq IL-P c5-1",
                ],
            });

        var entries = await store.ListAsync("F123");
        var loaded = await store.LoadAsync("F123", created.FileName);

        Assert.Equal("Return later", renamed.Name);
        Assert.Equal("Updated notes", noted.Notes);
        Assert.True(favorite.IsFavorite);
        Assert.Equal(2, updated.Search.CompletedSystems.Count);
        var entry = Assert.Single(entries);
        Assert.Equal("Return later", entry.Name);
        Assert.Equal("Updated notes", entry.Notes);
        Assert.True(entry.IsFavorite);
        Assert.Equal(2, entry.CompletedSystems);
        Assert.Equal(3, entry.TotalSystems);
        Assert.Equal(top.Prefix, entry.TopBoxelPrefix);
        Assert.Equal('c', entry.LowMassCode);
        Assert.Contains(top.Prefix, entry.Prefixes);
        Assert.Equal(created.CreatedAt, loaded.CreatedAt);
        Assert.Equal(created.FileName, loaded.Search.SavedSearchFileName);

        var trashPath = await store.DeleteAsync("F123", created.FileName);

        Assert.False(File.Exists(created.FilePath));
        Assert.True(File.Exists(trashPath));
        Assert.Empty(await store.ListAsync("F123"));
    }

    [Fact]
    public async Task ListIgnoresDamagedEntryWithoutHidingValidSearches()
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var created = await store.CreateAsync(
            "F123",
            "Valid",
            null,
            new BoxelSearchSnapshot
            {
                TopBoxel = top,
                Current = top,
                CurrentCount = 1,
                ProgressByPrefix = new Dictionary<string, int>
                {
                    [top.Prefix] = 1,
                },
            });
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(created.FilePath)!, "broken.json"),
            "not json");

        var entry = Assert.Single(await store.ListAsync("F123"));

        Assert.Equal("Valid", entry.Name);
    }

    [Fact]
    public async Task LoadFallsBackToTopBoxelWhenCurrentIsOutsideSearch()
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var created = await store.CreateAsync(
            "F123",
            "Corrupted current",
            null,
            new BoxelSearchSnapshot
            {
                TopBoxel = top,
                Current = top,
                CurrentCount = 1,
            });
        var root = JsonNode.Parse(await File.ReadAllTextAsync(created.FilePath))!
            .AsObject();
        root["search"]!["currentBoxel"] = "Sol";
        await File.WriteAllTextAsync(created.FilePath, root.ToJsonString());

        var loaded = await store.LoadAsync("F123", created.FileName);

        Assert.Equal(top.Prefix, loaded.Search.Current?.Prefix);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
