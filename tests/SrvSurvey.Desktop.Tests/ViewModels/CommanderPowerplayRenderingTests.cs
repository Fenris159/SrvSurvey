using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SkiaSharp;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed partial class CommanderProfileViewModelTests
{
    [AvaloniaFact]
    public async Task PowerplayCardRendersThePowerAndModernRankWithoutClipping()
    {
        FrontierAccountSnapshot snapshot = CreateSnapshot(DateTimeOffset.UnixEpoch) with
        {
            Ranks = [new("power", "Powerplay", 0, "None")],
            Powerplay = new("A. Lavigny-Duval", 160, 1260198, DateTimeOffset.UnixEpoch),
        };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(true, snapshot, snapshot.FetchedAt))
        );
        await model.OpenAsync();
        var view = new FrontierCommanderTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 900,
            Height = 900,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Border card = Assert.Single(
                view.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("rank-card")
            );
            TextBlock[] labels = card.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(labels, label => label.Text == "A. Lavigny-Duval");
            TextBlock detail = Assert.Single(labels, label => label.Text == "Rank 160 · 1,260,198 merits");
            Assert.True(detail.IsVisible);
            Assert.True(detail.Bounds.Height > 0);
            Assert.True(card.Bounds.Height >= card.DesiredSize.Height - card.Margin.Top - card.Margin.Bottom - 0.1);
            Assert.Contains("Frontier journal", Assert.IsType<string>(ToolTip.GetTip(card)));
            Border emblem = Assert.Single(
                card.GetVisualDescendants().OfType<Border>(),
                border => border.IsVisible && border.OpacityMask is ImageBrush
            );
            Assert.Equal(Color.Parse("#7F00FF"), Assert.IsAssignableFrom<ISolidColorBrush>(emblem.Background).Color);
            using var pixels = new MemoryStream();
            frame.Save(pixels, PngBitmapEncoderOptions.Default);
            using var bitmap = SKBitmap.Decode(pixels.ToArray());
            Assert.True(
                bitmap.Pixels.Count(pixel => pixel.Blue > 180 && pixel.Blue - pixel.Red > 60 && pixel.Green < 80) > 40,
                "The pledged power's purple emblem was not visible in the rendered card."
            );

            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_POWERPLAY_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
