using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class WindowChromeThemeCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-window-chrome-tests-{Guid.NewGuid():N}"
    );

    [Theory]
    [InlineData("blue-dark", "#FF13293F", "#FF195494", "#FFE5E5E5")]
    [InlineData("orange-dark", "#FF3F2200", "#FF824500", "#FFF4E1C8")]
    [InlineData("monochrome-dark", "#FF262626", "#FF3A3A3A", "#FFEDEDED")]
    public void ActivePaletteUsesThemeChromeColors(string themeKey, string caption, string border, string text)
    {
        var palette = WindowChromeThemePalette.Create(RavenThemeCatalog.Get(themeKey), isActive: true);

        Assert.Equal(Color.Parse(caption), palette.Caption);
        Assert.Equal(Color.Parse(border), palette.Border);
        Assert.Equal(Color.Parse(text), palette.Text);
    }

    [Fact]
    public void InactivePaletteFadesTowardWindowAndUsesMutedText()
    {
        var palette = WindowChromeThemePalette.Create(RavenThemeCatalog.Get("monochrome-dark"), isActive: false);

        Assert.Equal(Color.Parse("#FF1C1C1C"), palette.Caption);
        Assert.Equal(Color.Parse("#FF222222"), palette.Border);
        Assert.Equal(Color.Parse("#FFA3A3A3"), palette.Text);
    }

    [AvaloniaFact]
    public void CoordinatorTracksActivationAndLiveThemeChanges()
    {
        var application = new Application();
        var themeService = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(temporaryDirectory, "theme.json"))
        );
        var platform = new RecordingWindowChromeThemePlatform();
        var window = new Window();
        using var coordinator = new WindowChromeThemeCoordinator(window, themeService, platform);
        int paletteCountAtOpened = -1;
        window.Opened += (_, _) => paletteCountAtOpened = platform.Palettes.Count;

        try
        {
            window.Show();

            Assert.True(paletteCountAtOpened > 0);
            Assert.Equal(
                WindowChromeThemePalette.Create(themeService.Current, window.IsActive),
                platform.Palettes[paletteCountAtOpened - 1]
            );
            int paletteCountAfterShow = platform.Palettes.Count;

            themeService.Select("orange-dark");

            Assert.Equal(paletteCountAfterShow + 1, platform.Palettes.Count);
            Assert.Equal(WindowChromeThemePalette.Create(themeService.Current, window.IsActive), platform.Palettes[^1]);
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class RecordingWindowChromeThemePlatform : IWindowChromeThemePlatform
    {
        public List<WindowChromeThemePalette> Palettes { get; } = [];

        public void Apply(Window window, WindowChromeThemePalette palette)
        {
            Palettes.Add(palette);
        }
    }
}
