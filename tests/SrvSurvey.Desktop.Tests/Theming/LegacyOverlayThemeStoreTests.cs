using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class LegacyOverlayThemeStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-theme-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LegacyFormatsReferencesNullsAndMissingEntriesAreSupported()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        File.WriteAllText(
            path,
            """
            {
              // Four values are ARGB, matching the original theme reader.
              "orange": [ 128, 10, 20, 30 ],
              "orangeDark": "#28323C40",
              "cyan": [ 70, 80, 90 ],
              "cyanDark": null,
              "green": "#010203",
              "colonise": {
                "surplus": "green"
              },
            }
            """);

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.True(theme.IsCustom);
        Assert.Null(theme.Error);
        Assert.Equal(Color.FromArgb(128, 10, 20, 30), theme.GetColor("orange"));
        Assert.Equal(Color.FromArgb(64, 40, 50, 60), theme.GetColor("orangeDark"));
        Assert.Equal(Color.FromArgb(255, 70, 80, 90), theme.GetColor("cyan"));
        Assert.Equal(Color.FromArgb(255, 0, 139, 139), theme.GetColor("cyanDark"));
        Assert.Equal(Color.FromArgb(255, 1, 2, 3), theme.GetColor("green"));
        Assert.Equal(theme.GetColor("green"), theme.GetColor("colonise.surplus"));
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), theme.GetColor("red"));
        Assert.Equal(
            Color.FromArgb(255, 0, 0, 0),
            theme.GetColor("guardian.background"));
        Assert.Equal(
            Color.FromArgb(255, 255, 255, 0),
            theme.GetColor("guardian.header"));
        Assert.Equal(
            Color.FromArgb(255, 255, 111, 0),
            theme.GetColor("guardian.primary"));
        Assert.Equal(
            Color.FromArgb(255, 95, 48, 3),
            theme.GetColor("guardian.primaryDark"));
        Assert.Equal(
            Color.FromArgb(255, 84, 223, 237),
            theme.GetColor("guardian.secondary"));
        Assert.Equal(
            Color.FromArgb(255, 0, 139, 139),
            theme.GetColor("guardian.secondaryDark"));
        Assert.Equal(
            Color.FromArgb(255, 255, 255, 255),
            theme.GetColor("guardian.text"));
        Assert.Equal(
            Color.FromArgb(255, 100, 100, 100),
            theme.GetColor("guardian.muted"));
        Assert.Equal(
            Color.FromArgb(255, 255, 0, 0),
            theme.GetColor("guardian.danger"));
        Assert.Equal(
            Color.FromArgb(255, 0, 255, 0),
            theme.GetColor("guardian.success"));
        Assert.Equal(
            Color.FromArgb(255, 255, 255, 0),
            theme.GetColor("guardian.warning"));
        Assert.Equal(
            Color.FromArgb(255, 20, 20, 20),
            theme.GetColor("guardian.surface"));
    }

    [Fact]
    public void SaveRoundTripsCustomGuardianPrimary()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        var store = new LegacyOverlayThemeStore(path);
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        var customPrimary = Color.FromArgb(255, 9, 18, 27);
        colors["guardian.primary"] = customPrimary;

        _ = store.Save(new LegacyOverlayTheme(colors, true, null));
        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.Equal(customPrimary, loaded.GetColor("guardian.primary"));
        Assert.Equal(
            Color.FromArgb(255, 84, 223, 237),
            loaded.GetColor("guardian.secondary"));
    }

    [Fact]
    public void InvalidThemeFallsBackWithoutChangingTheSource()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        const string invalid = "{\"orange\":\"futureColour\"}";
        File.WriteAllText(path, invalid);

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.False(theme.IsCustom);
        Assert.NotNull(theme.Error);
        Assert.Contains("Prior colour", theme.Error);
        Assert.Equal(Color.FromArgb(255, 255, 111, 0), theme.GetColor("orange"));
        Assert.Equal(invalid, File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "theme.bad.json")));
    }

    [Fact]
    public void ReferenceMustNameAColorDefinedEarlierInTheFile()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        File.WriteAllText(
            path,
            "{\"colonise\":{\"item\":\"orange\"},\"orange\":[1,2,3]}");

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.False(theme.IsCustom);
        Assert.Contains("was not found", theme.Error);
    }

    [Fact]
    public void SaveRoundTripsAllColorsAndCreatesVerifiedBackup()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        File.WriteAllText(path, "{\"orange\":[1,2,3]}");
        var store = new LegacyOverlayThemeStore(path);
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        colors["orange"] = Color.FromArgb(128, 12, 34, 56);
        colors["custom.future"] = Color.FromArgb(255, 90, 80, 70);

        var result = store.Save(new LegacyOverlayTheme(colors, true, null));
        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.Equal(colors.Count, loaded.Colors.Count);
        Assert.All(colors, entry => Assert.Equal(
            entry.Value,
            loaded.Colors[entry.Key]));
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal("{\"orange\":[1,2,3]}", File.ReadAllText(result.BackupPath!));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
