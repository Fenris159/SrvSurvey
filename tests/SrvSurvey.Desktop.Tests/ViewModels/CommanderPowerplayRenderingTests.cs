using Avalonia;
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
    [AvaloniaTheory]
    [InlineData("Aisling Duval", "#0099FF")]
    [InlineData("Archon Delaine", "#FF0000")]
    [InlineData("A. Lavigny-Duval", "#7F00FF")]
    [InlineData("Denton Patreus", "#00FFFF")]
    [InlineData("Edmund Mahon", "#019C00")]
    [InlineData("Felicia Winters", "#FFC400")]
    [InlineData("Jerome Archer", "#DF1DE4")]
    [InlineData("Li Yong-Rui", "#33D688")]
    [InlineData("Nakato Kaine", "#A3F127")]
    [InlineData("Pranav Antal", "#FFFF00")]
    [InlineData("Yuri Grom", "#FF8000")]
    [InlineData("Zachary Hudson", "#FF0000")]
    [InlineData("Zemina Torval", "#0040FF")]
    public async Task PowerplayCardRendersThePowerAndModernRankWithoutClipping(string power, string colorHex)
    {
        FrontierAccountSnapshot snapshot = CreateSnapshot(DateTimeOffset.UnixEpoch) with
        {
            Ranks = [new("power", "Powerplay", 0, "None")],
            Powerplay = new(power, 160, 1260198, DateTimeOffset.UnixEpoch),
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
            Assert.Contains(labels, label => label.Text == power);
            TextBlock detail = Assert.Single(labels, label => label.Text == "Rank 160");
            TextBlock merits = Assert.Single(labels, label => label.Text == "1,260,198 merits");
            Assert.True(merits.IsVisible);
            Image rankBadge = Assert.Single(
                card.GetVisualDescendants().OfType<Image>(),
                image => image.IsVisible && image.Width is > 0 and <= 28
            );
            Assert.NotNull(rankBadge.Source);
            Assert.InRange(rankBadge.Bounds.Width, 27.9, 28.1);
            Assert.InRange(rankBadge.Bounds.Height, 27.9, 28.1);
            Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(rankBadge));
            Point badgePosition = rankBadge.TranslatePoint(default, card)!.Value;
            Point rankPosition = detail.TranslatePoint(default, card)!.Value;
            Assert.True(badgePosition.X > card.Bounds.Width / 2);
            Assert.True(badgePosition.Y < rankPosition.Y);
            Assert.InRange(badgePosition.X + rankBadge.Bounds.Width, card.Bounds.Width - 20, card.Bounds.Width);
            Assert.EndsWith("/powerplay/rank-5.png", Assert.Single(model.Ranks).RankBadgeIconPath);
            Assert.True(detail.IsVisible);
            Assert.True(detail.Bounds.Height > 0);
            Assert.True(card.Bounds.Height >= card.DesiredSize.Height - card.Margin.Top - card.Margin.Bottom - 0.1);
            Assert.Contains("Frontier journal", Assert.IsType<string>(ToolTip.GetTip(card)));
            Border emblem = Assert.Single(
                card.GetVisualDescendants().OfType<Border>(),
                border => border.IsVisible && border.OpacityMask is ImageBrush
            );
            var color = Color.Parse(colorHex);
            Assert.Equal(color, Assert.IsAssignableFrom<ISolidColorBrush>(emblem.Background).Color);
            using var pixels = new MemoryStream();
            frame.Save(pixels, PngBitmapEncoderOptions.Default);
            using var bitmap = SKBitmap.Decode(pixels.ToArray());
            Assert.True(
                bitmap.Pixels.Count(pixel =>
                    Math.Abs(pixel.Red - color.R) < 8
                    && Math.Abs(pixel.Green - color.G) < 8
                    && Math.Abs(pixel.Blue - color.B) < 8
                ) > 40,
                $"The {power} emblem was not visible in its power color in the rendered card."
            );

            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_POWERPLAY_RENDER_OUTPUT");
            if (power == "A. Lavigny-Duval" && !string.IsNullOrWhiteSpace(output))
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
