using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class PublishedReferenceVersionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadReadsImportedWinFormsVersionFields()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "settings.json"),
            """
            {
              "pubCodexRef": 10,
              "pubBioCriteria": 7,
              "pubDataSettlementTemplate": 48,
              "pubDataGuardian": 68,
              "pubSettlements": 15,
              "pubNicknames": 2,
              "pubGGG": 3,
              "unknownPlayerSetting": true
            }
            """);

        var result = new PublishedReferenceVersionStore().Load(root);

        Assert.Equal(10, result.CodexReference);
        Assert.Equal(7, result.BiologyCriteria);
        Assert.Equal(48, result.SettlementTemplate);
        Assert.Equal(68, result.Guardian);
        Assert.Equal(15, result.Settlements);
        Assert.Equal(2, result.Nicknames);
        Assert.Equal(3, result.GreenGasGiants);
        Assert.Contains(
            "unknownPlayerSetting",
            File.ReadAllText(Path.Combine(root, "settings.json")));
    }

    [Fact]
    public async Task CrossPlatformManifestTakesPrecedenceWithoutChangingLegacySettings()
    {
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        const string legacy = "{\"pubCodexRef\":1,\"unknown\":42}";
        File.WriteAllText(settingsPath, legacy);
        var versions = new PublishedReferenceVersions(10, 7, 4, 48, 68, 15, 2, 3);
        var store = new PublishedReferenceVersionStore();

        await store.WriteAsync(Path.Combine(root, "pub"), versions);
        var result = store.Load(root);

        Assert.Equal(versions, result);
        Assert.Equal(legacy, File.ReadAllText(settingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
