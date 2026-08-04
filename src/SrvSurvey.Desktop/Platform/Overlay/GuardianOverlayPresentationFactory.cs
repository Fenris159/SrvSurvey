using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class GuardianOverlayPresentationFactory
{
    public static bool IsSupported(string plotterName) => plotterName is
        "PlotGuardians"
        or "PlotGuardianStatus"
        or "PlotGuardianSystem"
        or "PlotRamTah";

    public static bool TryCreate(string plotterName, out Control? presentation)
    {
        presentation = plotterName switch
        {
            "PlotGuardians" => new GuardianSiteOverlayPresentation(),
            "PlotGuardianStatus" => new GuardianStatusOverlayPresentation(),
            "PlotGuardianSystem" => new GuardianSystemOverlayPresentation(),
            "PlotRamTah" => new RamTahOverlayPresentation(),
            _ => null,
        };
        return presentation is not null;
    }
}
