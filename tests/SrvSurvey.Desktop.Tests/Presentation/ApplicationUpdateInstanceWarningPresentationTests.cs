using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ApplicationUpdateInstanceWarningPresentationTests
{
    [AvaloniaFact]
    public void WarningExplainsTheInstanceCountAndClosureBeforeDownload()
    {
        var dialog = new MultipleApplicationInstancesDialog(2);
        try
        {
            dialog.Show();
            Assert.NotNull(dialog.CaptureRenderedFrame());

            var text = dialog.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            Assert.Contains(
                "3 SrvSurvey instances are currently running.",
                text);
            Assert.Contains(
                "Are you sure you want to proceed? Doing so will close all instances.",
                text);
            Assert.Contains(text, value => value!.Contains(
                "before downloading begins",
                StringComparison.Ordinal));

            var buttons = dialog.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => button.Content?.ToString())
                .ToArray();
            Assert.Contains("Cancel", buttons);
            Assert.Contains("Close all and continue", buttons);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void WarningRequiresAtLeastOneOtherInstance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MultipleApplicationInstancesDialog(0));
    }

    [AvaloniaFact]
    public void WarningExplainsWhenWindowsCouldNotVerifyAMatchingProcess()
    {
        var dialog = new MultipleApplicationInstancesDialog(
            otherInstanceCount: 2,
            unverifiedInstanceCount: 1);
        try
        {
            dialog.Show();
            Assert.NotNull(dialog.CaptureRenderedFrame());

            var text = dialog.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(block => block.IsVisible)
                .Select(block => block.Text)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            Assert.Contains(text, value => value!.Contains(
                "prevented verification of 1 matching process",
                StringComparison.Ordinal));
            Assert.Contains(text, value => value!.Contains(
                "will not force-close an unverified process",
                StringComparison.Ordinal));
        }
        finally
        {
            dialog.Close();
        }
    }
}
