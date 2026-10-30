using System.Text.Json.Nodes;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class MiningOverlaySettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-mining-overlay-settings-{Guid.NewGuid():N}"
    );

    [Fact]
    public void DraftPatchAccumulatesEditsIncludingReturningToADefaultValue()
    {
        var store = new MiningOverlaySettingsStore(Path.Combine(directory, "ui.json"));
        var defaults = new MiningOverlayPreferences();
        MiningOverlayPreferences changed = defaults with { SessionYieldLimit = 9 };
        store.Save(null, defaults, changed);
        store.Save(null, changed, changed with { HideInSupercruise = false });
        MiningOverlayPreferences draft = store.Load(null, () => defaults);
        Assert.Equal(9, draft.SessionYieldLimit);
        Assert.False(draft.HideInSupercruise);
        store.Save(null, draft, draft with { SessionYieldLimit = 5 });
        MiningOverlayPreferences legacy = defaults with { NotificationSeconds = 40, SessionYieldLimit = 20 };
        MiningOverlayPreferences applied = store.Load(" F1 ", () => legacy);
        Assert.Equal(5, applied.SessionYieldLimit);
        Assert.Equal(40, applied.NotificationSeconds);
        Assert.False(applied.HideInSupercruise);
        Assert.Equal(defaults, store.Load("F2", () => defaults));
        Assert.Equal(
            applied,
            store.Load("f1", () => throw new InvalidOperationException("Saved settings should not need mining history"))
        );
    }

    [Fact]
    public void KnownPreferencesPreserveOtherSettingsAndNoOpDoesNotWriteAFile()
    {
        string path = Path.Combine(directory, "ui.json");
        var store = new MiningOverlaySettingsStore(path);
        var defaults = new MiningOverlayPreferences();
        store.Save(null, defaults, defaults);
        Assert.False(File.Exists(path));
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, """{"Other":{"Value":42}}""");
        MiningOverlayPreferences changed = defaults with { SessionYieldSort = MiningSessionYieldSort.Finds };
        store.Save("F1", defaults, changed);
        Assert.Equal(42, JsonNode.Parse(File.ReadAllText(path))!["Other"]!["Value"]!.GetValue<int>());
        Assert.Equal(changed, store.Load("F1", () => defaults));
        Assert.Equal(defaults, store.Load("F2", () => defaults));
    }

    [Fact]
    public void InvalidNumericPreferencesAreClampedWhenApplied()
    {
        var preferences = new MiningOverlayPreferences(
            PersistentProspectSlots: 0,
            SessionYieldLimit: 999,
            SessionYieldSort: (MiningSessionYieldSort)99
        );
        var settings = new MiningPreferences();
        preferences.ApplyTo(settings);
        Assert.Equal(1, settings.PersistentProspectSlots);
        Assert.Equal(100, settings.SessionYieldLimit);
        Assert.Equal(MiningSessionYieldSort.AveragePercentage, settings.SessionYieldSort);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
