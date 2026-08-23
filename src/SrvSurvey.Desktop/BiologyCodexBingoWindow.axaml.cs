using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop;

public sealed partial class BiologyCodexBingoWindow : Window
{
    private readonly BiologyCodexBingoViewModel viewModel;

    public BiologyCodexBingoWindow()
        : this(CreateDesignViewModel())
    {
    }

    public BiologyCodexBingoWindow(BiologyCodexBingoViewModel viewModel)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        if (DesktopExternalEffectPolicy.IsAllowed)
        {
            viewModel.SetPlatformServices(WriteClipboardAsync, LaunchUriAsync);
        }
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        await viewModel.EnsureInitializedAsync();
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
            ?? throw new InvalidOperationException(
                "The desktop clipboard is not available.");
        await clipboard.SetTextAsync(text);
        await clipboard.FlushAsync();
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return Launcher.LaunchUriAsync(uri);
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        viewModel.SetPlatformServices(null, null);
    }

    private static BiologyCodexBingoViewModel CreateDesignViewModel()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-CodexBingo-Design");
        var store = new CommanderCodexStore(temporaryDirectory);
        var catalog = ExobiologyReferenceCatalog.LoadEmbedded();
        return new BiologyCodexBingoViewModel(
            store,
            catalog,
            new CanonnCodexChallengeImporter(
                new CanonnCodexChallengeClient(),
                store,
                catalog),
            new CommanderCodexJournalImporter(temporaryDirectory, store),
            new CodexDiscoveryLocationClient());
    }
}
