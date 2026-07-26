using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

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

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
