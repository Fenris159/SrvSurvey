using SkiaSharp;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class JourneyReportBundleExporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-report-bundle-{Guid.NewGuid():N}");

    [Fact]
    public async Task BundleIncludesOriginalsAndBoundedPreviewsAndSurvivesBeingMoved()
    {
        JourneyHtmlExport report = CreateReport();
        string parent = Path.Combine(directory, "exports");
        string folder = await JourneyReportBundleExporter.SaveAsync(report, parent);
        Assert.Equal(report.Html, await File.ReadAllTextAsync(Path.Combine(folder, "index.html")));
        Assert.Equal(
            await File.ReadAllBytesAsync(report.Images[0].SourcePath),
            await File.ReadAllBytesAsync(Path.Combine(folder, "images", "42", "0001.png"))
        );
        using (var preview = SKBitmap.Decode(Path.Combine(folder, "images", "42", "0001-preview.png")))
        {
            Assert.NotNull(preview);
            Assert.Equal(640, preview.Width);
            Assert.Equal(320, preview.Height);
        }
        string moved = Path.Combine(directory, "shared-on-another-machine");
        Directory.Move(folder, moved);
        Assert.True(File.Exists(Path.Combine(moved, "images", "42", "0001.png")));
        Assert.DoesNotContain(report.Images[0].SourcePath, report.Html, StringComparison.Ordinal);
        string another = await JourneyReportBundleExporter.SaveAsync(report, parent);
        string duplicate = await JourneyReportBundleExporter.SaveAsync(report, parent);
        Assert.NotEqual(another, duplicate);
        Assert.True(File.Exists(Path.Combine(another, "index.html")));
        Assert.True(File.Exists(Path.Combine(duplicate, "index.html")));
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_JOURNEY_BUNDLE_OUTPUT");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            CopyDirectory(moved, output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedImageExportLeavesEarlierReportsIntactAndCleansTheStagingFolder(bool corrupt)
    {
        JourneyHtmlExport report = CreateReport();
        string parent = Path.Combine(directory, "exports");
        string earlier = await JourneyReportBundleExporter.SaveAsync(report, parent);
        string oldHtml = await File.ReadAllTextAsync(Path.Combine(earlier, "index.html"));
        if (corrupt)
        {
            await File.WriteAllTextAsync(report.Images[0].SourcePath, "Broken PNG");
        }
        else
        {
            File.Delete(report.Images[0].SourcePath);
        }
        Exception? failure = await Record.ExceptionAsync(() => JourneyReportBundleExporter.SaveAsync(report, parent));
        Assert.True(failure is IOException or InvalidDataException);
        Assert.Equal([earlier], Directory.GetDirectories(parent));
        Assert.Equal(oldHtml, await File.ReadAllTextAsync(Path.Combine(earlier, "index.html")));
    }

    [Fact]
    public async Task EmptyBundleAndCanceledExportDoNotRequireImages()
    {
        var report = new JourneyHtmlExport("journey-empty.html", "<html>Empty report</html>", []);
        string folder = await JourneyReportBundleExporter.SaveAsync(report, directory);
        Assert.True(Directory.Exists(Path.Combine(folder, "images")));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JourneyReportBundleExporter.SaveAsync(report, directory, cancellation.Token)
        );
        Assert.Equal([folder], Directory.GetDirectories(directory));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            JourneyReportBundleExporter.SaveAsync(report with { FileName = "../outside.html" }, directory)
        );
    }

    private JourneyHtmlExport CreateReport()
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "private-original.png");
        using (var bitmap = new SKBitmap(1600, 800))
        {
            bitmap.Erase(SKColors.MidnightBlue);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.CornflowerBlue };
            canvas.DrawCircle(1000, 400, 230, paint);
            using var image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(source, data.ToArray());
        }
        var picture = new JourneyReportImage(
            "Planet over the horizon.png",
            "images/42/0001.png",
            "images/42/0001-preview.png"
        );
        var second = new JourneyReportImage("A second view.png", "images/42/0002.png", "images/42/0002-preview.png");
        DateTimeOffset at = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        JourneySystemVisit Visit(string name, long address, int hour) =>
            new(
                new(name, address, new(hour * 10, hour * 2, hour * 4)),
                at.AddHours(hour),
                at.AddHours(hour + 1),
                JourneyCounts.Empty with
                {
                    Screenshots = 2,
                    BodyScans = 8,
                    BodyCount = 12,
                    Organisms = 3,
                },
                null,
                null,
                null,
                null,
                null,
                null,
                null
            );
        var journey = new JourneyDocument(
            "sample",
            "private.json",
            "PRIVATE-FID",
            "Drew",
            "The long way home",
            "A field record from the edge of inhabited space.",
            "Journal.test.log",
            at,
            at.AddHours(3),
            at.AddHours(3),
            [Visit("Sol", 42, 0), Visit("Alpha", 43, 1), Visit("Sol", 42, 2)]
        );
        string html = JourneyReport.Html(
            journey,
            new Dictionary<long, string> { [42] = "A quiet landing. Return at sunrise." },
            new Dictionary<long, IReadOnlyList<JourneyReportImage>> { [42] = [picture, second] }
        );
        return new("journey-sample.html", html, [new(source, picture), new(source, second)]);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
