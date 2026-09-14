using System.Globalization;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class BundledAssetImageConverterTests
{
    [Fact]
    public void ReusesTheDecodedBitmapForRepeatedBindings()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "Bodies",
            "earth-like-world.png"
        );
        int openCount = 0;
        object decoded = new object();
        var converter = new BundledAssetImageConverter(
            _ =>
            {
                openCount++;
                return File.OpenRead(path);
            },
            stream =>
            {
                Assert.True(stream.Length > 0);
                return decoded;
            }
        );
        const string asset = "avares://SrvSurvey.Desktop/Assets/Bodies/earth-like-world.png";

        object? first = converter.Convert(asset, typeof(object), null, CultureInfo.InvariantCulture);
        object? second = converter.Convert(asset, typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Same(decoded, first);
        Assert.Same(first, second);
        Assert.Equal(1, openCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Assets/Bodies/earth-like-world.png")]
    [InlineData("https://example.com/body.png")]
    public void RejectsNonAvaloniaResourcePaths(string? value)
    {
        var converter = new BundledAssetImageConverter(
            _ => throw new InvalidOperationException("The asset loader must not run."),
            _ => throw new InvalidOperationException("The asset decoder must not run.")
        );

        object? result = converter.Convert(value, typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Null(result);
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
