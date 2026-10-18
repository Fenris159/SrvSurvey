using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
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
        window.Resources["RavenControlAccentBrush"] = new SolidColorBrush(Color.Parse("#F5F5F5"));
        window.Resources["RavenAccentForegroundBrush"] = new SolidColorBrush(Color.Parse("#0A0A0A"));
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
            Assert.All(
                toggle.GetVisualDescendants().OfType<TextBlock>(),
                label =>
                    Assert.Equal(
                        Color.Parse("#0A0A0A"),
                        Assert.IsType<ISolidColorBrush>(label.Foreground, exactMatch: false).Color
                    )
            );
            // Avalonia supplies a default TransformGroup for transform keyframes.
            TransformGroup transforms = Assert.IsType<TransformGroup>(spinner.RenderTransform);
            Assert.Single(transforms.Children.OfType<RotateTransform>());
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
