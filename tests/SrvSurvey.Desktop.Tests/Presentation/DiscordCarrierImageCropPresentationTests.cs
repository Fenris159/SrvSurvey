using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Presentation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiscordCarrierImageCropPresentationTests
{
    [AvaloniaFact]
    public void DraggingZoomingAndArrowKeysMoveTheImageWithinTheFixedFrame()
    {
        using var stream = new MemoryStream(Quadrants());
        using var bitmap = new Bitmap(stream);
        var selector = new CarrierImageCropSelector
        {
            Source = bitmap,
            Crop = new(2),
            Focusable = true,
        };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = selector,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            window.MouseDown(new Point(200, 150), MouseButton.Left);
            window.MouseMove(new Point(292, 150));
            window.MouseUp(new Point(292, 150), MouseButton.Left);
            Assert.Equal(0.375, selector.Crop.CenterX, 6);
            Assert.Equal(0.5, selector.Crop.CenterY, 6);
            window.MouseMove(new Point(310, 150));
            Assert.Equal(0.375, selector.Crop.CenterX, 6);
            window.MouseWheel(new Point(200, 150), new Vector(0, 1));
            Assert.Equal(2.2, selector.Crop.Zoom, 6);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.Equal(0.365, selector.Crop.CenterX, 6);
            window.MouseDown(new Point(200, 150), MouseButton.Left);
            window.MouseMove(new Point(3000, 150));
            window.MouseUp(new Point(3000, 150), MouseButton.Left);
            Assert.Equal(0, selector.Crop.GetRectangle(320, 180).X, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ImageSelectorShowsTheCropAndTheMessagePreviewTracksZoomAndReset()
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-crop-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "carrier.png");
        await File.WriteAllBytesAsync(path, Quadrants());
        var model = new DiscordCarrierMessageEditorViewModel(new(), previewTime: DateTimeOffset.UnixEpoch)
        {
            IncludeDetails = false,
        };
        var window = new DiscordCarrierMessageEditorWindow(model, _ => { });
        try
        {
            window.Show();
            model.SelectImage(path);
            await window.PendingImage;
            window.UpdateLayout();
            StackPanel panel = window.FindControl<StackPanel>("ImageCropPanel")!;
            panel.BringIntoView();
            window.UpdateLayout();
            CarrierImageCropSelector selector = window.FindControl<CarrierImageCropSelector>("ImageCropSelector")!;
            Assert.True(panel.IsVisible);
            Assert.Equal(new PixelSize(320, 180), selector.Source!.PixelSize);
            Slider zoom = window.FindControl<Slider>("ImageZoomSlider")!;
            zoom.Value = 2;
            Assert.Equal(2, selector.Crop!.Zoom);
            CarrierCroppedImage preview = Assert.IsType<CarrierCroppedImage>(
                window.FindControl<Image>("CarrierImage")!.Source
            );
            Assert.Equal(new Size(1280, 720), preview.Size);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_DISCORD_CROP_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, "carrier-crop-editor.png"), PngBitmapEncoderOptions.Default);
            }
            window.FindControl<Button>("ResetImageCropButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new DiscordCarrierImageCrop(), model.ImageCrop);
            Assert.Equal(1, zoom.Value);
        }
        finally
        {
            window.Close();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public void ThePreviewAndExportBothShowTheSelectedRegionWithoutChangingTheSource()
    {
        byte[] bytes = Quadrants();
        byte[] before = bytes.ToArray();
        var original = new DiscordCarrierImage(bytes, "original.png", "image/png");
        var crop = new DiscordCarrierImageCrop(2, 0.75, 0.75);
        DiscordCarrierImage exported = DiscordCarrierImageProcessor.Crop(original, crop);
        using var decoded = SKBitmap.Decode(exported.Bytes);
        Assert.Equal(1280, decoded.Width);
        Assert.Equal(720, decoded.Height);
        Assert.Equal(SKColors.Cyan, decoded.GetPixel(5, 5));
        Assert.Equal(SKColors.Cyan, decoded.GetPixel(1274, 714));
        Assert.Equal(before, bytes);
        using var stream = new MemoryStream(bytes);
        using var bitmap = new Bitmap(stream);
        var image = new Image
        {
            Source = new CarrierCroppedImage(bitmap, crop),
            Width = 320,
            Height = 180,
        };
        image.Measure(new Size(320, 180));
        image.Arrange(new Rect(0, 0, 320, 180));
        using var render = new RenderTargetBitmap(new PixelSize(320, 180));
        render.Render(image);
        using var png = new MemoryStream();
        render.Save(png, PngBitmapEncoderOptions.Default);
        using var preview = SKBitmap.Decode(png.ToArray());
        Assert.Equal(SKColors.Cyan, preview.GetPixel(5, 5));
        Assert.Equal(SKColors.Cyan, preview.GetPixel(314, 174));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnouncementsUploadTheSavedCropForTheMatchingCarrier(bool squadron)
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-crop-send-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "carrier.png");
        byte[] original = Quadrants();
        await File.WriteAllBytesAsync(path, original);
        byte[]? delivered = null;
        using var handler = new Handler(async request =>
        {
            HttpContent[] parts = Assert.IsType<MultipartFormDataContent>(request.Content).ToArray();
            delivered = await parts[1].ReadAsByteArrayAsync();
            return new(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(http)
        );
        try
        {
            DiscordCarrierAnnouncementsViewModel announcements = main.DiscordCarrierAnnouncements;
            announcements.Enabled = true;
            announcements.WebhookUrl = "https://discord.com/api/webhooks/123/test-token";
            var crop = new DiscordCarrierImageCrop(2, 0.75, 0.75);
            announcements.ApplyMessageSettings(
                new(
                    PersonalImagePath: squadron ? "missing.png" : path,
                    SquadronImagePath: squadron ? path : "missing.png",
                    PersonalImageCrop: squadron ? null : crop,
                    SquadronImageCrop: squadron ? crop : null
                )
            );
            await announcements.SaveAsync();
            Assert.True(
                JournalEventEnvelope.TryParse(
                    $$"""{"timestamp":"2026-11-14T12:00:00Z","event":"CarrierJumpRequest","CarrierID":1,"CarrierType":"{{(squadron ? "SquadronCarrier" : "FleetCarrier")}}","SystemName":"Lave","DepartureTime":"2026-11-14T12:15:00Z"}""",
                    out JournalEventEnvelope? entry,
                    out _
                )
            );
            announcements.ApplyJournalEvents("F1", [entry!], true);
            await announcements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            using var result = SKBitmap.Decode(Assert.IsType<byte[]>(delivered));
            Assert.Equal(1280, result.Width);
            Assert.Equal(720, result.Height);
            Assert.Equal(SKColors.Cyan, result.GetPixel(5, 5));
            Assert.Equal(SKColors.Cyan, result.GetPixel(1274, 714));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            await main.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    private static byte[] Quadrants()
    {
        using var bitmap = new SKBitmap(320, 180);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            canvas.Clear(SKColors.Red);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(new SKRect(160, 0, 320, 90), paint);
            paint.Color = SKColors.Yellow;
            canvas.DrawRect(new SKRect(0, 90, 160, 180), paint);
            paint.Color = SKColors.Cyan;
            canvas.DrawRect(new SKRect(160, 90, 320, 180), paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request);
    }
}
