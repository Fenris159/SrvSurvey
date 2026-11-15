using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Presentation;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

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
    public void ZoomingDuringAPanKeepsTheNewZoomWhenDraggingContinues()
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
            window.MouseWheel(new Point(200, 150), new Vector(0, 1));
            window.MouseMove(new Point(280.96, 150));
            window.MouseUp(new Point(280.96, 150), MouseButton.Left);
            Assert.Equal(2.2, selector.Crop.Zoom, 6);
            Assert.Equal(0.4, selector.Crop.CenterX, 6);
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

    [AvaloniaFact]
    public async Task AppliedAndSavedMessageAndImageArePostedAfterRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "carrier-editor-restart-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "carrier.png");
        await File.WriteAllBytesAsync(path, Quadrants());
        string journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        string journal = Path.Combine(journals, "Journal.2026-11-14T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"2026-11-14T12:00:00Z","event":"Fileheader","Odyssey":true,"gameversion":"4.0.0.1"}
            {"timestamp":"2026-11-14T12:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-11-14T12:00:02Z","event":"LoadGame","Commander":"Drew","FID":"F123","Ship":"CobraMkIII","GameMode":"Solo"}
            {"timestamp":"2026-11-14T12:00:03Z","event":"CarrierStats","CarrierID":1,"Name":"Explorer","Callsign":"ABC-123"}
            {"timestamp":"2026-11-14T12:00:04Z","event":"CarrierLocation","CarrierID":1,"StarSystem":"Sol"}
            """ + "\n"
        );
        byte[]? deliveredImage = null;
        string? deliveredTitle = null;
        string? deliveredImageUrl = null;
        using var handler = new Handler(async request =>
        {
            if (request.RequestUri!.Host != "discord.com")
            {
                return new(HttpStatusCode.NotFound);
            }
            string json;
            if (request.Content is MultipartFormDataContent multipart)
            {
                HttpContent[] parts = multipart.ToArray();
                json = await parts[0].ReadAsStringAsync();
                deliveredImage = await parts[1].ReadAsByteArrayAsync();
            }
            else
            {
                json = await request.Content!.ReadAsStringAsync();
            }
            using var payload = JsonDocument.Parse(json);
            JsonElement embed = payload.RootElement.GetProperty("embeds")[0];
            deliveredTitle = embed.GetProperty("title").GetString();
            deliveredImageUrl = embed.TryGetProperty("image", out JsonElement image)
                ? image.GetProperty("url").GetString()
                : null;
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new
                    {
                        id = "456",
                        embeds = new[]
                        {
                            new
                            {
                                title = deliveredTitle,
                                image = new
                                {
                                    url = "https://cdn.discordapp.com/attachments/123/456/carrier.png?private-signature",
                                },
                            },
                        },
                        attachments = Array.Empty<object>(),
                    }
                ),
            };
        });
        using var http = new HttpClient(handler);
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        )
        {
            SettingsFrontierId = "F123",
        };
        var applicationLog = new ApplicationLogService(paths.DataDirectory);
        MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            journals,
            builder =>
                builder
                    .WithAppDataPaths(paths)
                    .WithExternalNetworkClient(http)
                    .WithApplicationLogService(applicationLog)
        );
        var view = new SettingsView { DataContext = main };
        var owner = new Window { Content = view };
        try
        {
            await main.RefreshAsync();
            main.DiscordCarrierAnnouncements.Enabled = true;
            main.DiscordCarrierAnnouncements.WebhookUrl = "https://discord.com/api/webhooks/123/test-token";
            owner.Show();
            main.SettingsWorkspace.SelectCategory("application");
            view.FindControl<Button>("EditDiscordCarrierMessageButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DiscordCarrierMessageEditorWindow editor = Assert.Single(
                owner.OwnedWindows.OfType<DiscordCarrierMessageEditorWindow>()
            );
            editor.FindControl<TextBox>("TitleEditor")!.Text = "Departure to {destination}";
            DiscordCarrierMessageEditorViewModel model = Assert.IsType<DiscordCarrierMessageEditorViewModel>(
                editor.DataContext
            );
            model.SelectImage(path);
            model.ImageCrop = new(2, 0.75, 0.75);
            await editor.PendingImage;
            editor.FindControl<Button>("ApplyEditorButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Button save = view.FindControl<Button>("SaveDiscordWebhookButton")!;
            Assert.True(save.Command!.CanExecute(null));
            save.Command.Execute(null);
            Assert.False(main.DiscordCarrierAnnouncements.HasChanges);
            owner.Close();
            await main.DisposeAsync();

            main = MainWindowViewModelTestBuilder.Create(
                journals,
                builder =>
                    builder
                        .WithAppDataPaths(paths)
                        .WithExternalNetworkClient(http)
                        .WithApplicationLogService(applicationLog)
            );
            await main.RefreshAsync();
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-11-14T12:00:05Z","event":"CarrierJumpRequest","CarrierID":1,"CarrierType":"FleetCarrier","SystemName":"Lave","DepartureTime":"2026-11-14T12:15:00Z"}"""
                    + "\n"
            );
            await main.RefreshAsync();
            await main.DiscordCarrierAnnouncements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Departure to Lave", deliveredTitle);
            Assert.Equal("attachment://carrier.png", deliveredImageUrl);
            using var result = SKBitmap.Decode(Assert.IsType<byte[]>(deliveredImage));
            Assert.Equal(1280, result.Width);
            Assert.Equal(720, result.Height);
            Assert.Equal(SKColors.Cyan, result.GetPixel(5, 5));
            Assert.Equal(SKColors.Cyan, result.GetPixel(1274, 714));
            Assert.Equal("Message sent to Discord.", main.DiscordCarrierAnnouncements.StatusMessage);
            string[] diagnostics = applicationLog
                .Entries.Where(entry => entry.Contains("[Discord carrier]", StringComparison.Ordinal))
                .ToArray();
            Assert.Contains(
                diagnostics,
                entry =>
                    entry.Contains("editor-applied; scope=", StringComparison.Ordinal)
                    && entry.Contains("personal-image=True; personal-crop=True", StringComparison.Ordinal)
            );
            Assert.Contains(
                diagnostics,
                entry =>
                    entry.Contains("saved; scope=", StringComparison.Ordinal)
                    && entry.Contains("revision=1; enabled=True", StringComparison.Ordinal)
            );
            string queued = Assert.Single(diagnostics, entry => entry.Contains(" queued; ", StringComparison.Ordinal));
            string sendId = queued.Split("send=", 2)[1].Split(' ')[0];
            Assert.True(Guid.TryParseExact(sendId, "N", out _));
            Assert.Contains(
                diagnostics,
                entry =>
                    entry.Contains($"send={sendId} rendered;", StringComparison.Ordinal)
                    && entry.Contains("custom-template=True", StringComparison.Ordinal)
            );
            Assert.Contains(
                diagnostics,
                entry =>
                    entry.Contains($"send={sendId} confirmed;", StringComparison.Ordinal)
                    && entry.Contains("title-match=True", StringComparison.Ordinal)
                    && entry.Contains("image-expected=True; image-returned=True", StringComparison.Ordinal)
            );
            Assert.DoesNotContain(
                diagnostics,
                entry =>
                    entry.Contains(path, StringComparison.Ordinal)
                    || entry.Contains("test-token", StringComparison.Ordinal)
                    || entry.Contains("private-signature", StringComparison.Ordinal)
                    || entry.Contains("Departure to", StringComparison.Ordinal)
            );
        }
        finally
        {
            owner.Close();
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
