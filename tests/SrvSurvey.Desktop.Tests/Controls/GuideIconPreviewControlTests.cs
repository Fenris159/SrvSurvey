using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuideIconPreviewControlTests
{
    [AvaloniaFact]
    public void EveryDocumentedIconKindRendersToTheHeadlessSurface()
    {
        foreach (var kind in Enum.GetValues<GuideIconKind>())
        {
            var control = CreateControl(kind, "✓");

            Assert.True(Render(control), $"{kind} rendered no visible pixels.");
        }
    }

    [AvaloniaFact]
    public void GlyphPaletteAndFallbackSymbolsAllRender()
    {
        string[] symbols =
        [
            "✓",
            "⚠",
            "?",
            "⚑",
            "⚐",
            "☀",
            "◆",
            "◇",
            "▲",
            "+",
            "!",
            "■",
            "►",
            "AB",
        ];

        Assert.All(symbols, symbol => Assert.True(
            Render(CreateControl(GuideIconKind.Glyph, symbol)),
            $"Glyph '{symbol}' rendered no visible pixels."));
    }

    private static GuideIconPreviewControl CreateControl(
        GuideIconKind kind,
        string symbol)
    {
        return new GuideIconPreviewControl
        {
            Kind = kind,
            Symbol = symbol,
            BackgroundBrush = Brushes.MidnightBlue,
            PrimaryBrush = Brushes.OrangeRed,
            SecondaryBrush = Brushes.DeepSkyBlue,
            MutedBrush = Brushes.SlateGray,
            SuccessBrush = Brushes.LimeGreen,
            WarningBrush = Brushes.Gold,
            DangerBrush = Brushes.Red,
            GoldBrush = Brushes.Goldenrod,
            PipConfirmedBrush = Brushes.Orange,
            PipConfirmedDimBrush = Brushes.DarkOrange,
            PipPotentialBrush = Brushes.SaddleBrown,
            PipPredictionBrush = Brushes.Gold,
            PipPredictionPotentialBrush = Brushes.DarkGoldenrod,
            PipHighlightBrush = Brushes.Yellow,
            PipGlobalRegionalBrush = Brushes.White,
            PipGlobalRegionalPotentialBrush = Brushes.Gray,
            PipUnknownBrush = Brushes.Gray,
            PipUnknownGlyphBrush = Brushes.LightGray,
            PipHatchBrush = Brushes.Black,
            PipEmptyBrush = Brushes.DarkSlateGray,
        };
    }

    private static bool Render(GuideIconPreviewControl control)
    {
        var size = new Size(96, 96);
        var window = new Window
        {
            Width = size.Width,
            Height = size.Height,
            Content = control,
        };

        try
        {
            window.Show();
            var frame = window.CaptureRenderedFrame();
            return frame?.PixelSize == new PixelSize(96, 96);
        }
        finally
        {
            window.Close();
        }
    }
}
