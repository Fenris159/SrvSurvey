using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class RouteWindow : Window
{
    private readonly RouteWorkspaceViewModel viewModel;
    private SpanshSearchWindow? searchWindow;

    public RouteWindow()
        : this(CreateDesignViewModel()) { }

    public RouteWindow(RouteWorkspaceViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PrepareCarrierWorkspace();
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    /// <summary>
    /// Opens or focuses the workspace's owned Spansh search window.
    /// </summary>
    private void SpanshSearch_Click(object? sender, RoutedEventArgs eventArgs)
    {
        viewModel.PrepareCarrierSearch();
        if (searchWindow is not null)
        {
            searchWindow.Activate();
        }
        else if (viewModel.CanSearchSpansh && viewModel.SpanshSearch is not null)
        {
            searchWindow = new SpanshSearchWindow(viewModel.SpanshSearch);
            searchWindow.Closed += OnSearchClosed;
            searchWindow.Show(this);
        }
    }

    /// <summary>
    /// Releases the closed search-window reference so it can be opened again.
    /// </summary>
    private void OnSearchClosed(object? sender, EventArgs e)
    {
        if (sender is SpanshSearchWindow window)
        {
            window.Closed -= OnSearchClosed;
            searchWindow = null;
        }
    }

    /// <summary>
    /// Closes the owned search window and releases route-window event handlers.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        searchWindow?.Close();
        viewModel.DismissDialogs();
        base.OnClosed(e);
    }

    private void HopCheckBox_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is CheckBox { DataContext: RouteHopItemViewModel hop })
        {
            viewModel.SetProgressThrough(hop.Index, !hop.IsReached);
        }
    }

    private void RemoveHop_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: RouteHopItemViewModel hop })
        {
            viewModel.RemoveHop(hop);
        }
    }

    private async void BioTargetCheckBox_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is CheckBox { DataContext: RouteBioTargetItemViewModel target })
        {
            await viewModel.SetBioTargetCompletedAsync(target, !target.IsCompleted);
        }
    }

    private async void ImportSpansh_Click(object? sender, RoutedEventArgs eventArgs)
    {
        await viewModel.ImportSpanshUrlAsync(await ReadClipboardAsync());
    }

    private async void ImportNames_Click(object? sender, RoutedEventArgs eventArgs)
    {
        await viewModel.ImportNamesTextAsync(await ReadClipboardAsync(), appendToRoute: true);
    }

    private async void ImportFile_Click(object? sender, RoutedEventArgs eventArgs)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose a system-name text file",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Text files") { Patterns = ["*.txt"], MimeTypes = ["text/plain"] },
                    ],
                }
            );
            if (files.Count == 0)
            {
                return;
            }

            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            await viewModel.ImportNamesTextAsync(await reader.ReadToEndAsync());
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            viewModel.ReportImportError("The system-name file could not be read: " + exception.Message);
        }
    }

    private async Task<string?> ReadClipboardAsync()
    {
        try
        {
            return await (
                TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("The desktop clipboard is not available.")
            ).TryGetTextAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            viewModel.ReportImportError("The clipboard could not be read: " + exception.Message);
            return null;
        }
    }

    private static RouteWorkspaceViewModel CreateDesignViewModel()
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "SrvSurvey-Route-Design");
        return new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(temporaryDirectory)),
            new RouteNameImporter(new EmptySystemResolver()),
            new EmptySpanshRouteClient()
        );
    }

    private sealed class EmptySystemResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptySpanshRouteClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }
}
