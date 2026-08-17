using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelSystemActionMenuTests
{
    [AvaloniaFact]
    public void LauncherOpensPopupWithCommandsFromTheRow()
    {
        var row = new BoxelSystemRowViewModel(new BoxelSystemRowOptions
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
        var control = new BoxelSystemActionMenu
        {
            DataContext = row,
        };
        var window = new Window { Content = control };
        window.Show();

        control.FindControl<Button>("Launcher")!.RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));

        var popup = control.FindControl<Popup>("MenuPopup");
        Assert.True(popup?.IsOpen);
        Assert.Same(
            row.CompleteCommand,
            control.FindControl<Button>("CompleteActionButton")?.Command);
        Assert.Same(
            row.ReopenCommand,
            control.FindControl<Button>("ReopenActionButton")?.Command);
        Assert.Same(
            row.DeferCommand,
            control.FindControl<Button>("DeferActionButton")?.Command);
        Assert.Same(
            row.StartHereCommand,
            control.FindControl<Button>("StartHereActionButton")?.Command);

        window.Close();
    }
}
