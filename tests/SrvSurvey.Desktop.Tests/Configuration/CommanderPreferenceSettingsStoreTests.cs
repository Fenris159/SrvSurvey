using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CommanderPreferenceSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-commander-preference-tests-{Guid.NewGuid():N}");

    [Fact]
    public void RoundTripsNormalizedStableIdentityAndPreservesOtherSettings()
    {
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(settingsPath, "{\"Theme\":\"green-dark\"}");
        var store = new CommanderPreferenceSettingsStore(settingsPath);

        store.Save(new CommanderPreferencePreferences("  Drew  ", " f123 "));

        Assert.Equal(
            new CommanderPreferencePreferences("Drew", "F123"),
            store.Load());
        Assert.Contains("green-dark", File.ReadAllText(settingsPath));
    }

    [Fact]
    public void RejectsInvalidFrontierIdWithoutChangingSettings()
    {
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        var store = new CommanderPreferenceSettingsStore(settingsPath);
        store.Save(new CommanderPreferencePreferences("Drew", "F123"));
        var before = File.ReadAllText(settingsPath);

        Assert.Throws<ArgumentException>(() => store.Save(
            new CommanderPreferencePreferences("Raven", "../profile")));

        Assert.Equal(before, File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task ResolvesUniqueImportedNameAndPersistsFrontierId()
    {
        var profileDirectory = Path.Combine(temporaryDirectory, "profiles");
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        Directory.CreateDirectory(profileDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(profileDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"isOdyssey\":true}");
        var store = new CommanderPreferenceSettingsStore(settingsPath);
        store.Save(new CommanderPreferencePreferences("drew", null));

        var result = await new CommanderPreferenceResolver(
                store,
                new CommanderProfileCatalog(profileDirectory))
            .ResolveAsync(null);

        Assert.Equal("F123", result.TargetFrontierId);
        Assert.False(result.IsCommandLineOverride);
        Assert.Contains("stable identity", result.StatusMessage);
        Assert.Equal(
            new CommanderPreferencePreferences("Drew", "F123"),
            store.Load());
    }

    [Fact]
    public async Task AmbiguousImportedNameFallsBackWithoutChangingPreference()
    {
        var profileDirectory = Path.Combine(temporaryDirectory, "profiles");
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        Directory.CreateDirectory(profileDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(profileDirectory, "F123-live.json"),
            "{\"fid\":\"F123\",\"commander\":\"Drew\"}");
        await File.WriteAllTextAsync(
            Path.Combine(profileDirectory, "F456-live.json"),
            "{\"fid\":\"F456\",\"commander\":\"Drew\"}");
        var store = new CommanderPreferenceSettingsStore(settingsPath);
        var preference = new CommanderPreferencePreferences("Drew", null);
        store.Save(preference);

        var result = await new CommanderPreferenceResolver(
                store,
                new CommanderProfileCatalog(profileDirectory))
            .ResolveAsync(null);

        Assert.Null(result.TargetFrontierId);
        Assert.Contains("more than one", result.StatusMessage);
        Assert.Equal(preference, store.Load());
    }

    [Fact]
    public async Task CommandLineIdentityWinsWithoutReplacingSavedPreference()
    {
        var settingsPath = Path.Combine(temporaryDirectory, "ui.json");
        var store = new CommanderPreferenceSettingsStore(settingsPath);
        var preference = new CommanderPreferencePreferences("Drew", "F123");
        store.Save(preference);

        var result = await new CommanderPreferenceResolver(
                store,
                new CommanderProfileCatalog(Path.Combine(
                    temporaryDirectory,
                    "missing-profiles")))
            .ResolveAsync("F456");

        Assert.Equal("F456", result.TargetFrontierId);
        Assert.True(result.IsCommandLineOverride);
        Assert.Equal(preference, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
