using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Views;

public sealed partial class BookmarksView : UserControl
{
    public BookmarksView() => InitializeComponent();
    private void OpenScreenshot_Click(object? sender, RoutedEventArgs e) { if (DataContext is BookmarksViewModel vm && (sender as Control)?.Tag is string path) vm.Status = MiningAttachmentActions.Open(path); }
    private void RemoveScreenshot_Click(object? sender, RoutedEventArgs e) { if (DataContext is BookmarksViewModel vm && (sender as Control)?.Tag is string path) vm.RemoveScreenshot(path); }
    private void Undo_Click(object? sender, RoutedEventArgs e) => (DataContext as BookmarksViewModel)?.UndoDelete();
    private async void Attach_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BookmarksViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        if (vm.Selected is null) { vm.Status = "Save or select a bookmark before attaching screenshots."; return; }
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach location screenshots", AllowMultiple = true, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
            vm.AttachScreenshots(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { vm.Status = ex.Message; }
    }
    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BookmarksViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import bookmarks", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.Json] });
            if (files.Count == 0) return;
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            vm.Import(await reader.ReadToEndAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { vm.Status = ex.Message; }
    }
    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BookmarksViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var json = vm.Export();
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export bookmarks", SuggestedFileName = "srvsurvey-bookmarks.json", DefaultExtension = "json" });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            vm.Status = "Bookmarks exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { vm.Status = ex.Message; }
    }
}
