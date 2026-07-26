using SkiaSharp;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class FssTuningDiagnosticWriter
{
    public static string Save(
        string directory,
        CapturedPixelBuffer capture,
        long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(capture);
        Directory.CreateDirectory(directory);

        var imageInfo = new SKImageInfo(
            capture.Width,
            capture.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(imageInfo);
        var bytes = capture.BgraPixels.ToArray();
        System.Runtime.InteropServices.Marshal.Copy(
            bytes,
            0,
            bitmap.GetPixels(),
            bytes.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException(
                "The FSS diagnostic image could not be encoded.");
        var fileName = "WatchFSS-"
            + DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff")
            + $"-{revision}.png";
        var path = Path.Combine(directory, fileName);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
        data.SaveTo(stream);
        return path;
    }
}
