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
                "Monochrome (dark)",
            ],
            RavenThemeCatalog.All.Select(theme => theme.DisplayName));
        Assert.Equal(2, RavenThemeCatalog.All.Count(theme => !theme.IsDark));
        Assert.Equal(4, RavenThemeCatalog.All.Count(theme => theme.IsDark));
        Assert.Equal(
            RavenThemeCatalog.All.Count,
            RavenThemeCatalog.All.Select(theme => theme.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("blue-dark", "#3F87D4", "#000012")]
    [InlineData("orange-dark", "#D36F00", "#000000")]
    [InlineData("green-light", "#3C8223", "#F9FFF7")]
    [InlineData("green-dark", "#D1D93B", "#1E3533")]
    [InlineData("monochrome-dark", "#E6D59A", "#0A0A0A")]
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
    public void MonochromeThemeUsesTheRequestedLowGlarePaletteWithSoftAccent()
    {
        var theme = RavenThemeCatalog.Get("monochrome-dark");

        Assert.Equal("#0A0A0A", theme.WindowColor);
        Assert.Equal("#141414", theme.SidebarColor);
        Assert.Equal("#141414", theme.SurfaceColor);
        Assert.Equal("#1C1C1C", theme.RaisedSurfaceColor);
        Assert.Equal("#E6D59A", theme.AccentColor);
        Assert.Equal("#F0E4BC", theme.AccentHoverColor);
        Assert.Equal("#F5F5F5", theme.ControlAccentColor);
        Assert.Equal("#EDEDED", theme.ControlAccentHoverColor);
        Assert.Equal("#262626", theme.AccentMutedColor);
        Assert.Equal("#0A0A0A", theme.AccentForegroundColor);
        Assert.Equal("#EDEDED", theme.TextColor);
        Assert.Equal("#A3A3A3", theme.MutedTextColor);
        Assert.Equal("#2A2A2A", theme.BorderColor);
        Assert.Equal("#FF7B72", theme.DangerColor);
    }

    [Fact]
    public void UnknownThemeFallsBackToBlueDark()
    {
        Assert.Equal(
            RavenThemeCatalog.DefaultThemeKey,
            RavenThemeCatalog.Get("not-a-theme").Key);
    }
}
