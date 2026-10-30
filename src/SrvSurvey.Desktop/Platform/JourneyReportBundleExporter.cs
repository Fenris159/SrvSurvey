using SkiaSharp;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Desktop.Platform;

public sealed record JourneyReportAsset(string SourcePath, JourneyReportImage Image);

public sealed record JourneyHtmlExport(string FileName, string Html, IReadOnlyList<JourneyReportAsset> Images);

/// <summary>Publishes a complete report folder only after its HTML, images and previews are ready.</summary>
public static class JourneyReportBundleExporter
{
    public static Task<string> SaveAsync(
        JourneyHtmlExport report,
        string destination,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        string folderName = Path.GetFileNameWithoutExtension(report.FileName);
        if (
            string.IsNullOrWhiteSpace(folderName)
            || report.FileName.Any(character => character < ' ' || "<>:\"/\\|?*".Contains(character))
            || folderName.EndsWith('.')
        )
        {
            throw new ArgumentException("The report folder name is invalid.", nameof(report));
        }
        return Task.Run(
            () => SaveCoreAsync(report, Path.GetFullPath(destination), folderName, cancellationToken),
            cancellationToken
        );
    }

    private static async Task<string> SaveCoreAsync(
        JourneyHtmlExport report,
        string destination,
        string folderName,
        CancellationToken cancellationToken
    )
    {
        string temporary = Path.Combine(destination, $".journey-export-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary, "images"));
            foreach (JourneyReportAsset asset in report.Images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string original = Path.Combine(
                    temporary,
                    asset.Image.ImagePath.Replace('/', Path.DirectorySeparatorChar)
                );
                string preview = Path.Combine(
                    temporary,
                    asset.Image.PreviewPath.Replace('/', Path.DirectorySeparatorChar)
                );
                Directory.CreateDirectory(Path.GetDirectoryName(original) ?? temporary);
                Directory.CreateDirectory(Path.GetDirectoryName(preview) ?? temporary);
                await CopyImageAsync(asset.SourcePath, original, cancellationToken).ConfigureAwait(false);
                WritePreview(original, preview, asset.Image.Caption);
            }
            await File.WriteAllTextAsync(Path.Combine(temporary, "index.html"), report.Html, cancellationToken)
                .ConfigureAwait(false);
            return PublishFolder(temporary, destination, folderName, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static async Task CopyImageAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true
        );
        await using var output = new FileStream(
            target,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true
        );
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void WritePreview(string source, string target, string caption)
    {
        using SKBitmap original =
            SKBitmap.Decode(source) ?? throw new InvalidDataException($"Screenshot '{caption}' could not be decoded.");
        double scale = Math.Min(1, Math.Min(640d / original.Width, 400d / original.Height));
        var size = new SKImageInfo(
            Math.Max(1, (int)(original.Width * scale)),
            Math.Max(1, (int)(original.Height * scale))
        );
        using SKBitmap preview =
            original.Resize(size, new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidDataException("The screenshot preview could not be created.");
        using var image = SKImage.FromBitmap(preview);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static string PublishFolder(
        string temporary,
        string destination,
        string folderName,
        CancellationToken cancellationToken
    )
    {
        int number = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = number == 1 ? folderName : $"{folderName}-{number}";
            string target = Path.Combine(destination, name);
            try
            {
                Directory.Move(temporary, target);
                return target;
            }
            catch (IOException) when (Directory.Exists(target) || File.Exists(target))
            {
                // Keep earlier exports intact and retry with a distinct name.
                number++;
            }
        }
    }
}
