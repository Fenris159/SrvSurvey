using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

/// <summary>A dedicated, nonmodal native window for public Raven build data and local CSV export.</summary>
public sealed partial class ColonizationProjectPreviewWindow : Window
{
    private readonly ColonizationProjectPreviewViewModel viewModel;

    /// <summary>Provides a designer-safe window whose placeholder view model never accesses Raven.</summary>
    public ColonizationProjectPreviewWindow()
        : this(new ColonizationProjectPreviewViewModel(new RavenColonialClient(), "preview", () => false)) { }

    /// <summary>Attaches the preview lifetime to opening and closing this popout.</summary>
    public ColonizationProjectPreviewWindow(ColonizationProjectPreviewViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    /// <summary>Starts live reads only after the preview becomes visible.</summary>
    private async void OnOpened(object? sender, EventArgs eventArgs) => await viewModel.RunAsync();

    /// <summary>Stops refreshes and in-flight reads when this preview closes.</summary>
    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        Closed -= OnClosed;
        viewModel.Dispose();
    }

    /// <summary>Allows an immediate refresh after a network error without overlapping another request.</summary>
    private async void Refresh_Click(object? sender, RoutedEventArgs eventArgs) => await viewModel.RefreshAsync();

    /// <summary>Captures the current snapshot before presenting the platform's save dialog.</summary>
    private async void ExportCsv_Click(object? sender, RoutedEventArgs eventArgs) =>
        await viewModel.ExportAsync(SaveCsvAsync);

    /// <summary>Writes an Excel-compatible UTF-8 CSV with a BOM, truncating an existing selected file.</summary>
    internal async Task<bool> SaveCsvAsync(string fileName, string csv)
    {
        DesktopExternalEffectPolicy.ThrowIfDisabled();
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Raven build CSV",
                SuggestedFileName = fileName,
                DefaultExtension = "csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV table") { Patterns = ["*.csv"], MimeTypes = ["text/csv"] },
                ],
            }
        );
        if (file is null)
        {
            return false;
        }
        await using Stream stream = await file.OpenWriteAsync();
        await ColonizationProjectCsvExporter.WriteUtf8Async(stream, csv);
        return true;
    }
}
