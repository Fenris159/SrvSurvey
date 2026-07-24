using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class RavenThemeCatalogTests
{
    [Fact]
    public void CatalogMatchesRavenThemeMenu()
    {
        Assert.Equal(
            [
                "Blue (light)",
                "Blue (dark)",
                "Orange (dark)",
                "Green (light)",
                "Green (dark)",
            ],
            RavenThemeCatalog.All.Select(theme => theme.DisplayName));
        Assert.Equal(2, RavenThemeCatalog.All.Count(theme => !theme.IsDark));
        Assert.Equal(3, RavenThemeCatalog.All.Count(theme => theme.IsDark));
        Assert.Equal(
            RavenThemeCatalog.All.Count,
            RavenThemeCatalog.All.Select(theme => theme.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("blue-dark", "#3F87D4", "#000012")]
    [InlineData("orange-dark", "#D36F00", "#000000")]
    [InlineData("green-light", "#3C8223", "#F9FFF7")]
    [InlineData("green-dark", "#D1D93B", "#1E3533")]
    public void CatalogPreservesRavenPrimaryAndWindowColors(
        string key,
        string primary,
        string window)
    {
        var theme = RavenThemeCatalog.Get(key);

        Assert.Equal(primary, theme.AccentColor);
        Assert.Equal(window, theme.WindowColor);
    }

    [Fact]
    public void UnknownThemeFallsBackToBlueDark()
    {
        Assert.Equal(
            RavenThemeCatalog.DefaultThemeKey,
            RavenThemeCatalog.Get("not-a-theme").Key);
    }
}
