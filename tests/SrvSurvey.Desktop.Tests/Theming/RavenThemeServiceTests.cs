using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Theming;

public sealed class RavenThemeServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-theme-service-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EveryThemeUpdatesAvaloniaResourcesAndNativeMode()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var service = new RavenThemeService(application, store);
        service.ApplyCurrent();

        foreach (var theme in RavenThemeCatalog.All)
        {
            service.Select(theme.Key);

            Assert.Equal(theme, service.Current);
            Assert.Equal(
                theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light,
                application.RequestedThemeVariant);
            var accent = Assert.IsType<SolidColorBrush>(
                application.Resources["RavenAccentBrush"]);
            Assert.Equal(Color.Parse(theme.AccentColor), accent.Color);
        }

        Assert.Equal("green-dark", store.LoadThemeKey());
    }

    [Fact]
    public void CustomLegacyPaletteCreatesOverlayAndNamedResources()
    {
        var application = new Application();
        var store = new ThemePreferenceStore(
            Path.Combine(temporaryDirectory, "ui.json"));
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary();
        colors["orange"] = Color.FromArgb(255, 12, 34, 56);
        colors["bio.gold"] = Color.FromArgb(255, 78, 90, 12);
        var service = new RavenThemeService(
            application,
            store,
            new LegacyOverlayTheme(colors, true, null));

        service.ApplyCurrent();

        Assert.Equal(
            Color.FromArgb(255, 12, 34, 56),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenOverlayAccentBrush"]).Color);
        Assert.Equal(
            Color.FromArgb(255, 78, 90, 12),
            Assert.IsType<SolidColorBrush>(
                application.Resources["LegacyTheme.bio.gold"]).Color);
        Assert.Equal(
            Color.Parse(RavenThemeCatalog.Get(null).AccentColor),
            Assert.IsType<SolidColorBrush>(
                application.Resources["RavenAccentBrush"]).Color);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
