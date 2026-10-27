using Avalonia.Media;

namespace SrvSurvey.Desktop.Theming;

/// <summary>Stable carrier colors shared by bars and legends, with no fixed palette-size limit.</summary>
public static class CarrierColorPalette
{
    public static Color GetColor(long marketId, bool isDark)
    {
        // Fibonacci hashing separates nearby carrier IDs around the hue wheel without per-refresh randomness.
        ulong seed = unchecked((ulong)marketId * 11400714819323198485UL);
        double hue = (seed >> 11) * (360d / (1UL << 53));
        return new HslColor(1, hue, 0.60, isDark ? 0.76 : 0.33).ToRgb();
    }
}
