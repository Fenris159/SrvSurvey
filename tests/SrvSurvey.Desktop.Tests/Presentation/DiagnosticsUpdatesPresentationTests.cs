using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiagnosticsUpdatesPresentationTests
{
    /// <summary>
    /// Exercises real copy and installed-notes controls without an available update at narrow and wide widths.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(760)]
    [InlineData(1120)]
    public async Task InstalledDetailsCopyAndReleaseNotesRemainAvailableWithoutAnUpdate(int width)
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null);
        model.SelectedDiagnosticsTab = DiagnosticsWorkspaceTab.Updates;
        var view = new DiagnosticsView { DataContext = model };
        var window = new Window
        {
            Width = width,
            Height = 900,
            Content = view,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.False(model.ReleaseUpdates.IsUpdateAvailable);
            Button copy = view.FindControl<Button>("CopySupportDetailsButton")!;
            Button notes = view.FindControl<Button>("InstalledReleaseNotesButton")!;
            Assert.True(copy.IsEffectivelyVisible);
            Assert.True(copy.IsEnabled);
            Assert.True(notes.IsEffectivelyVisible);

            Click(window, copy);
            Assert.Equal(model.ReleaseUpdates.SupportDetails, await window.Clipboard!.TryGetTextAsync());

            Click(window, notes);
            ReleaseNotesDialog dialog = Assert.Single(window.OwnedWindows.OfType<ReleaseNotesDialog>());
            using WriteableBitmap? notesFrame = dialog.CaptureRenderedFrame();
            Assert.NotNull(notesFrame);
            ReleaseNotesDialogViewModel content = Assert.IsType<ReleaseNotesDialogViewModel>(dialog.DataContext);
            Assert.NotEmpty(content.Changes);
            Assert.Contains("SrvSurvey-XP", content.Title);
            dialog.Close();

            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == model.ReleaseUpdates.CurrentVersion && block.IsEffectivelyVisible
            );
            string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_DIAGNOSTICS_RENDER_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                frame.Save(
                    Path.Combine(directory, $"diagnostics-updates-{width}.png"),
                    PngBitmapEncoderOptions.Default
                );
                notesFrame.Save(
                    Path.Combine(directory, "installed-release-notes.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
        }
        finally
        {
            window.Close();
        }

        Assert.False(model.ReleaseUpdates.CopySupportDetailsCommand.CanExecute(null));
    }

    /// <summary>
    /// Delivers actual headless pointer input so command execution and routed handlers run as they do for a user.
    /// </summary>
    private static void Click(Window window, Control control)
    {
        Point point = control
            .TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
