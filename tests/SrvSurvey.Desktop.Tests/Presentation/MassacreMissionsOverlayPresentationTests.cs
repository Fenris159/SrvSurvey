using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MassacreMissionsOverlayPresentationTests
{
    [AvaloniaTheory]
    [InlineData(6)]
    [InlineData(3)]
    [InlineData(0)]
    public void MissionShowsCompletedOverTotalAndTheCommodityCompletionCheckmark(int remaining)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "srvsurvey-massacre-preview-" + Guid.NewGuid().ToString("N")
        );
        var combat = new CombatViewModel(
            new CombatSettingsStore(Path.Combine(directory, "ui.json")),
            new CommanderProfileStore(directory)
        );
        combat.LoadProfile(
            null,
            null,
            true,
            new CombatSnapshot([new(123, "Mission giver", "Target faction", null, 6, remaining)])
        );
        var panel = new MassacreMissionsOverlayPresentation
        {
            DataContext = new CombatOverlayViewModel(
                combat,
                OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows)
            ),
        };
        var window = new Window { Content = panel, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            TextBlock[] text = panel.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(text, block => block.Text == $"{6 - remaining}/6");
            Assert.DoesNotContain(text, block => block.Text == "COMPLETE");
            TextBlock check = Assert.Single(text, block => block.Name == "MissionCompletionCheckmark");
            Assert.Equal("✓", check.Text);
            Assert.Equal(remaining == 0, check.IsEffectivelyVisible);
            Assert.Contains("Noto Sans Symbols 2", check.FontFamily.Name);
            // Both presentations resolve the same theme brush instead of hard-coding a second green.
            Assert.True(window.TryFindResource("RavenColoniseSurplusBrush", out object? expected));
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(check.Foreground).Color
            );
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-massacre-{6 - remaining}-of-6.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
