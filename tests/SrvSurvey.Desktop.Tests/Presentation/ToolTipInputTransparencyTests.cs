using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Behaviors;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ToolTipInputTransparencyTests
{
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00A0")]
    public void BlankBoundHintsDoNotOpenAnEmptyPopupAndKeepTheirBinding(string? blank)
    {
        var source = new Button { Content = "Details", Tag = blank };
        source.Bind(ToolTip.TipProperty, new Binding(nameof(Control.Tag)) { Source = source });
        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = source,
        };
        try
        {
            window.Show();
            Render(window);
            ToolTip.SetIsOpen(source, true);
            Render(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ToolTip>(), tip => tip.IsEffectivelyVisible);
            Assert.Null(ToolTip.GetTip(source));
            ToolTip.SetIsOpen(source, false);

            source.Tag = "Useful contextual detail";
            Assert.Equal("Useful contextual detail", ToolTip.GetTip(source));
            ToolTip.SetIsOpen(source, true);
            Render(window);
            Assert.Contains(window.GetVisualDescendants().OfType<ToolTip>(), tip => tip.IsEffectivelyVisible);

            source.Tag = blank;
            Render(window);
            Assert.Null(ToolTip.GetTip(source));
            Assert.False(ToolTip.GetIsOpen(source));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ToolTip>(), tip => tip.IsEffectivelyVisible);
            source.Tag = "Updated contextual detail";
            Assert.Equal("Updated contextual detail", ToolTip.GetTip(source));
        }
        finally
        {
            ToolTip.SetIsOpen(source, false);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RichHintsKeepTheirContentAndCloseWhenReplacedByBlankText(bool explicitToolTip)
    {
        ToolTipContentBehavior.Register();
        ToolTipContentBehavior.Register();
        var detail = new StackPanel();
        detail.Children.Add(new TextBlock { Text = "Planned: 100 t" });
        detail.Children.Add(new TextBlock { Text = "Available: 75 t" });
        object content = explicitToolTip ? new ToolTip { Content = detail } : detail;
        var source = new Button { Content = "Supply" };
        ToolTip.SetTip(source, content);
        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = source,
        };
        try
        {
            window.Show();
            ToolTip.SetIsOpen(source, true);
            Render(window);
            Assert.Same(content, ToolTip.GetTip(source));
            ToolTip toolTip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>());
            Assert.True(toolTip.IsEffectivelyVisible);
            Assert.Same(detail, toolTip.Content);
            Assert.Collection(
                toolTip.GetVisualDescendants().OfType<TextBlock>(),
                block => Assert.Equal("Planned: 100 t", block.Text),
                block => Assert.Equal("Available: 75 t", block.Text)
            );

            ToolTip.SetTip(source, "");
            Render(window);
            Assert.Null(ToolTip.GetTip(source));
            Assert.False(ToolTip.GetIsOpen(source));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ToolTip>(), tip => tip.IsEffectivelyVisible);
        }
        finally
        {
            ToolTip.SetIsOpen(source, false);
            window.Close();
        }
    }

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
        using WriteableBitmap? firstFrame = window.CaptureRenderedFrame();
        Assert.NotNull(firstFrame);
        using WriteableBitmap? secondFrame = window.CaptureRenderedFrame();
        Assert.NotNull(secondFrame);
    }
}
