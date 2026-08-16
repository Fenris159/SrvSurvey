using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Behaviors;

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

    [AvaloniaFact]
    public void WheelAtListBoxEndpointDoesNotScrollOuterPage()
    {
        var listBox = new ListBox
        {
            ItemsSource = Enumerable.Range(0, 30)
                .Select(index => $"Event {index}")
                .ToArray(),
        };

        AssertWheelAtEndpointDoesNotScrollOuterPage(listBox);
    }

    [AvaloniaFact]
    public void WheelAtTextBoxEndpointDoesNotScrollOuterPage()
    {
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            Text = string.Join(
                Environment.NewLine,
                Enumerable.Range(0, 40).Select(index => $"Log line {index}")),
        };

        AssertWheelAtEndpointDoesNotScrollOuterPage(textBox);
    }

    [AvaloniaFact]
    public void ListBoxSelectionDoesNotScrollOuterPage()
    {
        var listBox = new ListBox
        {
            Height = 120,
            ItemsSource = Enumerable.Range(0, 30)
                .Select(index => $"Event {index}")
                .ToArray(),
        };
        var outer = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 400 },
                    listBox,
                    new Border { Height = 400 },
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
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.True(ListBoxBringIntoViewBehavior.GetContain(listBox));
            var listScroller = listBox.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single();

            listBox.SelectedIndex = 29;
            Assert.NotNull(window.CaptureRenderedFrame());

            Assert.True(listScroller.Offset.Y > 0);
            Assert.Equal(0, outer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertWheelAtEndpointDoesNotScrollOuterPage(
        Control nestedScroller)
    {
        nestedScroller.Height = 120;
        var outer = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 40 },
                    nestedScroller,
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
            var scrollViewer = nestedScroller.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single();
            scrollViewer.Offset = new Vector(
                0,
                scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var outerOffset = outer.Offset;

            Assert.False(
                ScrollViewer.GetIsScrollChainingEnabled(nestedScroller));
            window.MouseWheel(
                new Point(80, 90),
                new Vector(0, -1));

            Assert.Equal(outerOffset, outer.Offset);
            Assert.Equal(
                scrollViewer.Extent.Height - scrollViewer.Viewport.Height,
                scrollViewer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }
}
