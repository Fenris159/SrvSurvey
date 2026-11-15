using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class RavenBodyRecoveryPresentationTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovalWarningShowsLoadedSystemAndAllBodiesAndRequiresExplicitYes(bool accept)
    {
        var owner = new Window { Width = 900, Height = 750 };
        owner.Show();
        var dialog = new RavenBodyRemovalDialog(
            new(
                "Synuefai CX-V c18-6",
                1732784263842,
                ["Synuefai CX-V c18-6 1 (BodyID 1)", "Synuefai CX-V c18-6 2 (BodyID 2)"]
            )
        );
        try
        {
            Task<bool> response = dialog.ShowDialog<bool>(owner);
            using WriteableBitmap? frame = dialog.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "raven-body-removal-warning.png"),
                PngBitmapEncoderOptions.Default
            );
            Assert.Equal("Synuefai CX-V c18-6 (1732784263842)", dialog.FindControl<TextBlock>("SystemText")!.Text);
            Assert.Equal(2, dialog.FindControl<ItemsControl>("RemovedBodies")!.ItemCount);
            Button choice = dialog
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, accept ? "Yes" : "No"));
            choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(accept, await response);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task RediscoverButtonAppearsAfterGuideAndRequiresALoadedSystem()
    {
        using var http = new HttpClient(new LoadedSystemHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.SelectedWorkspaceTabIndex = 2;
        var view = new ColonizationView { DataContext = main };
        var owner = new Window
        {
            Content = view,
            Width = 1320,
            Height = 900,
        };
        try
        {
            owner.Show();
            using WriteableBitmap? frame = owner.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "raven-body-editor-button.png"),
                PngBitmapEncoderOptions.Default
            );
            Button rediscover = view.FindControl<Button>("RediscoverAbsentBodies")!;
            Button guide = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Guide"));
            Assert.Equal("Rediscover Absent Bodies", rediscover.Content);
            Assert.Contains("danger", rediscover.Classes);
            Assert.False(rediscover.IsEffectivelyEnabled);
            Assert.True(
                rediscover.TranslatePoint(default, owner)!.Value.X > guide.TranslatePoint(default, owner)!.Value.X
            );
            main.Colonization.SystemEditor.UpdateContext(new(true, "Test Cmdr", "Example", 42, "test-key"));
            await main.Colonization.SystemEditor.LoadAsync();
            Assert.True(rediscover.IsEffectivelyEnabled);
            using WriteableBitmap? loadedFrame = owner.CaptureRenderedFrame();
            Assert.NotNull(loadedFrame);
            loadedFrame.Save(
                Path.Combine(Path.GetTempPath(), "raven-body-editor-button-loaded.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            owner.Close();
        }
    }

    private sealed class LoadedSystemHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"id64":42,"name":"Example","architect":"Test Cmdr","bodies":[{"num":0,"name":"Example","type":"st"}],"sites":[]}""",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }
}
