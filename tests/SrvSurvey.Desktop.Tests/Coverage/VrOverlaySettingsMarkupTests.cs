using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class VrOverlaySettingsMarkupTests
{
    [Fact]
    public void VrCardPresentsConnectionPairingStatusAndCalibrationInOrder()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", "OverlaySettingsView.axaml")
        );
        string markup = document.ToString();

        int choose = markup.IndexOf("1 · CHOOSE YOUR HEADSET ROUTE", StringComparison.Ordinal);
        int pair = markup.IndexOf("2 · PAIR AND START", StringComparison.Ordinal);
        int verify = markup.IndexOf("3 · ENABLE AND VERIFY", StringComparison.Ordinal);
        int calibration = markup.IndexOf("CALIBRATION TARGET", StringComparison.Ordinal);

        Assert.True(choose >= 0);
        Assert.True(pair > choose);
        Assert.True(verify > pair);
        Assert.True(calibration > verify);
        Assert.Contains("VrOverlay.PlatformProfiles", markup, StringComparison.Ordinal);
        Assert.Contains("VrOverlay.ConnectionStateLabel", markup, StringComparison.Ordinal);
        Assert.Contains("VrOverlay.IsCustomRuntime", markup, StringComparison.Ordinal);
        int desktopInteraction = markup.IndexOf("Live-overlay interaction shortcut", StringComparison.Ordinal);
        int vrInteraction = markup.IndexOf("Toggle VR overlay interaction", StringComparison.Ordinal);
        Assert.True(vrInteraction > desktopInteraction);
        Assert.Contains("VrOverlayInteractionBinding.Chord", markup, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
