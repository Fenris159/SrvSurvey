using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class MineMapView : UserControl
{
    public MineMapView()
    {
        InitializeComponent();
        SurfaceSearchResultsScroller.SizeChanged += (_, _) => FitSurfaceSearchResults();
    }

    internal (ScrollViewer Scroller, Control Table) SurfaceResultsWidthTarget =>
        (SurfaceSearchResultsScroller, SurfaceSearchResultsTable);

    private void FitSurfaceSearchResults()
    {
        double width = SurfaceSearchResultsScroller.Bounds.Width;
        if (width > 0)
        {
            SurfaceSearchResultsTable.Width = Math.Max(1280, width);
        }
    }

    private void OnSurveyRowTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (
            sender is Control { DataContext: MineMapSurveyRowViewModel row }
            && DataContext is MainWindowViewModel mainWindow
        )
        {
            mainWindow.MineMap.SelectSurvey(row);
            eventArgs.Handled = true;
        }
    }

    private async void ExportSurveyCsv_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (
            DataContext is not MainWindowViewModel { MineMap: { ActiveSurvey: { } survey } mineMap }
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
        )
        {
            return;
        }

        try
        {
            IStorageFile? file = await storage.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export Surface Mining survey",
                    SuggestedFileName = MineMapCsvExporter.CreateSuggestedFileName(survey),
                    DefaultExtension = "csv",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("CSV table") { Patterns = ["*.csv"], MimeTypes = ["text/csv"] },
                    ],
                }
            );
            if (file is null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteAsync(MineMapCsvExporter.Write(survey));
            mineMap.ReportCsvExported(file.Name);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            mineMap.ReportCsvExportFailed(exception.Message);
        }
    }
}
