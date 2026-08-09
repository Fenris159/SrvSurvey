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
        Assert.Equal(Color.Parse("#9C4F05"), defaults["bio.confirmedDim"]);
        Assert.Equal(Color.Parse("#5F3003"), defaults["bio.potential"]);
        Assert.Equal(Color.Parse("#FFC400"), defaults["bio.prediction"]);
        Assert.Equal(
            Color.Parse("#8A6A00"),
            defaults["bio.predictionPotential"]);
        Assert.Equal(Color.Parse("#FFFF00"), defaults["bio.gold"]);
        Assert.Equal(Color.Parse("#FFFFFF"), defaults["bio.galacticRegion"]);
        Assert.Equal(
            Color.Parse("#808080"),
            defaults["bio.galacticRegionPotential"]);
        Assert.Equal(Color.Parse("#B0B0B0"), defaults["bio.unknownGlyph"]);
        Assert.Equal((byte)190, defaults["bio.hatch"].A);
        Assert.Equal((byte)48, defaults["bio.empty"].A);
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
        Assert.Equal(Color.Parse(text), preset.Colors["bio.unknownGlyph"]);
        Assert.Equal(Color.Parse(text), preset.Colors["bio.white"]);
        Assert.NotEqual(
            preset.Colors["bio.confirmed"],
            preset.Colors["bio.prediction"]);
        Assert.NotEqual(
            preset.Colors["bio.prediction"],
            preset.Colors["bio.predictionPotential"]);
        Assert.Equal((byte)190, preset.Colors["bio.hatch"].A);
        Assert.Equal((byte)48, preset.Colors["bio.empty"].A);
        Assert.Equal(required["red"], preset.Colors["red"]);
        Assert.Equal(required["green"], preset.Colors["green"]);
    }
}
