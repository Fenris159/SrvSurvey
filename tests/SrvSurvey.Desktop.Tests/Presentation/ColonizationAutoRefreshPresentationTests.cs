using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public async Task AutoRefreshButtonShowsActivityAndExplainsSessionLimits()
    {
        using var http = new HttpClient(new RavenHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 800,
        };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ToggleButton toggle = view.FindControl<ToggleButton>("AutoRefreshProjects")!;
            TextBlock spinner = view.FindControl<TextBlock>("AutoRefreshSpinner")!;
            Assert.Same(view.FindControl<Button>("RefreshProjects")!.Parent, toggle.Parent);
            Assert.True(toggle.IsEnabled);
            Assert.False(toggle.IsChecked);
            Assert.False(spinner.IsVisible);
            string help = Assert.IsType<string>(ToolTip.GetTip(toggle));
            Assert.Contains("60 seconds", help);
            Assert.Contains("Session only", help);
            Assert.Contains("one hour", help);
            toggle.IsChecked = true;
            Assert.True(main.Colonization.IsAutoRefreshEnabled);
            Assert.True(spinner.IsVisible);
            using WriteableBitmap? active = window.CaptureRenderedFrame();
            Assert.NotNull(active);
            active.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-auto-refresh-preview.png"),
                PngBitmapEncoderOptions.Default
            );
            toggle.IsChecked = false;
            await main.Colonization.AutoRefreshTask;
            Assert.False(spinner.IsVisible);
            main.Colonization.IsEnabled = false;
            Assert.False(toggle.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
