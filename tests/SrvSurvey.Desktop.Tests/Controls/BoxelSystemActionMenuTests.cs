using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelSystemActionMenuTests
{
    [AvaloniaFact]
    public void LauncherOpensPopupWithCommandsFromTheRow()
    {
        var row = CreateRow();
        var control = new BoxelSystemActionMenu
        {
            DataContext = row,
        };
        var window = new Window { Content = control };
        window.Show();

        control.FindControl<Button>("Launcher")!.RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));

        var popup = control.FindControl<Popup>("MenuPopup");
        Assert.False(popup?.IsOpen);
        Assert.True(control.IsRevealPending);
        Assert.Contains("engaged", control.FindControl<Button>("Launcher")!.Classes);
        Assert.DoesNotContain("open", control.FindControl<Canvas>("MenuSurface")!.Classes);
        Assert.False(control.FindControl<Canvas>("MenuSurface")!.IsVisible);
        Assert.Equal(1_500, BoxelSystemActionMenu.RevealDelayMilliseconds);
        Assert.True(control.TryRevealMenu(launcherIsPointerOver: false));
        Assert.True(popup?.IsOpen);
        Assert.True(control.FindControl<Canvas>("MenuSurface")!.IsVisible);
        Assert.DoesNotContain(
            "open",
            control.FindControl<Canvas>("MenuSurface")!.Classes);
        control.AdvanceCommittedReveal();
        Assert.Contains(
            "open",
            control.FindControl<Canvas>("MenuSurface")!.Classes);
        Assert.True(control.FindControl<Canvas>("MenuHitSurface")!.IsVisible);
        Assert.Same(
            row.CompleteCommand,
            control.FindControl<Button>("CompleteActionButton")?.Command);
        Assert.Same(
            row.ReopenCommand,
            control.FindControl<Button>("ReopenActionButton")?.Command);
        Assert.False(row.ReopenCommand.CanExecute(null));
        Assert.Same(
            row.DeferCommand,
            control.FindControl<Button>("DeferActionButton")?.Command);
        Assert.Same(
            row.StartHereCommand,
            control.FindControl<Button>("StartHereActionButton")?.Command);
        Assert.Equal(1, control.FindControl<Button>("ReopenActionButton")!.Opacity);
        var actionButtons = new[]
        {
            control.FindControl<Button>("CompleteActionButton")!,
            control.FindControl<Button>("ReopenActionButton")!,
            control.FindControl<Button>("DeferActionButton")!,
            control.FindControl<Button>("StartHereActionButton")!,
        };
        Assert.All(
            actionButtons,
            button =>
            {
                Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            });
        control.FindControl<Button>("Launcher")!.RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        Assert.True(popup?.IsOpen);

        window.Close();
    }

    [AvaloniaFact]
    public void PassingAcrossLaunchersCancelsTheEarlierRevealIntent()
    {
        var first = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var second = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window
        {
            Content = new StackPanel
            {
                Children = { first, second },
            },
        };
        window.Show();

        first.BeginOpenIntent(explicitRequest: false);

        Assert.True(first.IsRevealPending);
        Assert.False(first.FindControl<Popup>("MenuPopup")!.IsOpen);
        Assert.Contains("engaged", first.FindControl<Button>("Launcher")!.Classes);

        first.CancelOpenIntent();
        second.BeginOpenIntent(explicitRequest: false);

        Assert.False(first.IsRevealPending);
        Assert.False(first.TryRevealMenu(launcherIsPointerOver: true));
        Assert.False(first.FindControl<Popup>("MenuPopup")!.IsOpen);
        Assert.DoesNotContain("engaged", first.FindControl<Button>("Launcher")!.Classes);
        Assert.True(second.IsRevealPending);
        Assert.False(second.FindControl<Popup>("MenuPopup")!.IsOpen);
        Assert.Contains("engaged", second.FindControl<Button>("Launcher")!.Classes);

        Assert.True(second.TryRevealMenu(launcherIsPointerOver: true));

        Assert.True(second.FindControl<Popup>("MenuPopup")!.IsOpen);
        Assert.True(second.FindControl<Canvas>("MenuSurface")!.IsVisible);
        window.Close();
    }

    private static BoxelSystemRowViewModel CreateRow()
    {
        return new BoxelSystemRowViewModel(new BoxelSystemRowOptions
        {
            Name = "Praea Euq IL-P c5-0",
            IsComplete = false,
            IsKnown = true,
            IsEmpty = false,
            IsDeferred = false,
            IsCurrent = false,
            IsNextIncomplete = true,
            Distance = "\u2014",
            VisitedAt = "\u2014",
            SpanshUpdatedAt = "\u2014",
            Complete = () => Task.CompletedTask,
            Reopen = () => Task.CompletedTask,
            Defer = () => Task.CompletedTask,
            StartHere = () => Task.CompletedTask,
        });
    }
}
