using Avalonia.Media;
using SrvSurvey.Desktop.Theming;
using System.Text.Json;

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
        Assert.Equal(OverlayTypographySettings.Default, theme.EffectiveTypography);
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
        var typography = OverlayTypographySettings.Default with
        {
            Header = 11.5,
            Title = 16,
        };

        var result = store.Save(new LegacyOverlayTheme(
            colors,
            true,
            null,
            typography));
        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.Equal(colors.Count, loaded.Colors.Count);
        Assert.All(colors, entry => Assert.Equal(
            entry.Value,
            loaded.Colors[entry.Key]));
        Assert.Equal(typography, loaded.EffectiveTypography);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal("{\"orange\":[1,2,3]}", File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public void LegacyCeruleanGoldBiologyPaletteIsUpgradedInMemory()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        File.WriteAllText(path, JsonSerializer.Serialize(
            CreateLegacyCeruleanGoldColors()));

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.True(OverlayThemePresetCatalog.TryGet(
            "Cerulean Gold",
            out var currentPreset));
        Assert.Equal(
            currentPreset.Colors["bio.prediction"],
            theme.GetColor("bio.prediction"));
        Assert.Equal(Color.Parse("#FFCC33"), theme.GetColor("header"));
        Assert.NotEqual(
            Color.Parse("#4D4F51"),
            theme.GetColor("bio.prediction"));
        Assert.Equal(
            "Cerulean Gold",
            OverlayThemePresetCatalog.FindMatching(theme.Colors)?.Name);
    }

    [Fact]
    public void OriginalDefaultBiologyPaletteGainsLegacyPipLayersAndEdges()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        var colors = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["orange"] = [255, 255, 111, 0],
            ["orangeDark"] = [255, 95, 48, 3],
            ["cyan"] = [255, 84, 223, 237],
            ["cyanDark"] = [255, 0, 139, 139],
            ["yellow"] = [255, 255, 255, 0],
            ["white"] = [255, 255, 255, 255],
            ["menuGold"] = [235, 235, 145, 0],
            ["grey"] = [255, 100, 100, 100],
            ["bio.gold"] = [255, 255, 215, 0],
            ["bio.goldDark"] = [255, 120, 95, 0],
            ["bio.unknown"] = [255, 105, 105, 105],
            ["bio.hatch"] = [242, 64, 64, 64],
            ["bio.white"] = [255, 255, 255, 255],
            ["bio.prediction"] = [255, 47, 79, 79],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(colors));

        var theme = new LegacyOverlayThemeStore(path).Load();
        var defaults = LegacyOverlayThemeStore.CreateDefault().Colors;

        Assert.Null(theme.Error);
        Assert.Equal(defaults["bio.prediction"], theme.GetColor("bio.prediction"));
        Assert.Equal(defaults["grey"], theme.GetColor("grey"));
        Assert.Equal(defaults["bio.goldFill"], theme.GetColor("bio.goldFill"));
        Assert.Equal(
            defaults["bio.predictionPotential"],
            theme.GetColor("bio.predictionPotential"));
        Assert.Equal(
            defaults["bio.predictionEdge"],
            theme.GetColor("bio.predictionEdge"));
        Assert.Equal(
            defaults["bio.predictionSegmentEdge"],
            theme.GetColor("bio.predictionSegmentEdge"));
        Assert.Equal(
            defaults["bio.goldPotentialSegmentEdge"],
            theme.GetColor("bio.goldPotentialSegmentEdge"));
        Assert.Equal(
            defaults["bio.confirmedDimPotential"],
            theme.GetColor("bio.confirmedDimPotential"));
        Assert.Equal(
            defaults["bio.goldPotential"],
            theme.GetColor("bio.goldPotential"));
        Assert.Equal(
            OverlayThemePresetCatalog.DefaultName,
            OverlayThemePresetCatalog.FindMatching(theme.Colors)?.Name);
    }

    [Fact]
    public void CustomLegacyMutedGreyIsNotMigrated()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        File.WriteAllText(
            path,
            """
            {
              "orange": "#010203",
              "grey": "#646464"
            }
            """);

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.Null(theme.Error);
        Assert.Equal(Color.Parse("#646464"), theme.GetColor("grey"));
    }

    [Fact]
    public void NonPresetLegacyBiologyPaletteDerivesExpandedCustomRoles()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        var colors = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["orange"] = [255, 10, 20, 30],
            ["orangeDark"] = [255, 4, 8, 12],
            ["cyan"] = [255, 120, 140, 160],
            ["cyanDark"] = [255, 40, 50, 60],
            ["yellow"] = [255, 210, 190, 70],
            ["white"] = [255, 220, 210, 200],
            ["black"] = [255, 4, 5, 6],
            ["menuGold"] = [235, 170, 120, 40],
            ["grey"] = [255, 80, 70, 60],
            ["bio.gold"] = [255, 210, 190, 70],
            ["bio.goldDark"] = [255, 90, 70, 20],
            ["bio.unknown"] = [255, 70, 60, 50],
            ["bio.hatch"] = [242, 30, 25, 20],
            ["bio.white"] = [255, 200, 180, 160],
            ["bio.prediction"] = [255, 100, 80, 60],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(colors));

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.Null(theme.Error);
        Assert.Null(OverlayThemePresetCatalog.FindMatching(theme.Colors));
        Assert.Equal(
            Color.FromArgb(180, 45, 36, 27),
            theme.GetColor("bio.predictionPotential"));
        Assert.Equal(
            Color.FromArgb(255, 200, 180, 160),
            theme.GetColor("bio.galacticRegion"));
        Assert.Equal(
            Color.FromArgb(140, 148, 133, 118),
            theme.GetColor("bio.galacticRegionPotential"));
        Assert.Equal(
            Color.FromArgb(255, 70, 60, 50),
            theme.GetColor("bio.unknownGlyph"));
        Assert.Equal(
            Color.FromArgb(255, 4, 5, 6),
            theme.GetColor("bio.empty"));
    }

    [Fact]
    public void ExplicitExpandedBiologyPaletteIsNotMigrated()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "theme.json");
        var colors = CreateLegacyCeruleanGoldColors();
        colors["bio.confirmed"] = "#010203";
        File.WriteAllText(path, JsonSerializer.Serialize(colors));

        var theme = new LegacyOverlayThemeStore(path).Load();

        Assert.Equal(Color.Parse("#4D4F51"), theme.GetColor("bio.prediction"));
        Assert.Equal(Color.Parse("#010203"), theme.GetColor("bio.confirmed"));
    }

    private static Dictionary<string, string> CreateLegacyCeruleanGoldColors()
    {
        var preset = OverlayThemePresetCatalog.Presets.Single(candidate =>
            candidate.Name == "Cerulean Gold");
        var colors = preset.Colors
            .Where(entry => !entry.Key.StartsWith("bio.", StringComparison.Ordinal))
            .ToDictionary(
                entry => entry.Key,
                entry => LegacyOverlayThemeStore.FormatHtmlColor(entry.Value),
                StringComparer.Ordinal);
        colors["bio.gold"] = "#FFCC33";
        colors["bio.goldDark"] = "#6B5615";
        colors["bio.unknown"] = "#70808C";
        colors["bio.hatch"] = "#061017F2";
        colors["bio.white"] = "#C8E4FA";
        colors["bio.prediction"] = "#4D4F51";
        return colors;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
