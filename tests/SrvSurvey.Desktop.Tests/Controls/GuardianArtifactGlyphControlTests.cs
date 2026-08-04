using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuardianArtifactGlyphControlTests
{
    [AvaloniaFact]
    public void ArtifactCodesRenderWithoutThrowing()
    {
        string?[] codes =
        [
            null,
            string.Empty,
            "unknown",
            "ca",
            "casket",
            "or",
            "orb",
            "ta",
            "tablet",
            "to",
            "totem",
            "ur",
            "urn",
            "re",
            "relic",
        ];

        foreach (var code in codes)
        {
            var control = new GuardianArtifactGlyphControl
            {
                ArtifactCode = code,
                Width = 24,
                Height = 24,
            };
            var window = new Window
            {
                Width = 64,
                Height = 64,
                Content = control,
            };

            try
            {
                window.Show();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(64, 64), frame.PixelSize);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
