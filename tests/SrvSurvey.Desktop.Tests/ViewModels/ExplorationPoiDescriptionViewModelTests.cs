using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ExplorationPoiDescriptionViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-poi-images-" + Guid.NewGuid().ToString("N")
    );

    /// <summary>Provides poi for the isolated test fixture.</summary>
    private static ExplorationPoi Poi(string description) =>
        new(1, "GEC", "Attraction", "Sol", 1, null, "Scenery", "", "Synopsis", description, 7, 4, 12, false);

    /// <summary>Verifies photos load inline and cache by url without repeating downloads for the same selection.</summary>
    [AvaloniaFact]
    public async Task PhotosLoadInlineAndCacheByUrlWithoutRepeatingDownloadsForTheSameSelection()
    {
        byte[] png = CreatePng();
        var handler = new Handler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) })
        );
        using var http = new HttpClient(handler);
        using var model = new ExplorationPoiDescriptionViewModel(new CodexImageCache(directory, http));
        ExplorationPoi poi = Poi(
            "Before. ![First](https://example.test/one.png) Between. ![Second](https://example.test/two.png) After."
        );
        model.Update(poi, true);
        await model.PendingLoad;
        Assert.Equal(5, model.Blocks.Count);
        Assert.Equal("Before.", model.Blocks[0].Text);
        Assert.True(model.Blocks[0].HasText);
        Assert.False(model.Blocks[0].HasCaption);
        Assert.True(model.Blocks[1].HasImage);
        Assert.True(model.Blocks[1].HasCaption);
        Assert.Equal("First", model.Blocks[1].Caption);
        Assert.False(model.Blocks[1].HasStatus);
        Assert.True(model.Blocks[3].HasImage);
        Assert.Equal(2, handler.Requests.Count);
        Task first = model.PendingLoad;
        model.Update(poi, true);
        Assert.Same(first, model.PendingLoad);
        IReadOnlyList<ExplorationPoiDescriptionBlockViewModel> old = model.Blocks;
        model.Update(null, true);
        Assert.Empty(model.Blocks);
        Assert.False(old[1].HasImage);
        model.Update(poi, true);
        await model.PendingLoad;
        Assert.Equal(2, handler.Requests.Count);
        model.Update(poi with { Description = "![Changed](https://example.test/changed.png)" }, true);
        await model.PendingLoad;
        Assert.True(Assert.Single(model.Blocks).HasImage);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>Verifies missing and undecodable photos show fallback text without urls or markup.</summary>
    [AvaloniaFact]
    public async Task MissingAndUndecodablePhotosShowFallbackTextWithoutUrlsOrMarkup()
    {
        var handler = new Handler(request =>
            Task.FromResult(
                new HttpResponseMessage(
                    request.RequestUri!.AbsolutePath.Contains("missing") ? HttpStatusCode.NotFound : HttpStatusCode.OK
                )
                {
                    Content = new ByteArrayContent([1, 2, 3, 4]),
                }
            )
        );
        using var http = new HttpClient(handler);
        using var model = new ExplorationPoiDescriptionViewModel(new CodexImageCache(directory, http));
        model.Update(
            Poi("Before. ![](https://example.test/missing.png) Between. ![](https://example.test/invalid.png) After."),
            true
        );
        await model.PendingLoad;
        Assert.All(model.Blocks, block => Assert.False(block.HasImage));
        Assert.Contains("unavailable", model.Blocks[1].Status);
        Assert.Contains("could not be displayed", model.Blocks[3].Status);
        Assert.All(
            model.Blocks,
            block =>
            {
                Assert.DoesNotContain("example.test", block.Text + block.Status);
                Assert.DoesNotContain("![", block.Text + block.Status);
            }
        );
    }

    /// <summary>Verifies selection changes and external opt out reject late images and keep readable text.</summary>
    [AvaloniaFact]
    public async Task SelectionChangesAndExternalOptOutRejectLateImagesAndKeepReadableText()
    {
        byte[] png = CreatePng();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(_ =>
        {
            started.SetResult();
            return pending.Task;
        });
        using var http = new HttpClient(handler);
        using var model = new ExplorationPoiDescriptionViewModel(new CodexImageCache(directory, http));
        ExplorationPoi poi = Poi("Before. ![](https://example.test/late.png) After.");
        model.Update(poi, false);
        Assert.Empty(handler.Requests);
        Assert.True(model.Blocks[1].HasStatus);
        model.Update(poi, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task oldLoad = model.PendingLoad;
        IReadOnlyList<ExplorationPoiDescriptionBlockViewModel> oldBlocks = model.Blocks;
        model.Update(poi, false);
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        await oldLoad;
        Assert.False(oldBlocks[1].HasImage);
        Assert.False(model.Blocks[1].HasImage);
        Assert.Contains("unavailable", model.Blocks[1].Status);
        Assert.Equal("After.", model.Blocks[2].Text);
        model.Update(Poi("Replacement text."), false);
        Assert.Equal("Replacement text.", Assert.Single(model.Blocks).Text);
    }

    /// <summary>Verifies unwritable cache and unavailable cache keep the description usable.</summary>
    [AvaloniaFact]
    public async Task UnwritableCacheAndUnavailableCacheKeepTheDescriptionUsable()
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "not-a-directory");
        await File.WriteAllTextAsync(file, "occupied");
        using var http = new HttpClient(
            new Handler(_ => throw new InvalidOperationException("Must not request an image."))
        );
        using var model = new ExplorationPoiDescriptionViewModel(new CodexImageCache(file, http));
        ExplorationPoi poi = Poi("![Photo](https://example.test/photo.png) Body.");
        model.Update(poi, true);
        await model.PendingLoad;
        Assert.Contains("unavailable", model.Blocks[0].Status);
        Assert.Equal("Body.", model.Blocks[1].Text);
        using var unavailable = new ExplorationPoiDescriptionViewModel();
        unavailable.Update(poi, true);
        Assert.True(unavailable.Blocks[0].HasStatus);
    }

    /// <summary>Provides create png for the isolated test fixture.</summary>
    private static byte[] CreatePng()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(48, 24), new Vector(96, 96));
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>Verifies workspace displays loaded photos and readable text instead of image syntax.</summary>
    [AvaloniaFact]
    public async Task WorkspaceDisplaysLoadedPhotosAndReadableTextInsteadOfImageSyntax()
    {
        byte[] png = CreatePng();
        using var http = new HttpClient(
            new Handler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) })
            )
        );
        ExplorationPoi poi = Poi("Before. ![Moon](https://example.test/moon.png) After.");
        using var model = new ExplorationPoiViewModel(
            new PoiClient(poi),
            new ExplorationPoiSettingsStore(Path.Combine(directory, "ui.json")),
            imageCache: new CodexImageCache(directory, http)
        );
        model.UpdateContext("Sol", 1, new(0, 0, 0), null, []);
        model.SetExternalEnabled(true);
        await model.PendingLoad;
        await model.DescriptionPreview.PendingLoad;
        var view = new ExplorationPoiView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 1100,
        };
        try
        {
            window.Show();
            Image photo = Assert.Single(
                view.GetVisualDescendants().OfType<Image>(),
                image => image.IsEffectivelyVisible && image.Source is not null
            );
            Assert.True(photo.Bounds.Width > 0);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Moon");
            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text =>
                    text.Text?.Contains("![", StringComparison.Ordinal) == true
                    || text.Text?.Contains("example.test", StringComparison.Ordinal) == true
            );
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class PoiClient(ExplorationPoi poi) : IExplorationPoiClient
    {
        /// <summary>Provides get async for the isolated test fixture.</summary>
        public Task<ExplorationPoiLoadResult> GetAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ExplorationPoiLoadResult(new ExplorationPoiCatalog([poi]), DateTimeOffset.UtcNow, true)
            );
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        /// <summary>Records the request and returns the controlled response for this test.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!);
            return send(request);
        }
    }

    /// <summary>Releases the isolated test resources after assertions complete.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
