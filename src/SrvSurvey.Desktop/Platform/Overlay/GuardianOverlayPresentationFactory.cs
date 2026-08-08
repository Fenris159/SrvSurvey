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
        // Prefer the unified runtime presentation factory so Guardian stays
        // on the same path as every other shared overlay template.
        if (OverlayRuntimePresentationFactory.IsSupported(plotterName))
        {
            presentation = OverlayRuntimePresentationFactory.CreatePresentation(
                plotterName);
            return presentation is not null;
        }

        presentation = null;
        return false;
    }
}
