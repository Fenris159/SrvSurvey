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

    protected override void OnClosed(EventArgs e)
    {
        imageLoadCancellation?.Cancel();
        imageLoadCancellation?.Dispose();
        imageLoadCancellation = null;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.SetUriLauncher(null);
        ReplaceImage(null);
        base.OnClosed(e);
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
        var loadCancellation = await BeginImageLoadAsync();
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
        var result = await TryLoadImageResultAsync(
            organism,
            forceRefresh,
            loadCancellation);
        if (result is null
            || cancellationToken.IsCancellationRequested
            || viewModel.SelectedOrganism?.EntryId != organism.EntryId)
        {
            return;
        }

        ApplyImageResult(organism, result);
    }

    private async Task<CancellationTokenSource> BeginImageLoadAsync()
    {
        var previousCancellation = imageLoadCancellation;
        if (previousCancellation is not null)
        {
            await previousCancellation.CancelAsync();
            previousCancellation.Dispose();
        }

        var loadCancellation = new CancellationTokenSource();
        imageLoadCancellation = loadCancellation;
        return loadCancellation;
    }

    private async Task<CodexImageCacheResult?> TryLoadImageResultAsync(
        BiologyCodexOrganismViewModel organism,
        bool forceRefresh,
        CancellationTokenSource loadCancellation)
    {
        var cancellationToken = loadCancellation.Token;
        var imageLoadTask = imageCache.GetAsync(
            organism.EntryId,
            organism.ImageUrl!,
            organism.LocalImageName,
            forceRefresh,
            cancellationToken);
        try
        {
            return await imageLoadTask.WaitAsync(
                ImageLoadTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (TimeoutException)
        {
            await loadCancellation.CancelAsync();
            ObserveFault(imageLoadTask);
            SetImageUnavailableIfCurrent(
                organism.EntryId,
                "The reference image download timed out.");
            return null;
        }
        catch (Exception exception)
        {
            SetImageUnavailableIfCurrent(organism.EntryId, exception.Message);
            return null;
        }
    }

    private void SetImageUnavailableIfCurrent(long entryId, string message)
    {
        if (viewModel.SelectedOrganism?.EntryId == entryId)
        {
            ImageStatusText.Text = "Image unavailable: " + message;
        }
    }

    private void ApplyImageResult(
        BiologyCodexOrganismViewModel organism,
        CodexImageCacheResult result)
    {
        if (!result.IsSuccess)
        {
            ImageStatusText.Text = "Image unavailable: " + result.Error;
            return;
        }

        try
        {
            ReplaceImage(new Bitmap(result.Path));
            ImageStatusText.Text = FormatImageStatus(organism, result);
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

    private static string FormatImageStatus(
        BiologyCodexOrganismViewModel organism,
        CodexImageCacheResult result)
    {
        if (result.IsLocal)
        {
            return "Local flora reference image";
        }

        return organism.ImageCreditText
            + (result.IsFromCache ? " · cached" : " · downloaded");
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
