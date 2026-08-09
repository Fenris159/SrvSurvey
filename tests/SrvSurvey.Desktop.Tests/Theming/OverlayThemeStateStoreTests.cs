using Avalonia.Media;
using SrvSurvey.Desktop.Theming;
using System.Text.Json;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class OverlayThemeStateStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-state-tests-{Guid.NewGuid():N}");

    [Fact]
    public void NamedStatesRoundTripUpdateAndDeleteWithoutChangingThemeJson()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var statePath = Path.Combine(temporaryDirectory, "overlay-theme-states.json");
        var themePath = Path.Combine(temporaryDirectory, "theme.json");
        const string originalTheme = "{\"orange\":[1,2,3]}";
        File.WriteAllText(themePath, originalTheme);
        var store = new OverlayThemeStateStore(statePath);
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();

        var first = store.SaveState("Exploration", colors);
        colors["orange"] = Color.FromArgb(255, 10, 20, 30);
        var updated = store.SaveState(" exploration ", colors);
        var loaded = store.Load();

        var state = Assert.Single(loaded.States);
        Assert.Equal("exploration", state.Name);
        Assert.Equal(Color.FromArgb(255, 10, 20, 30), state.Colors["orange"]);
        Assert.False(first.ReplacedExisting);
        Assert.True(updated.ReplacedExisting);
        Assert.NotNull(updated.BackupPath);
        Assert.Equal(originalTheme, File.ReadAllText(themePath));

        _ = store.DeleteState("EXPLORATION");

        Assert.Empty(store.Load().States);
        Assert.Equal(originalTheme, File.ReadAllText(themePath));
    }

    [Fact]
    public void InvalidStateFileIsPreservedAndNotOverwritten()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var statePath = Path.Combine(temporaryDirectory, "overlay-theme-states.json");
        const string invalid = "{\"version\":99,\"states\":[]}";
        File.WriteAllText(statePath, invalid);
        var store = new OverlayThemeStateStore(statePath);

        var error = Assert.Throws<InvalidDataException>(() => store.SaveState(
            "Do not write",
            LegacyOverlayThemeStore.CreateDefault().Colors));

        Assert.Contains("not supported", error.Message);
        Assert.Equal(invalid, File.ReadAllText(statePath));
    }

    [Fact]
    public void StatesSavedBeforePipEdgesGainMatchingPresetRolesOnLoad()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var statePath = Path.Combine(temporaryDirectory, "overlay-theme-states.json");
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        var addedRoles = new[]
        {
            "bio.goldFill",
            "bio.goldDarkFill",
            "bio.confirmedEdge",
            "bio.confirmedDimEdge",
            "bio.predictionEdge",
            "bio.goldEdge",
            "bio.goldDarkEdge",
            "bio.galacticRegionEdge",
            "bio.unknownEdge",
        };
        foreach (var role in addedRoles)
        {
            Assert.True(colors.Remove(role));
        }

        var serializedColors = colors.ToDictionary(
            entry => entry.Key,
            entry => LegacyOverlayThemeStore.FormatHtmlColor(entry.Value));
        File.WriteAllText(
            statePath,
            JsonSerializer.Serialize(new
            {
                version = 1,
                states = new[]
                {
                    new { name = "Older default", colors = serializedColors },
                },
            }));

        var loaded = new OverlayThemeStateStore(statePath).Load();

        Assert.Null(loaded.Error);
        var state = Assert.Single(loaded.States);
        var defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        Assert.All(addedRoles, role =>
            Assert.Equal(defaults[role], state.Colors[role]));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
