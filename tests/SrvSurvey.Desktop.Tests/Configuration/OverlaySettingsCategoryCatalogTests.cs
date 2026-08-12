using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlaySettingsCategoryCatalogTests
{
    [Fact]
    public void CatalogDefinesEveryNavigableOverlayCategoryInOrder()
    {
        var categories = OverlaySettingsCategoryCatalog.All;

        Assert.Equal(
            [
                OverlaySettingsCategory.Exploration,
                OverlaySettingsCategory.Exobiology,
                OverlaySettingsCategory.Travel,
                OverlaySettingsCategory.Guardian,
                OverlaySettingsCategory.Quests,
                OverlaySettingsCategory.Colonization,
            ],
            categories.Select(category => category.Category).ToArray());
        Assert.Equal(
            ["exploration", "exobiology", "travel", "guardian", "quests", "colonisation"],
            categories.Select(category => category.NavigationKey).ToArray());
        Assert.All(categories, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.Equal(definition.DisplayName.ToUpperInvariant(), definition.Eyebrow);
            Assert.StartsWith("Configure ", definition.Description);
        });
    }

    [Fact]
    public void TryGetResolvesEveryExactNavigationKey()
    {
        foreach (var expected in OverlaySettingsCategoryCatalog.All)
        {
            Assert.True(OverlaySettingsCategoryCatalog.TryGet(
                expected.NavigationKey,
                out var actual));
            Assert.Same(expected, actual);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Guardian")]
    [InlineData("search")]
    public void TryGetRejectsUnknownOrNonExactNavigationKeys(string? key)
    {
        Assert.False(OverlaySettingsCategoryCatalog.TryGet(key, out var definition));
        Assert.Null(definition);
    }
}
