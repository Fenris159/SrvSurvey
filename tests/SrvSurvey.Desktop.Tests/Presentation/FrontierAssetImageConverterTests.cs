using SkiaSharp;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class FrontierAssetImageConverterTests
{
    [Theory]
    [InlineData("Assets/Frontier/Ranks/exploration/rank-9.png")]
    [InlineData("Assets/Frontier/Factions/federation.png")]
    [InlineData("Assets/Frontier/Powers/aisling-duval.png")]
    [InlineData("Assets/Frontier/Powers/archon-delaine.png")]
    [InlineData("Assets/Frontier/Powers/arissa-lavigny-duval.png")]
    [InlineData("Assets/Frontier/Powers/denton-patreus.png")]
    [InlineData("Assets/Frontier/Powers/edmund-mahon.png")]
    [InlineData("Assets/Frontier/Powers/felicia-winters.png")]
    [InlineData("Assets/Frontier/Powers/li-yong-rui.png")]
    [InlineData("Assets/Frontier/Powers/pranav-antal.png")]
    [InlineData("Assets/Frontier/Powers/yuri-grom.png")]
    [InlineData("Assets/Frontier/Powers/zachary-hudson.png")]
    [InlineData("Assets/Frontier/Powers/zemina-torval.png")]
    public void BundledFrontierAssetIsAValidImage(string relativePath)
    {
        string desktopRoot = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop");
        string path = Path.Combine(desktopRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        using var image = SKBitmap.Decode(path);

        Assert.NotNull(image);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
        Assert.Contains(image.Pixels, pixel => pixel.Alpha > 0);
        if (relativePath.Contains("/Powers/", StringComparison.Ordinal))
        {
            Assert.Contains(image.Pixels, pixel => pixel.Alpha == 0);
        }
    }

    [Fact]
    public void DynamicFrontierImagesUseTheRuntimeAssetConverter()
    {
        string views = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views");
        string commander = File.ReadAllText(Path.Combine(views, "FrontierCommanderTabView.axaml"));

        Assert.Equal(4, CountOccurrences(commander, "Converter={StaticResource FrontierAssetImageConverter}"));
    }

    private static int CountOccurrences(string value, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
