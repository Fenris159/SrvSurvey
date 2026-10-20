using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1180, "blue-dark")]
    [InlineData(800, "monochrome-dark")]
    public async Task SystemPickerShowsSixRowsFiltersByTypingAndUsesCurrentLocation(int width, string themeKey)
    {
        string[] names = ["Other A", "Other B", .. Enumerable.Range(1, 8).Select(index => $"Synuefai {index}")];
        int[] categories = [0];
        var handler = new RavenHandler
        {
            SystemMapJson = JsonSerializer.Serialize(
                new { systems = names.Select(name => new { name, cat = categories }) }
            ),
        };
        using var http = new HttpClient(handler);
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        string themeDirectory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-system-picker-theme-" + Guid.NewGuid().ToString("N")
        );
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(themeDirectory, "theme.json"))
        );
        string previousTheme = theme.Current.Key;
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        main.Colonization.UpdateSystemContext("Current Outside Catalog", null, 999);
        main.Colonization.SelectedWorkspaceTabIndex = 2;
        await main.Colonization.SystemEditor.SystemCatalogRefreshTask;
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 900,
        };
        try
        {
            theme.Select(themeKey);
            theme.ApplyCurrent();
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            AutoCompleteBox input = view.FindControl<AutoCompleteBox>("EditorSystemName")!;
            Button dropdown = view.FindControl<Button>("EditorSystemDropdown")!;
            dropdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-system-catalog-open.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            Assert.True(input.IsDropDownOpen);
            Popup popup = input.GetVisualDescendants().OfType<Popup>().Single();
            ListBox list = popup.Child!.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.Equal(10, list.ItemCount);
            Assert.InRange(list.Bounds.Height, 192, 193);
            ListBoxItem[] rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.All(rows, row => Assert.Equal(32, row.Bounds.Height));
            Assert.Equal(
                6,
                rows.Count(row =>
                    row.TranslatePoint(default, list)!.Value.Y >= 0
                    && row.TranslatePoint(default, list)!.Value.Y + row.Bounds.Height <= list.Bounds.Height + 0.1
                )
            );
            ScrollViewer scroller = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            TextBox textBox = input.GetVisualDescendants().OfType<TextBox>().Single();
            textBox.Text = "syn";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-system-catalog-filtered.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            Assert.True(input.IsDropDownOpen);
            Assert.Equal(8, list.ItemCount);
            Assert.All(list.Items.Cast<string>(), name => Assert.StartsWith("Synuefai", name));
            input.Text = "Synuefai 2";
            input.IsDropDownOpen = false;
            Assert.Equal("Synuefai 2", main.Colonization.SystemEditor.SystemNameInput);
            LeftClick(window, view.FindControl<Button>("UseCurrentEditorSystem")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal("Current Outside Catalog", input.Text);
            Button refresh = view.FindControl<Button>("RefreshEditorSystems")!;
            Assert.Equal("Open on Raven", Assert.IsType<Button>(((WrapPanel)refresh.Parent!).Children[1]).Content);
            LeftClick(window, refresh);
            await main.Colonization.SystemEditor.SystemCatalogRefreshTask;
            Assert.Equal(2, handler.SystemCatalogReads);
            main.Colonization.SelectedWorkspaceTabIndex = 0;
            main.Colonization.SelectedWorkspaceTabIndex = 2;
            await main.Colonization.SystemEditor.SystemCatalogRefreshTask;
            Assert.Equal(2, handler.SystemCatalogReads);
        }
        finally
        {
            window.Close();
            theme.Select(previousTheme);
            theme.ApplyCurrent();
            if (Directory.Exists(themeDirectory))
            {
                Directory.Delete(themeDirectory, true);
            }
        }
    }
}
