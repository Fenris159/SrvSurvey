using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class WaylandScreenSharePromptPresentationTests
{
    [AvaloniaFact]
    public void PromptRecommendsDisplayFirstAndExplainsWindowFallback()
    {
        var dialog = new WaylandScreenSharePromptDialog();
        try
        {
            dialog.Show();
            Assert.NotNull(dialog.CaptureRenderedFrame());

            string text = string.Join(
                '\n',
                dialog
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
            );
            Assert.Contains("Share the display running Elite Dangerous", text, StringComparison.Ordinal);
            Assert.Contains("switch to its display or screen tab", text, StringComparison.Ordinal);
            Assert.Contains("can reduce lag", text, StringComparison.Ordinal);
            Assert.Contains("If detection fails", text, StringComparison.Ordinal);
            Assert.Contains("try the Elite Dangerous window", text, StringComparison.Ordinal);
            Assert.Contains("Alt+Tab", text, StringComparison.Ordinal);
            Assert.Contains("does not transmit your screen", text, StringComparison.Ordinal);

            string?[] buttons = dialog
                .GetVisualDescendants()
                .OfType<Button>()
                .Select(button => button.Content?.ToString())
                .ToArray();
            Assert.Contains("Cancel", buttons);
            Assert.Contains("Continue to picker", buttons);
        }
        finally
        {
            dialog.Close();
        }
    }
}
