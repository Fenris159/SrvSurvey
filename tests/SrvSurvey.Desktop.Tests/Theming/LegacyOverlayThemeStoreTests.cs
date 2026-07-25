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
        Assert.Equal(29, theme.Colors.Count);
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

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
