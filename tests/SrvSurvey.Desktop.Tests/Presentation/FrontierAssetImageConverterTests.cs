using SkiaSharp;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class FrontierAssetImageConverterTests
{
    [Theory]
    [InlineData("Assets/Frontier/Ranks/exploration/rank-9.png")]
    [InlineData("Assets/Frontier/Factions/federation.png")]
    public void BundledFrontierAssetIsAValidImage(string relativePath)
    {
        var desktopRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop");
        var path = Path.Combine(
            desktopRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        using var image = SKBitmap.Decode(path);

        Assert.NotNull(image);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }

    [Fact]
    public void DynamicFrontierImagesUseTheRuntimeAssetConverter()
    {
        var views = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Views");
        var commander = File.ReadAllText(
            Path.Combine(views, "FrontierCommanderTabView.axaml"));

        Assert.Equal(3, CountOccurrences(
            commander,
            "Converter={StaticResource FrontierAssetImageConverter}"));
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
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
