using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ScrollViewerBehaviorTests
{
    [AvaloniaFact]
    public void WheelAtNestedEndpointsDoesNotScrollOuterPage()
    {
        var inner = new ScrollViewer
        {
            Height = 120,
            Content = new Border { Height = 600 },
        };
        var outer = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 40 },
                    inner,
                    new Border { Height = 600 },
                },
            },
        };
        var window = new Window
        {
            Width = 320,
            Height = 260,
            Content = outer,
        };

        try
        {
            window.Show();
            inner.Offset = new Vector(
                0,
                inner.Extent.Height - inner.Viewport.Height);
            var outerOffset = outer.Offset;

            window.MouseWheel(
                new Point(80, 90),
                new Vector(0, -1));

            Assert.False(inner.IsScrollChainingEnabled);
            Assert.Equal(outerOffset, outer.Offset);
            Assert.Equal(
                inner.Extent.Height - inner.Viewport.Height,
                inner.Offset.Y);

            inner.Offset = default;
            outer.Offset = new Vector(0, 20);
            outerOffset = outer.Offset;

            window.MouseWheel(
                new Point(80, 90),
                new Vector(0, 1));

            Assert.Equal(outerOffset, outer.Offset);
            Assert.Equal(0, inner.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }
}
