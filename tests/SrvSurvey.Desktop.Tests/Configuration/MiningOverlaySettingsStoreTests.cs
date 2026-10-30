using System.Text.Json.Nodes;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class MiningOverlaySettingsStoreTests : IDisposable
{
    [Fact]
    public void ExplicitCommanderEditsSupersedeOnlyThoseFieldsFromAnOlderUnassignedDraft()
    {
        var store = new MiningOverlaySettingsStore(Path.Combine(directory, "ui.json"));
        var defaults = new MiningOverlayPreferences();
        store.Save(null, defaults, defaults with { SessionYieldLimit = 9, NotificationSeconds = 30 });
        MiningOverlayPreferences shown = store.ReadPreferences("F1", () => defaults);
        store.SaveCommander("F1", shown, shown with { SessionYieldLimit = 12 });
        MiningOverlayPreferences reloaded = store.ReadPreferences("F1", () => defaults);
        Assert.Equal(12, reloaded.SessionYieldLimit);
        Assert.Equal(30, reloaded.NotificationSeconds);
        Assert.Equal(reloaded, store.Load("F1", () => defaults));
        Assert.Equal(defaults, store.Load("F2", () => defaults));
    }

    [Fact]
    public void AssigningADraftRetainsChangesWrittenAfterTheRead()
    {
        var store = new MiningOverlaySettingsStore(Path.Combine(directory, "ui.json"));
        var defaults = new MiningOverlayPreferences();
        store.Save(null, defaults, defaults with { SessionYieldLimit = 9 });
        MiningOverlayPreferences stale = store.ReadPreferences("F1", () => defaults);
        store.Save(null, defaults, defaults with { SessionYieldLimit = 11 });
        store.SaveCommander("F1", stale, stale, assignDraft: true);
        Assert.Equal(11, store.Load("F1", () => defaults).SessionYieldLimit);
        Assert.Equal(defaults, store.Load("F2", () => defaults));
    }

    [Fact]
    public void SavingOneFieldDoesNotOverwriteAnUnrelatedConcurrentPreference()
    {
        var store = new MiningOverlaySettingsStore(Path.Combine(directory, "ui.json"));
        var original = new MiningOverlayPreferences();
        store.SaveCommander("F1", original, original with { NotificationSeconds = 45 });
        store.SaveCommander("F1", original, original with { SessionYieldLimit = 9 });
        MiningOverlayPreferences saved = store.Load("F1", () => original);
        Assert.Equal(45, saved.NotificationSeconds);
        Assert.Equal(9, saved.SessionYieldLimit);
    }

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

    [Theory]
    [InlineData(0, 3)]
    [InlineData(999, 120)]
    public void ImportedNotificationDurationIsClampedToTheEditorRange(int stored, int expected)
    {
        var preferences = new MiningOverlayPreferences(NotificationSeconds: stored);
        var settings = new MiningPreferences();
        preferences.ApplyTo(settings);
        Assert.Equal(expected, settings.NotificationSeconds);
    }

    [Fact]
    public void DraftCanBeReadWithoutWritingAndAssignedOnlyAfterTheCommanderSnapshotIsSaved()
    {
        string path = Path.Combine(directory, "ui.json");
        var store = new MiningOverlaySettingsStore(path);
        var defaults = new MiningOverlayPreferences();
        store.Save(null, defaults, defaults with { SessionYieldLimit = 9 });
        string previous = File.ReadAllText(path);
        MiningOverlayPreferences legacy = defaults with { NotificationSeconds = 45 };
        MiningOverlayPreferences assigned = store.ReadPreferences(" F1 ", () => legacy);
        Assert.Equal(legacy with { SessionYieldLimit = 9 }, assigned);
        Assert.Equal(previous, File.ReadAllText(path));
        Assert.Equal(defaults, store.ReadPreferences("F2", () => defaults, includeDraft: false));
        store.SaveCommander(" f1 ", assigned, assigned, assignDraft: true);
        Assert.Equal(assigned, store.Load("F1", () => legacy));
        Assert.Equal(defaults, store.Load("F2", () => defaults));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
