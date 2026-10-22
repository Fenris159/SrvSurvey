using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationNexusEditingPresentationTests
{
    [AvaloniaFact]
    public async Task HubPrivacyButtonsSaveSharedStatusAndChangeTheVisiblePadlock()
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        source = source with
        {
            FleetCarriers =
            [
                source.FleetCarriers[0] with
                {
                    Cargo = new() { ["steel"] = 48 },
                },
                new() { MarketId = 43, Name = "Unassigned carrier" },
            ],
            Systems =
            [
                source.Systems[0] with
                {
                    Total = 236304,
                    Progress = 1196,
                    Needs = new() { ["steel"] = 235108 },
                },
                source.Systems[1],
                source.Systems[2],
            ],
        };
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = source };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 1050,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            ColonizationCargoProgressBar total = view.GetVisualDescendants()
                .OfType<ColonizationCargoProgressBar>()
                .Single(bar => bar.Name == "NexusTotalHaul");
            Assert.Equal(48, total.Cargo!.Ready);
            Assert.InRange(total.Children[1].Bounds.Width, 3, 4);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Assigned to:");
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "None" && text.IsVisible
            );
            Border hub = view.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "NexusHubDetails");
            hub.BringIntoView();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-nexus-ready-48.png"),
                PngBitmapEncoderOptions.Default
            );
            Button[] buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nexus-privacy"))
                .ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.All(buttons, button => Assert.True(button.IsEnabled));
            Assert.All(buttons, button => Assert.Same(model.TogglePrivacyCommand, button.Command));
            Assert.All(
                buttons,
                button => Assert.True(button.GetVisualDescendants().OfType<PathIcon>().First().IsVisible)
            );
            view.FindControl<ScrollViewer>("NexusPageScroll")!.ScrollToHome();
            Assert.NotNull(window.CaptureRenderedFrame());
            Point click = buttons[0]
                .TranslatePoint(new Point(buttons[0].Bounds.Width / 2, buttons[0].Bounds.Height / 2), window)!
                .Value;
            window.MouseDown(click, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
            await model.ActiveTask;
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.False(model.IsPublic);
            Assert.Equal(1, api.PrivacyChanges);
            buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nexus-privacy"))
                .ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.All(
                buttons,
                button => Assert.False(button.GetVisualDescendants().OfType<PathIcon>().First().IsVisible)
            );
            Assert.All(
                buttons,
                button => Assert.True(button.GetVisualDescendants().OfType<PathIcon>().Last().IsVisible)
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EditWindowShowsWarningAndKeepDoesNotDeleteWhileConfirmDoes()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer();
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => true,
            _ => Task.CompletedTask,
            [],
            ColonizationNexusReportViewModelTests.Sample()
        );
        var window = new ColonizationNexusEditorWindow(model) { Width = 620, Height = 650 };
        try
        {
            window.Show();
            Assert.Equal("Edit Nexus", window.Title);
            Button delete = window.FindControl<Button>("DeleteNexus")!;
            Assert.True(delete.IsEnabled);
            Assert.Equal("Save changes", window.FindControl<Button>("SubmitNexus")!.Content);
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.FindControl<Border>("DeleteNexusPrompt")!.IsVisible);
            Assert.False(window.FindControl<Button>("SubmitNexus")!.IsEnabled);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-nexus-delete-warning.png"),
                PngBitmapEncoderOptions.Default
            );
            window.FindControl<Button>("CancelNexusDeletion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(model.IsDeleteConfirmationVisible);
            Assert.Equal(0, api.Deletes);
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<Button>("ConfirmNexusDeletion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, api.Deletes);
            Assert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
