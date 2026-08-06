using System.ComponentModel;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform;

public sealed class BiologyCodexWindowCoordinator : IDisposable
{
    private readonly BiologyCodexViewModel viewModel;
    private readonly CodexImageSettingsViewModel imageSettings;
    private readonly Window owner;
    private readonly CodexImageCache imageCache;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The pre-download worker disposes the captured source in its finally block.")]
    private CancellationTokenSource? preDownloadCancellation;
    private BiologyCodexWindow? window;
    private bool disposed;

    public BiologyCodexWindowCoordinator(
        BiologyCodexViewModel viewModel,
        Window owner,
        CodexImageSettingsViewModel imageSettings)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.imageSettings = imageSettings
            ?? throw new ArgumentNullException(nameof(imageSettings));
        imageCache = new CodexImageCache(() => new CodexImageLocations(
            imageSettings.EffectiveCacheDirectory,
            imageSettings.EffectiveLocalFloraDirectory));
        imageSettings.PropertyChanged += OnImageSettingsPropertyChanged;
        viewModel.SetWindowOpener(ShowOrActivateAsync);
        RestartPreDownload();
    }

    public bool IsVisible => window is not null;

    public Task<bool> ShowOrActivateAsync()
    {
        if (disposed || !viewModel.HasSystem)
        {
            return Task.FromResult(false);
        }

        if (window is not null)
        {
            window.Activate();
            return Task.FromResult(true);
        }

        var codexWindow = new BiologyCodexWindow(viewModel, imageCache);
        codexWindow.Closed += OnWindowClosed;
        window = codexWindow;
        codexWindow.Show(owner);
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.SetWindowOpener(null);
        imageSettings.PropertyChanged -= OnImageSettingsPropertyChanged;
        preDownloadCancellation?.Cancel();
        var codexWindow = window;
        window = null;
        if (codexWindow is not null)
        {
            codexWindow.Closed -= OnWindowClosed;
            codexWindow.Close();
        }

        imageCache.Dispose();
    }

    private void OnImageSettingsPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName is nameof(CodexImageSettingsViewModel.PreDownload)
                or nameof(CodexImageSettingsViewModel.CacheDirectory)
                or nameof(CodexImageSettingsViewModel.LocalFloraDirectory))
        {
            RestartPreDownload();
        }
    }

    private void RestartPreDownload()
    {
        preDownloadCancellation?.Cancel();
        if (disposed || !imageSettings.PreDownload)
        {
            imageSettings.SetReadyStatus();
            return;
        }

        var cancellation = new CancellationTokenSource();
        preDownloadCancellation = cancellation;
        _ = RunPreDownloadAsync(cancellation);
    }

    private async Task RunPreDownloadAsync(CancellationTokenSource cancellation)
    {
        try
        {
            imageSettings.SetPreDownloadStatus(
                true,
                "Preparing the Codex biology image cache...");
            var progress = new Progress<CodexImagePreDownloadProgress>(value =>
            {
                if (!cancellation.IsCancellationRequested)
                {
                    imageSettings.SetPreDownloadStatus(
                        true,
                        $"Preparing Codex images: {value.Completed:N0} of {value.Total:N0} checked, "
                            + $"{value.Downloaded:N0} downloaded, {value.Failed:N0} unavailable.");
                }
            });
            var requests = imageSettings.BiologyEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ImageUrl))
                .Select(entry => new CodexImageRequest(
                    entry.EntryId,
                    entry.ImageUrl!,
                    entry.GetLegacyLocalImageName()));
            var result = await imageCache.PreDownloadAsync(
                requests,
                progress,
                cancellation.Token);
            imageSettings.SetPreDownloadStatus(
                false,
                $"Codex image cache ready: {result.Downloaded:N0} downloaded, "
                    + $"{result.Cached:N0} already cached, {result.Local:N0} local, "
                    + $"and {result.Failed:N0} unavailable.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!imageSettings.PreDownload)
            {
                imageSettings.SetReadyStatus();
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            imageSettings.SetPreDownloadStatus(
                false,
                "Codex background downloading stopped: " + exception.Message);
        }
        finally
        {
            if (ReferenceEquals(preDownloadCancellation, cancellation))
            {
                preDownloadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is BiologyCodexWindow codexWindow)
        {
            codexWindow.Closed -= OnWindowClosed;
            if (ReferenceEquals(window, codexWindow))
            {
                window = null;
            }
        }
    }
}
