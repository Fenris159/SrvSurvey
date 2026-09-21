using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ToolTipInputTransparencyTests
{
    [AvaloniaFact]
    public void OpenToolTipDoesNotBlockTheControlBehindIt()
    {
        var source = new Border
        {
            Width = 160,
            Height = 30,
            Margin = new Thickness(80, 20, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var underlyingButton = new Button
        {
            Width = 200,
            Height = 50,
            Margin = new Thickness(80, 50, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = "Click through",
        };
        var content = new Grid();
        content.Children.Add(source);
        content.Children.Add(underlyingButton);
        var window = new Window
        {
            Width = 400,
            Height = 240,
            Content = content,
        };

        ToolTip.SetTip(source, "Informational tooltip");
        ToolTip.SetPlacement(source, PlacementMode.Bottom);
        ToolTip.SetVerticalOffset(source, 0);

        try
        {
            window.Show();
            Render(window);
            ToolTip.SetIsOpen(source, true);
            Render(window);

            ToolTip toolTip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>());
            Point hitPoint = toolTip
                .TranslatePoint(new Point(toolTip.Bounds.Width / 2, toolTip.Bounds.Height / 2), window)!
                .Value;
            IInputElement? hit = window.InputHitTest(hitPoint);

            Assert.NotNull(hit);
            Assert.True(
                ReferenceEquals(hit, underlyingButton)
                    || (hit as Visual)?.GetVisualAncestors().Contains(underlyingButton) == true,
                $"Expected the button beneath the tooltip, but hit {hit.GetType().Name}."
            );
        }
        finally
        {
            ToolTip.SetIsOpen(source, false);
            window.Close();
        }
    }

    private static void Render(Window window)
    {
        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.NotNull(window.CaptureRenderedFrame());
    }
}
