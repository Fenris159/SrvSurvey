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
        Assert.Equal(required["red"], preset.Colors["red"]);
        Assert.Equal(required["green"], preset.Colors["green"]);
    }
}
