using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ExplorationPoiDescriptionViewModel(CodexImageCache? imageCache = null)
    : WorkspaceObservable,
        IDisposable
{
    private ExplorationPoi? selected;
    private bool externalEnabled;
    private CancellationTokenSource? request;
    private IReadOnlyList<ExplorationPoiDescriptionBlockViewModel> blocks = [];

    public IReadOnlyList<ExplorationPoiDescriptionBlockViewModel> Blocks => blocks;
    public Task PendingLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Replaces the selected description and cancels image work belonging to the previous selection.</summary>
    public void Update(ExplorationPoi? poi, bool allowExternalData)
    {
        if (ReferenceEquals(selected, poi) && externalEnabled == allowExternalData)
        {
            return;
        }
        selected = poi;
        externalEnabled = allowExternalData;
        request?.Cancel();
        request = null;
        ClearBlocks();
        if (poi is null)
        {
            return;
        }
        IReadOnlyList<ExplorationPoiDescriptionBlock> content =
            poi.DescriptionBlocks.Count > 0
                ? poi.DescriptionBlocks
                : ExplorationPoiDescription.Parse(poi.Description).Blocks;
        blocks = content.Select(block => new ExplorationPoiDescriptionBlockViewModel(block)).ToArray();
        Changed(nameof(Blocks));
        if (!externalEnabled || imageCache is null)
        {
            foreach (ExplorationPoiDescriptionBlockViewModel block in blocks.Where(block => block.ImageUri is not null))
            {
                block.Status = "Image preview unavailable. Open the GEC entry to view photos.";
            }
            return;
        }
        var cancellation = new CancellationTokenSource();
        request = cancellation;
        PendingLoad = LoadImagesAsync(blocks, cancellation);
    }

    /// <summary>Loads bounded cached photos and installs them only while their selection generation remains current.</summary>
    private async Task LoadImagesAsync(
        IReadOnlyList<ExplorationPoiDescriptionBlockViewModel> content,
        CancellationTokenSource cancellation
    )
    {
        try
        {
            foreach (
                ExplorationPoiDescriptionBlockViewModel block in content.Where(block => block.ImageUri is not null)
            )
            {
                cancellation.Token.ThrowIfCancellationRequested();
                block.Status = "Loading image…";
                string url = block.ImageUri!.AbsoluteUri;
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
                long key = Math.Max(1, BinaryPrimitives.ReadInt64LittleEndian(hash) & long.MaxValue);
                CodexImageCacheResult result = await imageCache!.GetAsync(
                    key,
                    url,
                    cancellationToken: cancellation.Token
                );
                cancellation.Token.ThrowIfCancellationRequested();
                if (!result.IsSuccess)
                {
                    block.Status = "Image unavailable. Open the GEC entry to view the original.";
                    continue;
                }
                try
                {
                    block.Image = DecodeImage(result.Path);
                    block.Status = block.HasImage
                        ? string.Empty
                        : "Image could not be displayed. Open the GEC entry to view the original.";
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    block.Status = "Image could not be displayed. Open the GEC entry to view the original.";
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { /* Selection changes and external-data opt-out cancel the previous preview. */
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            foreach (
                ExplorationPoiDescriptionBlockViewModel block in content.Where(block =>
                    block.ImageUri is not null && !block.HasImage
                )
            )
            {
                block.Status = "Image unavailable. Open the GEC entry to view the original.";
            }
        }
        finally
        {
            if (ReferenceEquals(request, cancellation))
            {
                request = null;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>Validates and reduces a source image before creating its desktop preview bitmap.</summary>
    private static Bitmap? DecodeImage(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }
        using FileStream stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, Math.Min(codec.Info.Width, 1400));
    }

    /// <summary>Releases image resources owned by the previous description blocks.</summary>
    private void ClearBlocks()
    {
        foreach (ExplorationPoiDescriptionBlockViewModel block in blocks)
        {
            block.Dispose();
        }
        blocks = [];
        Changed(nameof(Blocks));
    }

    /// <summary>Cancels outstanding image work and releases the bitmaps owned by this description.</summary>
    public void Dispose()
    {
        request?.Cancel();
        request = null;
        ClearBlocks();
        imageCache?.Dispose();
    }
}

public sealed class ExplorationPoiDescriptionBlockViewModel(ExplorationPoiDescriptionBlock block)
    : WorkspaceObservable,
        IDisposable
{
    private Bitmap? image;
    private string status = string.Empty;

    public string Text => block.Text;
    public bool HasText => Text.Length > 0;
    public Uri? ImageUri => block.ImageUri;
    public string Caption => block.Caption;
    public bool HasCaption => Caption.Length > 0;
    public bool HasImage => Image is not null;
    public bool HasStatus => Status.Length > 0;

    public Bitmap? Image
    {
        get => image;
        internal set
        {
            Bitmap? oldImage = image;
            if (Set(ref image, value))
            {
                Changed(nameof(HasImage));
                oldImage?.Dispose();
            }
        }
    }

    public string Status
    {
        get => status;
        internal set
        {
            if (Set(ref status, value))
            {
                Changed(nameof(HasStatus));
            }
        }
    }

    /// <summary>Releases the preview bitmap owned by this description block.</summary>
    public void Dispose() => Image = null;
}
