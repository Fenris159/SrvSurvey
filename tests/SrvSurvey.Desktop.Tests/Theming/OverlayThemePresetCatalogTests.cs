using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class OverlayThemePresetCatalogTests
{
    [Fact]
    public void IncludesDefaultAndFiveExpandedRavenColonialPalettes()
    {
        Assert.Equal(
            [
                "Default",
                "Nebula Cyan",
                "Toxic Green",
                "Crimson Wake",
                "Void Amethyst",
                "Cerulean Gold",
            ],
            OverlayThemePresetCatalog.Presets.Select(preset => preset.Name));

        var defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        Assert.True(defaults.All(entry =>
            OverlayThemePresetCatalog.Default.Colors[entry.Key] == entry.Value));
        Assert.Equal(Color.Parse("#FF6F00"), defaults["bio.confirmed"]);
        Assert.Equal(Color.Parse("#552400"), defaults["bio.confirmedDim"]);
        Assert.Equal(
            Color.FromArgb(140, 95, 48, 3),
            defaults["bio.potential"]);
        Assert.Equal(
            Color.FromArgb(140, 31, 16, 1),
            defaults["bio.confirmedDimPotential"]);
        Assert.Equal(Color.Parse("#54DFED"), defaults["bio.prediction"]);
        Assert.Equal(
            Color.FromArgb(180, 0, 139, 139),
            defaults["bio.predictionPotential"]);
        Assert.Equal(Color.Parse("#FFD700"), defaults["bio.gold"]);
        Assert.Equal(Color.Parse("#785F00"), defaults["bio.goldDark"]);
        Assert.Equal(Color.Parse("#B8860B"), defaults["bio.goldFill"]);
        Assert.Equal(Color.Parse("#3F2D03"), defaults["bio.goldDarkFill"]);
        Assert.Equal(
            Color.FromArgb(144, 184, 134, 11),
            defaults["bio.goldPotential"]);
        Assert.Equal(
            Color.FromArgb(140, 184, 134, 11),
            defaults["bio.goldDarkPotential"]);
        Assert.Equal(Color.Parse("#F4F4F4"), defaults["bio.galacticRegion"]);
        Assert.Equal(
            Color.FromArgb(140, 184, 184, 184),
            defaults["bio.galacticRegionPotential"]);
        Assert.Equal(Color.Parse("#696969"), defaults["bio.unknownGlyph"]);
        Assert.Equal(Color.FromArgb(242, 64, 64, 64), defaults["bio.hatch"]);
        Assert.Equal(Color.Parse("#000000"), defaults["bio.empty"]);
        Assert.Equal(
            Color.FromArgb(96, 255, 111, 0),
            defaults["bio.confirmedEdge"]);
        Assert.Equal(
            Color.FromArgb(96, 85, 36, 0),
            defaults["bio.confirmedDimEdge"]);
        Assert.Equal(
            Color.FromArgb(96, 0, 139, 139),
            defaults["bio.predictionEdge"]);
        Assert.Equal(
            Color.FromArgb(96, 255, 215, 0),
            defaults["bio.goldEdge"]);
        Assert.Equal(
            Color.FromArgb(96, 184, 134, 11),
            defaults["bio.goldDarkEdge"]);
        Assert.Equal(
            Color.FromArgb(96, 255, 255, 255),
            defaults["bio.galacticRegionEdge"]);
        Assert.Equal(
            Color.FromArgb(96, 0, 139, 139),
            defaults["bio.unknownEdge"]);
        Assert.Equal(
            Color.Parse("#5F3003"),
            defaults["bio.confirmedSegmentEdge"]);
        Assert.Equal(
            Color.FromArgb(124, 255, 111, 0),
            defaults["bio.confirmedPotentialSegmentEdge"]);
        Assert.Equal(
            Color.Parse("#1F1001"),
            defaults["bio.confirmedDimSegmentEdge"]);
        Assert.Equal(
            Color.FromArgb(124, 85, 36, 0),
            defaults["bio.confirmedDimPotentialSegmentEdge"]);
        Assert.Equal(
            Color.Parse("#008B8B"),
            defaults["bio.predictionSegmentEdge"]);
        Assert.Equal(
            Color.Parse("#008B8B"),
            defaults["bio.predictionPotentialSegmentEdge"]);
        Assert.Equal(
            Color.Parse("#FFD700"),
            defaults["bio.goldSegmentEdge"]);
        Assert.Equal(
            Color.FromArgb(144, 214, 164, 11),
            defaults["bio.goldPotentialSegmentEdge"]);
        Assert.Equal(
            Color.Parse("#B8860B"),
            defaults["bio.goldDarkSegmentEdge"]);
        Assert.Equal(
            Color.FromArgb(124, 63, 45, 3),
            defaults["bio.goldDarkPotentialSegmentEdge"]);
        Assert.Equal(
            Color.Parse("#808080"),
            defaults["bio.galacticRegionSegmentEdge"]);
        Assert.Equal(
            Color.FromArgb(144, 255, 255, 255),
            defaults["bio.galacticRegionPotentialSegmentEdge"]);
    }

    [Theory]
    [InlineData("Nebula Cyan", "#5EC8F2", "#B8E8FF", "#D6EEF9", "#FFE8A3")]
    [InlineData("Toxic Green", "#5CFF9E", "#A8FFCC", "#D8FFE8", "#FFF066")]
    [InlineData("Crimson Wake", "#FF6B6B", "#FFB8B8", "#FFE4E4", "#FFD966")]
    [InlineData("Void Amethyst", "#C9A0FF", "#E2CCFF", "#E8E0F5", "#7FFFD4")]
    [InlineData("Cerulean Gold", "#3D9EE8", "#F2F7FC", "#C8E4FA", "#FFCC33")]
    public void PreservesSourceRolesAndCoversEverySrvSurveyColor(
        string name,
        string primary,
        string secondary,
        string text,
        string values)
    {
        Assert.True(OverlayThemePresetCatalog.TryGet(name, out var preset));
        var required = LegacyOverlayThemeStore.CreateDefault().Colors;

        Assert.Equal(required.Count, preset.Colors.Count);
        Assert.All(required.Keys, key => Assert.True(preset.Colors.ContainsKey(key)));
        Assert.Equal(Color.Parse(primary), preset.Colors["orange"]);
        Assert.Equal(Color.Parse(secondary), preset.Colors["cyan"]);
        Assert.Equal(Color.Parse(text), preset.Colors["white"]);
        Assert.Equal(Color.Parse(values), preset.Colors["yellow"]);
        Assert.Equal(Color.Parse(primary), preset.Colors["guardian.primary"]);
        Assert.Equal(Color.Parse(values), preset.Colors["colonise.highlight"]);
        Assert.Equal(Color.Parse(primary), preset.Colors["bio.confirmed"]);
        Assert.Equal(Color.Parse(values), preset.Colors["bio.gold"]);
        Assert.Equal(Color.Parse(text), preset.Colors["bio.galacticRegion"]);
        Assert.Equal(preset.Colors["grey"], preset.Colors["bio.unknownGlyph"]);
        Assert.Equal(Color.Parse(text), preset.Colors["bio.white"]);
        Assert.NotEqual(
            preset.Colors["bio.confirmed"],
            preset.Colors["bio.prediction"]);
        Assert.NotEqual(
            preset.Colors["bio.prediction"],
            preset.Colors["bio.predictionPotential"]);
        Assert.Equal((byte)242, preset.Colors["bio.hatch"].A);
        Assert.Equal((byte)140, preset.Colors["bio.confirmedDimPotential"].A);
        Assert.Equal((byte)144, preset.Colors["bio.goldPotential"].A);
        Assert.Equal((byte)140, preset.Colors["bio.goldDarkPotential"].A);
        Assert.Equal(preset.Colors["black"], preset.Colors["bio.empty"]);
        Assert.Equal((byte)96, preset.Colors["bio.confirmedEdge"].A);
        Assert.Equal((byte)96, preset.Colors["bio.confirmedDimEdge"].A);
        Assert.Equal((byte)96, preset.Colors["bio.predictionEdge"].A);
        Assert.Equal((byte)96, preset.Colors["bio.goldEdge"].A);
        Assert.Equal((byte)96, preset.Colors["bio.goldDarkEdge"].A);
        Assert.Equal((byte)96, preset.Colors["bio.galacticRegionEdge"].A);
        Assert.Equal((byte)96, preset.Colors["bio.unknownEdge"].A);
        AssertSameRgb(
            preset.Colors["bio.confirmed"],
            preset.Colors["bio.confirmedEdge"]);
        AssertSameRgb(
            preset.Colors["bio.confirmedDim"],
            preset.Colors["bio.confirmedDimEdge"]);
        AssertSameRgb(
            preset.Colors["bio.predictionPotential"],
            preset.Colors["bio.predictionEdge"]);
        AssertSameRgb(preset.Colors["bio.gold"], preset.Colors["bio.goldEdge"]);
        AssertSameRgb(
            preset.Colors["bio.goldFill"],
            preset.Colors["bio.goldDarkEdge"]);
        AssertSameRgb(
            preset.Colors["bio.white"],
            preset.Colors["bio.galacticRegionEdge"]);
        Assert.Equal(
            preset.Colors["bio.predictionEdge"],
            preset.Colors["bio.unknownEdge"]);
        Assert.Equal(
            preset.Colors["orangeDark"],
            preset.Colors["bio.confirmedSegmentEdge"]);
        Assert.Equal(
            (byte)124,
            preset.Colors["bio.confirmedPotentialSegmentEdge"].A);
        AssertSameRgb(
            preset.Colors["bio.predictionPotential"],
            preset.Colors["bio.predictionSegmentEdge"]);
        Assert.Equal(
            preset.Colors["bio.predictionSegmentEdge"],
            preset.Colors["bio.predictionPotentialSegmentEdge"]);
        Assert.Equal(
            preset.Colors["yellow"],
            preset.Colors["bio.goldSegmentEdge"]);
        Assert.Equal(
            (byte)144,
            preset.Colors["bio.goldPotentialSegmentEdge"].A);
        Assert.Equal(
            preset.Colors["bio.goldFill"],
            preset.Colors["bio.goldDarkSegmentEdge"]);
        Assert.Equal(
            (byte)124,
            preset.Colors["bio.goldDarkPotentialSegmentEdge"].A);
        Assert.Equal(
            (byte)144,
            preset.Colors["bio.galacticRegionPotentialSegmentEdge"].A);
        Assert.Equal(required["red"], preset.Colors["red"]);
        Assert.Equal(required["green"], preset.Colors["green"]);
    }

    private static void AssertSameRgb(Color expected, Color actual)
    {
        Assert.Equal(expected.R, actual.R);
        Assert.Equal(expected.G, actual.G);
        Assert.Equal(expected.B, actual.B);
    }
}
