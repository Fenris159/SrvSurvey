using Avalonia.Media;

namespace SrvSurvey.Desktop;

internal static class SrvSurveyFontConfiguration
{
    private const string FontRoot = "avares://SrvSurvey.Desktop/Assets/Fonts";

    internal static FontManagerOptions CreateOptions() =>
        new()
        {
            FontFallbacks =
            [
                CreateFallback("NotoSansSymbols", "Noto Sans Symbols", "U+2690,U+2691"),
                CreateFallback(
                    "NotoSansSymbols2",
                    "Noto Sans Symbols 2",
                    "U+23F3,U+25C6,U+25C7,U+2600,U+270B,U+2713,U+2715,U+1F30E"
                ),
                CreateFallback("NotoColorEmoji", "Noto Color Emoji", "U+1F4E1,U+1F680"),
            ],
        };

    private static FontFallback CreateFallback(string directory, string family, string unicodeRange) =>
        new()
        {
            FontFamily = new FontFamily($"{FontRoot}/{directory}#{family}"),
            UnicodeRange = UnicodeRange.Parse(unicodeRange),
        };
}
