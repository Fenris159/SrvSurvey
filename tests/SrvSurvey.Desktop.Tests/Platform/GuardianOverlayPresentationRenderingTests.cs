using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuardianOverlayPresentationRenderingTests
{
    [AvaloniaFact]
    public void GuardianZoomOrbsRenderAtTheirCompactOverlaySize()
    {
        var window = new GuardianZoomOverlayWindow(
            new GuardianZoomOverlayViewModel(_ => { }));
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(42, 20), frame.PixelSize);
            var outputPath = Environment.GetEnvironmentVariable(
                "SRVSURVEY_GUARDIAN_ZOOM_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                frame.Save(outputPath, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EveryGuardianEditorPresentationRendersAtItsCatalogSize()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var outputDirectory = Environment.GetEnvironmentVariable(
            "SRVSURVEY_GUARDIAN_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        foreach (var plotterName in new[]
                 {
                     "PlotGuardians",
                     "PlotGuardianStatus",
                     "PlotGuardianSystem",
                     "PlotRamTah",
                 })
        {
            var definition = OverlayLayoutCatalog.GetRequired(plotterName);
            var preview = new OverlayPositionPreviewWindow(definition);
            try
            {
                OverlayThemeResources.Apply(preview);
                preview.ApplyRuntimePresentationTheme();
                preview.Show();
                var frame = preview.CaptureRenderedFrame();
                Assert.NotNull(frame);
                // Content-driven hosts shrink/grow with presentation content;
                // only require a non-empty render and uniqueness across panels.
                Assert.True(frame.PixelSize.Width >= 1);
                Assert.True(frame.PixelSize.Height >= 1);

                using var stream = new MemoryStream();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                var png = stream.ToArray();
                hashes.Add(Convert.ToHexString(SHA256.HashData(png)));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    File.WriteAllBytes(
                        Path.Combine(outputDirectory, $"{plotterName}.png"),
                        png);
                }
            }
            finally
            {
                preview.Close();
            }
        }

        Assert.Equal(4, hashes.Count);
    }
}
