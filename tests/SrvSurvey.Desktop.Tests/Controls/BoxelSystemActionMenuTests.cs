using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelSystemActionMenuTests
{
    [AvaloniaFact]
    public void LauncherOpensPopupWithCommandsFromTheRow()
    {
        BoxelSystemRowViewModel row = CreateRow();
        var control = new BoxelSystemActionMenu { DataContext = row };
        var window = new Window { Content = control };
        window.Show();

        control.FindControl<Button>("Launcher")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Popup? popup = control.FindControl<Popup>("MenuPopup");
        Assert.False(popup?.IsOpen);
        Assert.True(control.IsRevealPending);
        Assert.Contains("engaged", control.FindControl<Button>("Launcher")!.Classes);
        Assert.DoesNotContain("open", control.FindControl<Canvas>("MenuSurface")!.Classes);
        Assert.False(control.FindControl<Canvas>("MenuSurface")!.IsVisible);
        Assert.Equal(1_500, BoxelSystemActionMenu.RevealDelayMilliseconds);
        Assert.True(control.TryRevealMenu(launcherIsPointerOver: false));
        Assert.True(popup?.IsOpen);
        Assert.Same(control.FindControl<Button>("Launcher"), popup?.PlacementTarget);
        Assert.Equal(PopupAnchor.None, popup?.PlacementAnchor);
        Assert.Equal(PopupGravity.None, popup?.PlacementGravity);
        Assert.Equal(PopupPositionerConstraintAdjustment.None, popup?.PlacementConstraintAdjustment);
        Assert.Null(popup?.PlacementRect);
        Assert.True(control.FindControl<Canvas>("MenuSurface")!.IsVisible);
        Assert.DoesNotContain("open", control.FindControl<Canvas>("MenuSurface")!.Classes);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        control.AdvanceCommittedReveal();
        Assert.Contains("open", control.FindControl<Canvas>("MenuSurface")!.Classes);
        Assert.True(control.FindControl<Canvas>("MenuHitSurface")!.IsVisible);
        Assert.Same(row.CompleteCommand, control.FindControl<Button>("CompleteActionButton")?.Command);
        Assert.Same(row.ReopenCommand, control.FindControl<Button>("ReopenActionButton")?.Command);
        Assert.False(row.ReopenCommand.CanExecute(null));
        Assert.Same(row.DeferCommand, control.FindControl<Button>("DeferActionButton")?.Command);
        Assert.Same(row.StartHereCommand, control.FindControl<Button>("StartHereActionButton")?.Command);
        Assert.Equal(1, control.FindControl<Button>("ReopenActionButton")!.Opacity);
        Button[] actionButtons = new[]
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
            }
        );
        control.FindControl<Button>("Launcher")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(popup?.IsOpen);

        window.Close();
    }

    [AvaloniaFact]
    public void FlyoutActionsHugLauncherWithDirectionalCurves()
    {
        var control = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window { Content = control };
        window.Show();

        Button complete = control.FindControl<Button>("CompleteActionButton")!;
        Button reopen = control.FindControl<Button>("ReopenActionButton")!;
        Button defer = control.FindControl<Button>("DeferActionButton")!;
        Button startHere = control.FindControl<Button>("StartHereActionButton")!;
        Canvas hitSurface = control.FindControl<Canvas>("MenuHitSurface")!;
        Canvas menuSurface = control.FindControl<Canvas>("MenuSurface")!;

        Assert.Equal(170, hitSurface.Width);
        Assert.Equal(178, hitSurface.Height);
        Assert.Equal(hitSurface.Width, menuSurface.Width);
        Assert.Equal(hitSurface.Height, menuSurface.Height);
        Assert.Equal(26, complete.GetValue(Canvas.LeftProperty));
        Assert.Equal(8, complete.GetValue(Canvas.TopProperty));
        Assert.Equal(4, reopen.GetValue(Canvas.LeftProperty));
        Assert.Equal(30, reopen.GetValue(Canvas.TopProperty));
        Assert.Equal(104, defer.GetValue(Canvas.LeftProperty));
        Assert.Equal(30, defer.GetValue(Canvas.TopProperty));
        Assert.Equal(26, startHere.GetValue(Canvas.LeftProperty));
        Assert.Equal(104, startHere.GetValue(Canvas.TopProperty));

        Assert.Equal(118, complete.Width);
        Assert.Equal(66, complete.Height);
        Assert.Equal(62, reopen.Width);
        Assert.Equal(118, reopen.Height);
        Assert.Equal(62, defer.Width);
        Assert.Equal(118, defer.Height);
        Assert.Equal(118, startHere.Width);
        Assert.Equal(66, startHere.Height);

        Assert.Equal(complete.GetValue(Canvas.LeftProperty) + 38, reopen.GetValue(Canvas.LeftProperty) + 60);
        Assert.Equal(complete.GetValue(Canvas.LeftProperty) + 80, defer.GetValue(Canvas.LeftProperty) + 2);
        Assert.Equal(complete.GetValue(Canvas.TopProperty) + 63, reopen.GetValue(Canvas.TopProperty) + 41);
        Assert.Equal(startHere.GetValue(Canvas.TopProperty) + 3, reopen.GetValue(Canvas.TopProperty) + 77);

        AssertDirectionalClip(complete, new Point(59, 31), new Point(59, 64));
        AssertDirectionalClip(reopen, new Point(29, 59), new Point(60, 59));
        AssertDirectionalClip(defer, new Point(33, 59), new Point(2, 59));
        AssertDirectionalClip(startHere, new Point(59, 31), new Point(59, 2));
        AssertMirroredSideGeometry(reopen, defer);
        AssertSideButtonsFitTopWedgeRadius(complete, reopen, defer);
        AssertRadialMenuIsCenteredInPopup(hitSurface, complete, startHere);

        window.Close();
    }

    [AvaloniaFact]
    public void HoverRevealUsesTheLauncherBoundsAsItsCenterAnchor()
    {
        var control = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window { Content = control };
        window.Show();
        control.BeginOpenIntent(explicitRequest: false);

        Assert.True(control.TryRevealMenu(launcherIsPointerOver: true));
        Assert.Null(control.FindControl<Popup>("MenuPopup")!.PlacementRect);

        window.Close();
    }

    [AvaloniaFact]
    public void RadialHoleCenterMatchesLauncherCenterOnScreen()
    {
        var control = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = new Border
            {
                Width = 110,
                Height = 52,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = control,
            },
        };
        window.Show();

        control.BeginOpenIntent(explicitRequest: true);
        Assert.True(control.TryRevealMenu(launcherIsPointerOver: false));
        using WriteableBitmap? frame = window.CaptureRenderedFrame();

        Button launcher = control.FindControl<Button>("Launcher")!;
        Canvas menuSurface = control.FindControl<Canvas>("MenuSurface")!;
        PixelPoint launcherCenter = launcher.PointToScreen(
            new Point(launcher.Bounds.Width / 2, launcher.Bounds.Height / 2)
        );
        PixelPoint radialCenter = menuSurface.PointToScreen(
            new Point(menuSurface.Bounds.Width / 2, menuSurface.Bounds.Height / 2)
        );

        Assert.Equal(launcherCenter, radialCenter);

        window.Close();
    }

    [AvaloniaFact]
    public void RevealCorrectsAHostPlacementOffsetBeforeShowingTheMenu()
    {
        var control = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = control,
        };
        window.Show();

        control.BeginOpenIntent(explicitRequest: true);
        Assert.True(control.TryRevealMenu(launcherIsPointerOver: false));
        Popup popup = control.FindControl<Popup>("MenuPopup")!;
        popup.HorizontalOffset = 12;
        popup.VerticalOffset = 58;
        using WriteableBitmap? displacedFrame = window.CaptureRenderedFrame();

        control.AdvanceCommittedReveal();
        using WriteableBitmap? correctedFrame = window.CaptureRenderedFrame();

        Button launcher = control.FindControl<Button>("Launcher")!;
        Canvas menuSurface = control.FindControl<Canvas>("MenuSurface")!;
        PixelPoint launcherCenter = launcher.PointToScreen(
            new Point(launcher.Bounds.Width / 2, launcher.Bounds.Height / 2)
        );
        PixelPoint radialCenter = menuSurface.PointToScreen(
            new Point(menuSurface.Bounds.Width / 2, menuSurface.Bounds.Height / 2)
        );
        Assert.Equal(launcherCenter, radialCenter);
        Assert.Contains("open", menuSurface.Classes);

        window.Close();
    }

    [AvaloniaFact]
    public void PassingAcrossLaunchersCancelsTheEarlierRevealIntent()
    {
        var first = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var second = new BoxelSystemActionMenu { DataContext = CreateRow() };
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
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
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
        window.Show();

        first.BeginOpenIntent(explicitRequest: true);
        Assert.True(first.TryRevealMenu(launcherIsPointerOver: false));
        Assert.True(first.FindControl<Popup>("MenuPopup")!.IsOpen);

        Assert.True(first.TryCloseForPointerExit(launcherIsPointerOver: false, menuIsPointerOver: false));
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
        Assert.DoesNotContain("engaged", control.FindControl<Button>("Launcher")!.Classes);
        Assert.False(BoxelSystemActionMenu.DismissActiveMenuForScroll());

        window.Close();
    }

    private static BoxelSystemRowViewModel CreateRow()
    {
        return new BoxelSystemRowViewModel(
            new BoxelSystemRowOptions
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
            }
        );
    }

    private static void AssertDirectionalClip(Button button, Point labelPoint, Point inwardCutoutPoint)
    {
        Geometry clip = Assert.IsType<Avalonia.Media.Geometry>(button.Clip, exactMatch: false);
        Assert.True(clip.FillContains(labelPoint));
        Assert.False(clip.FillContains(inwardCutoutPoint));
    }

    private static void AssertMirroredSideGeometry(Button left, Button right)
    {
        Geometry leftClip = Assert.IsType<Avalonia.Media.Geometry>(left.Clip, exactMatch: false);
        Geometry rightClip = Assert.IsType<Avalonia.Media.Geometry>(right.Clip, exactMatch: false);

        for (int y = 1; y < left.Height; y += 4)
        {
            for (int x = 1; x < left.Width; x += 4)
            {
                Assert.Equal(
                    leftClip.FillContains(new Point(x, y)),
                    rightClip.FillContains(new Point(left.Width - x, y))
                );
            }
        }
    }

    private static void AssertSideButtonsFitTopWedgeRadius(Button top, Button left, Button right)
    {
        Assert.IsType<Avalonia.Media.Geometry>(top.Clip, exactMatch: false);
        Geometry leftClip = Assert.IsType<Avalonia.Media.Geometry>(left.Clip, exactMatch: false);
        Geometry rightClip = Assert.IsType<Avalonia.Media.Geometry>(right.Clip, exactMatch: false);
        const double centerX = 85;
        const double centerY = 89;
        double topOuterX = top.GetValue(Canvas.LeftProperty) + 4;
        double topOuterY = top.GetValue(Canvas.TopProperty) + 22;
        double guideRadius = Math.Sqrt(Math.Pow(centerX - topOuterX, 2) + Math.Pow(centerY - topOuterY, 2));
        double leftReach = centerX - (left.GetValue(Canvas.LeftProperty) + leftClip.Bounds.Left);
        double rightReach = right.GetValue(Canvas.LeftProperty) + rightClip.Bounds.Right - centerX;

        Assert.InRange(leftReach, guideRadius - 1, guideRadius);
        Assert.InRange(rightReach, guideRadius - 1, guideRadius);
    }

    private static void AssertRadialMenuIsCenteredInPopup(Canvas surface, Button top, Button bottom)
    {
        double radialCenterX = top.GetValue(Canvas.LeftProperty) + (top.Width / 2);
        double radialCenterY =
            (top.GetValue(Canvas.TopProperty) + top.Height + bottom.GetValue(Canvas.TopProperty)) / 2;

        Assert.Equal(surface.Width / 2, radialCenterX);
        Assert.Equal(surface.Height / 2, radialCenterY);
    }
}
