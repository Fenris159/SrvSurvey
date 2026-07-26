using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class BiologyCodexWindow : Window
{
    private static readonly TimeSpan ImageLoadTimeout = TimeSpan.FromSeconds(35);

    private readonly BiologyCodexViewModel viewModel;
    private readonly CodexImageCache imageCache;
    private CancellationTokenSource? imageLoadCancellation;
    private Bitmap? loadedImage;

    public BiologyCodexWindow()
        : this(CreateDesignViewModel(), CreateDesignImageCache())
    {
    }

    public BiologyCodexWindow(
        BiologyCodexViewModel viewModel,
        CodexImageCache imageCache)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.imageCache = imageCache
            ?? throw new ArgumentNullException(nameof(imageCache));
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SetUriLauncher(LaunchUriAsync);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _ = LoadSelectedImageAsync(forceRefresh: false);
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        imageLoadCancellation?.Cancel();
        imageLoadCancellation?.Dispose();
        imageLoadCancellation = null;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.SetUriLauncher(null);
        ReplaceImage(null);
        base.OnClosed(eventArgs);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BiologyCodexViewModel.SelectedImageUrl))
        {
            _ = LoadSelectedImageAsync(forceRefresh: false);
        }
    }

    private async Task LoadSelectedImageAsync(bool forceRefresh)
    {
        imageLoadCancellation?.Cancel();
        imageLoadCancellation?.Dispose();
        var loadCancellation = new CancellationTokenSource();
        imageLoadCancellation = loadCancellation;
        var cancellationToken = loadCancellation.Token;
        var organism = viewModel.SelectedOrganism;
        if (organism is null || string.IsNullOrWhiteSpace(organism.ImageUrl))
        {
            ReplaceImage(null);
            ImageStatusText.Text = organism is null
                ? "Select an organism to load its reference image."
                : "No reference image is available for this entry.";
            return;
        }

        ReplaceImage(null);
        ImageStatusText.Text = forceRefresh
            ? "Refreshing reference image…"
            : "Loading reference image…";
        CodexImageCacheResult result;
        var imageLoadTask = imageCache.GetAsync(
            organism.EntryId,
            organism.ImageUrl,
            organism.LocalImageName,
            forceRefresh,
            cancellationToken);
        try
        {
            result = await imageLoadTask.WaitAsync(
                ImageLoadTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (TimeoutException)
        {
            loadCancellation.Cancel();
            ObserveFault(imageLoadTask);
            if (viewModel.SelectedOrganism?.EntryId == organism.EntryId)
            {
                ImageStatusText.Text =
                    "Image unavailable: The reference image download timed out.";
            }

            return;
        }
        catch (Exception exception)
        {
            if (viewModel.SelectedOrganism?.EntryId == organism.EntryId)
            {
                ImageStatusText.Text = "Image unavailable: " + exception.Message;
            }

            return;
        }
        if (cancellationToken.IsCancellationRequested
            || viewModel.SelectedOrganism?.EntryId != organism.EntryId)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ImageStatusText.Text = "Image unavailable: " + result.Error;
            return;
        }

        try
        {
            ReplaceImage(new Bitmap(result.Path));
            ImageStatusText.Text = result.IsLocal
                ? "Local flora reference image"
                : organism.ImageCreditText
                    + (result.IsFromCache ? " · cached" : " · downloaded");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            ReplaceImage(null);
            ImageStatusText.Text = "Image could not be decoded: "
                + exception.Message;
        }
    }

    private void ReplaceImage(Bitmap? image)
    {
        ImageViewport.Source = image;
        loadedImage?.Dispose();
        loadedImage = image;
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
                | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void ResetImage_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ImageViewport.ResetView();
    }

    private async void RefreshImage_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        await LoadSelectedImageAsync(forceRefresh: true);
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return Launcher.LaunchUriAsync(uri);
    }

    private static BiologyCodexViewModel CreateDesignViewModel()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-BiologyCodex-Design");
        return new BiologyCodexViewModel(
            new SystemSurveyViewModel(
                new SystemSurveySettingsStore(
                    Path.Combine(temporaryDirectory, "ui-settings.json"))),
            ExobiologyReferenceCatalog.LoadEmbedded(),
            BiologyCriteriaCatalog.LoadEmbedded());
    }

    private static CodexImageCache CreateDesignImageCache()
    {
        return new CodexImageCache(Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-BiologyCodex-Design",
            "images"));
    }
}
