using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using SrvSurvey.Desktop.Behaviors;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayPopupPlacementBehaviorTests
{
    [AvaloniaFact]
    public void EmbeddedPopupIgnoresTheDrawnDecorationOffset()
    {
        using Host host = Show(new Vector(12, 58));
        Assert.Equal(new Vector(12, 58), OverlayPopupPlacementBehavior.GetTopLevelRootOffset(host.Target));

        host.Popup.IsOpen = true;
        Render(host.Window);

        Assert.True(host.Popup.IsUsingOverlayLayer);
        Assert.Equal(-12, host.Popup.HorizontalOffset);
        Assert.Equal(-58, host.Popup.VerticalOffset);
        Assert.Equal(GetTargetBottom(host), GetPopupTop(host));

        host.Popup.IsOpen = false;
        Assert.Equal(0, host.Popup.HorizontalOffset);
        Assert.Equal(0, host.Popup.VerticalOffset);
    }

    [AvaloniaFact]
    public void ExistingOffsetsArePreservedAcrossOpenAndClose()
    {
        using Host host = Show(new Vector(12, 58), horizontalOffset: 7, verticalOffset: 11);

        host.Popup.IsOpen = true;
        Render(host.Window);

        Assert.Equal(-5, host.Popup.HorizontalOffset);
        Assert.Equal(-47, host.Popup.VerticalOffset);
        Assert.Equal(GetTargetBottom(host) + new PixelPoint(7, 11), GetPopupTop(host));

        host.Popup.IsOpen = false;
        Assert.Equal(7, host.Popup.HorizontalOffset);
        Assert.Equal(11, host.Popup.VerticalOffset);

        host.Popup.IsOpen = true;
        Render(host.Window);
        Assert.Equal(GetTargetBottom(host) + new PixelPoint(7, 11), GetPopupTop(host));
    }

    [AvaloniaFact]
    public void RegistrationIsIdempotent()
    {
        using Host host = Show(new Vector(12, 58));
        OverlayPopupPlacementBehavior.Register();
        OverlayPopupPlacementBehavior.Register();
        host.Popup.IsOpen = true;
        Render(host.Window);

        Assert.Equal(-12, host.Popup.HorizontalOffset);
        Assert.Equal(-58, host.Popup.VerticalOffset);
        Assert.Equal(GetTargetBottom(host), GetPopupTop(host));
    }

    [AvaloniaFact]
    public void PopupWithoutARootTranslationKeepsItsDeclaredOffsets()
    {
        using Host host = Show(default, horizontalOffset: 7, verticalOffset: 11);

        host.Popup.IsOpen = true;
        Render(host.Window);

        Assert.Equal(7, host.Popup.HorizontalOffset);
        Assert.Equal(11, host.Popup.VerticalOffset);
        Assert.Equal(GetTargetBottom(host) + new PixelPoint(7, 11), GetPopupTop(host));
    }

    [AvaloniaFact]
    public void StandardComboBoxPopupUsesTheGlobalCorrection()
    {
        var comboBox = new ComboBox
        {
            Width = 180,
            Margin = new Thickness(100, 50, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ItemsSource = new[] { "First", "Second", "Third" },
            SelectedIndex = 0,
        };
        var window = new Window
        {
            Width = 480,
            Height = 320,
            RenderTransform = new TranslateTransform(12, 58),
            Content = comboBox,
        };
        window.Show();
        Render(window);
        Popup popup = Assert.Single(comboBox.GetTemplateDescendants().OfType<Popup>());
        popup.ShouldUseOverlayLayer = true;

        comboBox.IsDropDownOpen = true;
        Render(window);

        Assert.True(popup.IsUsingOverlayLayer);
        Assert.Equal(-12, popup.HorizontalOffset);
        Assert.Equal(-58, popup.VerticalOffset);

        window.Close();
    }

    [Fact]
    public void UnattachedTargetHasNoRootOffset()
    {
        Assert.Equal(default, OverlayPopupPlacementBehavior.GetTopLevelRootOffset(new Border()));
    }

    private static Host Show(Vector rootTranslation, double horizontalOffset = 0, double verticalOffset = 0)
    {
        var target = new Border
        {
            Width = 120,
            Height = 36,
            Margin = new Thickness(100, 50, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var popupContent = new Border { Width = 160, Height = 80 };
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            ShouldUseOverlayLayer = true,
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
            Child = popupContent,
        };
        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);
        var window = new Window
        {
            Width = 480,
            Height = 320,
            RenderTransform = new TranslateTransform(rootTranslation.X, rootTranslation.Y),
            Content = host,
        };
        window.Show();
        Render(window);
        return new Host(window, target, popup, popupContent);
    }

    private static PixelPoint GetTargetBottom(Host host) =>
        host.Target.PointToScreen(new Point(host.Target.Bounds.Width / 2, host.Target.Bounds.Height));

    private static PixelPoint GetPopupTop(Host host) =>
        host.PopupContent.PointToScreen(new Point(host.PopupContent.Bounds.Width / 2, 0));

    private static void Render(Window window)
    {
        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.NotNull(window.CaptureRenderedFrame());
    }

    private sealed class Host(Window window, Border target, Popup popup, Border popupContent) : IDisposable
    {
        public Window Window { get; } = window;

        public Border Target { get; } = target;

        public Popup Popup { get; } = popup;

        public Border PopupContent { get; } = popupContent;

        public void Dispose()
        {
            Window.Close();
        }
    }
}
