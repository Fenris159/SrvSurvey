using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class EmptyBoxelStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-empty-boxel-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SetEmptyUsesTheLegacyMassCodeGGroupAndIdFormat()
    {
        var boxel = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var group = boxel.Parent.Parent.Parent.Parent;
        var store = new EmptyBoxelStore(temporaryDirectory);

        Assert.Equal(store.GetFilePath(group), store.GetFilePath(boxel));
        Assert.True(await store.SetEmptyAsync(boxel, true));
        Assert.False(await store.SetEmptyAsync(boxel, true));
        Assert.True(await new EmptyBoxelStore(temporaryDirectory).IsEmptyAsync(boxel));
        Assert.Contains(boxel.Id, await store.LoadGroupAsync(boxel));

        var path = store.GetFilePath(boxel);
        Assert.Equal(
            Path.Combine(temporaryDirectory, "emptyBoxels"),
            Path.GetDirectoryName(path));
        var values = JsonSerializer.Deserialize<HashSet<string>>(
            await File.ReadAllTextAsync(path));
        Assert.Contains(boxel.Id, Assert.IsType<HashSet<string>>(values));

        Assert.True(await store.SetEmptyAsync(boxel, false));
        Assert.False(await store.IsEmptyAsync(boxel));
    }

    [Fact]
    public async Task MalformedExistingFileIsNeverOverwritten()
    {
        var boxel = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var store = new EmptyBoxelStore(temporaryDirectory);
        var path = store.GetFilePath(boxel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string malformed = "[\"IL-P c5\",";
        await File.WriteAllTextAsync(path, malformed);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SetEmptyAsync(boxel, true));

        Assert.Contains("was not changed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task MassCodeHCannotBeStoredAsEmpty()
    {
        var store = new EmptyBoxelStore(temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetEmptyAsync(
                BoxelAddress.Parse("Praea Euq IL-P h5-0"),
                true));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
