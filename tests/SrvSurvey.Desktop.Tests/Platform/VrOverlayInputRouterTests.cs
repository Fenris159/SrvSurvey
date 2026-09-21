using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Infrastructure;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class VrOverlayInputRouterTests
{
    [AvaloniaFact]
    public void ControllerPointerCanClickAnAvaloniaButton()
    {
        var button = new Button
        {
            Content = "Activate",
            ClickMode = ClickMode.Press,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = button,
        };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            var registration = new RegisteredOverlayWindow(window, "PlotJumpInfo", button, IsVisible: true);
            using var router = new VrOverlayInputRouter();

            router.Dispatch(
                registration,
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Move, 20, 20)
            );
            router.Dispatch(
                registration,
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonDown, 20, 20)
            );
            Assert.True(button.IsPressed);
            router.Dispatch(
                registration,
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonUp, 20, 20)
            );

            Assert.Equal(1, clicks);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ControllerScrollIsRaisedAtTheLastOpenVrPosition()
    {
        var panel = new Border();
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = panel,
        };
        Vector? delta = null;
        panel.PointerWheelChanged += (_, eventArgs) => delta = eventArgs.Delta;
        try
        {
            window.Show();
            var registration = new RegisteredOverlayWindow(window, "PlotJumpInfo", panel, IsVisible: true);
            using var router = new VrOverlayInputRouter();

            router.Dispatch(
                registration,
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Wheel, 20, 20, -1, 2)
            );

            Assert.Equal(new Vector(-1, 2), delta);
            router.Reset();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(VrOverlayPointerEventKind.RightButtonDown, PointerUpdateKind.RightButtonPressed)]
    [InlineData(VrOverlayPointerEventKind.RightButtonUp, PointerUpdateKind.RightButtonReleased)]
    [InlineData(VrOverlayPointerEventKind.MiddleButtonDown, PointerUpdateKind.MiddleButtonPressed)]
    [InlineData(VrOverlayPointerEventKind.MiddleButtonUp, PointerUpdateKind.MiddleButtonReleased)]
    public void ControllerButtonsPreserveTheirAvaloniaUpdateKind(
        VrOverlayPointerEventKind kind,
        PointerUpdateKind expected
    )
    {
        var panel = new Border();
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = panel,
        };
        PointerUpdateKind? observed = null;
        panel.AddHandler(
            InputElement.PointerPressedEvent,
            (_, eventArgs) => observed = eventArgs.GetCurrentPoint(panel).Properties.PointerUpdateKind,
            RoutingStrategies.Bubble,
            handledEventsToo: true
        );
        panel.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, eventArgs) => observed = eventArgs.GetCurrentPoint(panel).Properties.PointerUpdateKind,
            RoutingStrategies.Bubble,
            handledEventsToo: true
        );
        try
        {
            window.Show();
            var registration = new RegisteredOverlayWindow(window, "PlotJumpInfo", panel, IsVisible: true);

            using var router = new VrOverlayInputRouter();
            router.Dispatch(registration, new VrOverlayPointerEvent("PlotJumpInfo", kind, 20, 20));

            Assert.Equal(expected, observed);
        }
        finally
        {
            window.Close();
        }
    }
}
