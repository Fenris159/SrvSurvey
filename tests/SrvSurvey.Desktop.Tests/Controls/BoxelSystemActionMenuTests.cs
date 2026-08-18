using Avalonia;
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
    public void FlyoutActionsHugLauncherWithDirectionalCurves()
    {
        var control = new BoxelSystemActionMenu
        {
            DataContext = CreateRow(),
        };
        var window = new Window { Content = control };
        window.Show();

        var complete = control.FindControl<Button>("CompleteActionButton")!;
        var reopen = control.FindControl<Button>("ReopenActionButton")!;
        var defer = control.FindControl<Button>("DeferActionButton")!;
        var startHere = control.FindControl<Button>("StartHereActionButton")!;

        Assert.Equal(106, complete.GetValue(Canvas.LeftProperty));
        Assert.Equal(12, complete.GetValue(Canvas.TopProperty));
        Assert.Equal(84, reopen.GetValue(Canvas.LeftProperty));
        Assert.Equal(34, reopen.GetValue(Canvas.TopProperty));
        Assert.Equal(184, defer.GetValue(Canvas.LeftProperty));
        Assert.Equal(34, defer.GetValue(Canvas.TopProperty));
        Assert.Equal(106, startHere.GetValue(Canvas.LeftProperty));
        Assert.Equal(108, startHere.GetValue(Canvas.TopProperty));

        Assert.Equal(118, complete.Width);
        Assert.Equal(66, complete.Height);
        Assert.Equal(62, reopen.Width);
        Assert.Equal(118, reopen.Height);
        Assert.Equal(62, defer.Width);
        Assert.Equal(118, defer.Height);
        Assert.Equal(118, startHere.Width);
        Assert.Equal(66, startHere.Height);

        Assert.Equal(
            complete.GetValue(Canvas.LeftProperty) + 38,
            reopen.GetValue(Canvas.LeftProperty) + 60);
        Assert.Equal(
            complete.GetValue(Canvas.LeftProperty) + 80,
            defer.GetValue(Canvas.LeftProperty) + 2);
        Assert.Equal(
            complete.GetValue(Canvas.TopProperty) + 63,
            reopen.GetValue(Canvas.TopProperty) + 41);
        Assert.Equal(
            startHere.GetValue(Canvas.TopProperty) + 3,
            reopen.GetValue(Canvas.TopProperty) + 77);

        AssertDirectionalClip(complete, new Point(59, 31), new Point(59, 64));
        AssertDirectionalClip(reopen, new Point(29, 59), new Point(60, 59));
        AssertDirectionalClip(defer, new Point(33, 59), new Point(2, 59));
        AssertDirectionalClip(startHere, new Point(59, 31), new Point(59, 2));
        AssertMirroredSideGeometry(reopen, defer);
        AssertSideButtonsFitTopWedgeRadius(complete, reopen, defer);

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

    [AvaloniaFact]
    public void PointerExitClosesPopupAndOnlyOneMenuRemainsActive()
    {
        var first = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var second = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window
        {
            Content = new StackPanel { Children = { first, second } },
        };
        window.Show();

        first.BeginOpenIntent(explicitRequest: true);
        Assert.True(first.TryRevealMenu(launcherIsPointerOver: false));
        Assert.True(first.FindControl<Popup>("MenuPopup")!.IsOpen);

        Assert.True(first.TryCloseForPointerExit(
            launcherIsPointerOver: false,
            menuIsPointerOver: false));
        Assert.False(first.FindControl<Popup>("MenuPopup")!.IsOpen);

        first.BeginOpenIntent(explicitRequest: true);
        Assert.True(first.TryRevealMenu(launcherIsPointerOver: false));
        second.BeginOpenIntent(explicitRequest: true);
        Assert.False(first.FindControl<Popup>("MenuPopup")!.IsOpen);
        Assert.True(second.TryRevealMenu(launcherIsPointerOver: false));
        Assert.True(second.FindControl<Popup>("MenuPopup")!.IsOpen);

        window.Close();
    }

    [AvaloniaFact]
    public void ScrollingDismissesTheActiveMenuImmediately()
    {
        var control = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window { Content = control };
        window.Show();

        control.BeginOpenIntent(explicitRequest: true);
        Assert.True(control.TryRevealMenu(launcherIsPointerOver: false));
        Assert.True(control.FindControl<Popup>("MenuPopup")!.IsOpen);

        Assert.True(BoxelSystemActionMenu.DismissActiveMenuForScroll());

        Assert.False(control.FindControl<Popup>("MenuPopup")!.IsOpen);
        Assert.False(control.IsRevealPending);
        Assert.DoesNotContain(
            "engaged",
            control.FindControl<Button>("Launcher")!.Classes);
        Assert.False(BoxelSystemActionMenu.DismissActiveMenuForScroll());

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

    private static void AssertDirectionalClip(
        Button button,
        Point labelPoint,
        Point inwardCutoutPoint)
    {
        var clip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(button.Clip);
        Assert.True(clip.FillContains(labelPoint));
        Assert.False(clip.FillContains(inwardCutoutPoint));
    }

    private static void AssertMirroredSideGeometry(Button left, Button right)
    {
        var leftClip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(left.Clip);
        var rightClip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(right.Clip);

        for (var y = 1; y < left.Height; y += 4)
        {
            for (var x = 1; x < left.Width; x += 4)
            {
                Assert.Equal(
                    leftClip.FillContains(new Point(x, y)),
                    rightClip.FillContains(new Point(left.Width - x, y)));
            }
        }
    }

    private static void AssertSideButtonsFitTopWedgeRadius(
        Button top,
        Button left,
        Button right)
    {
        var topClip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(top.Clip);
        var leftClip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(left.Clip);
        var rightClip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(right.Clip);
        const double centerX = 165;
        const double centerY = 93;
        var topOuterX = top.GetValue(Canvas.LeftProperty) + 4;
        var topOuterY = top.GetValue(Canvas.TopProperty) + 22;
        var guideRadius = Math.Sqrt(
            Math.Pow(centerX - topOuterX, 2)
            + Math.Pow(centerY - topOuterY, 2));
        var leftReach = centerX
            - (left.GetValue(Canvas.LeftProperty) + leftClip.Bounds.Left);
        var rightReach = right.GetValue(Canvas.LeftProperty)
            + rightClip.Bounds.Right
            - centerX;

        Assert.InRange(leftReach, guideRadius - 1, guideRadius);
        Assert.InRange(rightReach, guideRadius - 1, guideRadius);
    }
}
