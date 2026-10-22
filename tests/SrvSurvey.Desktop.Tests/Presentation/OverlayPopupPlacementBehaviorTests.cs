using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    [AvaloniaTheory]
    [InlineData(30, 58)]
    [InlineData(220, 58)]
    [InlineData(260, 58)]
    [InlineData(30, 0)]
    [InlineData(260, 0)]
    public void DropdownRemainsAttachedWhenFlippedAtWindowEdgeAndScrolled(int top, int rootOffset)
    {
        var combo = new ComboBox
        {
            Width = 240,
            Margin = new Thickness(20, top, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { "All systems", "Selected system" },
            SelectedIndex = 1,
        };
        var content = new StackPanel { Height = 800 };
        content.Children.Add(combo);
        var scroll = new ScrollViewer { Content = content };
        var window = new Window
        {
            Width = 480,
            Height = 350,
            RenderTransform = new TranslateTransform(0, rootOffset),
            Content = scroll,
        };
        try
        {
            window.Show();
            Render(window);
            Popup popup = Assert.Single(combo.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = true;
            combo.IsDropDownOpen = true;
            Render(window);
            AssertAttached(combo, popup);
            scroll.Offset = new Vector(0, 20);
            Render(window);
            Assert.True(combo.IsDropDownOpen);
            AssertAttached(combo, popup);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertAttached(ComboBox combo, Popup popup) =>
        Assert.True(
            IsAttached(combo, popup),
            $"Dropdown detached: target={combo.PointToScreen(default)}, popup={popup.Child!.PointToScreen(default)}, height={popup.Child!.Bounds.Height}"
        );

    [AvaloniaTheory]
    [InlineData(30, 0, true)]
    [InlineData(260, 0, true)]
    [InlineData(30, 400, true)]
    [InlineData(260, 400, true)]
    [InlineData(330, 0, true)]
    [InlineData(330, 400, true)]
    [InlineData(30, 400, false)]
    [InlineData(260, 400, false)]
    [InlineData(330, 400, false)]
    public void ClickingDropdownKeepsItOpenWithoutJumpingAwayFromTheControl(int top, int offset, bool overlay)
    {
        var combo = new ComboBox
        {
            Width = 240,
            MaxDropDownHeight = 120,
            Margin = new Thickness(20, top + offset, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = Enumerable.Range(0, 30).Select(index => $"Item {index}").ToArray(),
            SelectedIndex = 25,
        };
        var content = new StackPanel { Height = 1200 };
        content.Children.Add(combo);
        var scroll = new ScrollViewer { Content = content };
        var window = new Window
        {
            Content = scroll,
            Width = 480,
            Height = 350,
        };
        try
        {
            window.Show();
            Render(window);
            scroll.Offset = new Vector(0, offset);
            Render(window);
            Popup popup = Assert.Single(combo.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = overlay;
            Point origin = combo.TranslatePoint(default, window)!.Value;
            Point click =
                origin
                + new Vector(
                    combo.Bounds.Width - 15,
                    Math.Min(combo.Bounds.Height / 2, window.ClientSize.Height - origin.Y - 2)
                );
            window.MouseDown(click, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
            Render(window);
            Assert.True(
                combo.IsDropDownOpen,
                $"Dropdown closed after click; page offset={scroll.Offset} (was {offset})."
            );
            Assert.InRange(scroll.Offset.Y - offset, 0, top == 330 ? combo.Bounds.Height : 0);
            AssertAttached(combo, popup);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeyboardFocusRevealsTheSelectorAndMenuFocusOnlyScrollsTheMenu(bool overlay)
    {
        var combo = new ComboBox
        {
            Width = 200,
            MaxDropDownHeight = 120,
            ItemsSource = Enumerable.Range(0, 30).Select(index => $"Item {index}").ToArray(),
            SelectedIndex = 29,
        };
        var scroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 400 },
                    combo,
                    new Border { Height = 400 },
                },
            },
        };
        var window = new Window
        {
            Content = scroll,
            Width = 480,
            Height = 350,
        };
        try
        {
            window.Show();
            Render(window);
            Assert.True(combo.Focus(NavigationMethod.Tab));
            Render(window);
            Assert.True(scroll.Offset.Y > 0);
            Vector offset = scroll.Offset;
            Popup popup = Assert.Single(combo.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = overlay;
            window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
            Render(window);
            Assert.True(combo.IsDropDownOpen);
            Assert.Equal(offset, scroll.Offset);
            ScrollViewer menu = Assert.Single(popup.Child!.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(menu.Offset.Y > 0);
            double menuOffset = menu.Offset.Y;
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Render(window);
            Assert.True(combo.ContainerFromIndex(0)!.IsFocused);
            Assert.True(menu.Offset.Y < menuOffset);
            Assert.Equal(offset, scroll.Offset);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Render(window);
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(offset, scroll.Offset);

            combo.IsDropDownOpen = true;
            Render(window);
            scroll.ScrollToHome();
            Render(window);
            combo.BringIntoView();
            Render(window);
            Assert.Equal(offset, scroll.Offset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpeningDropdownDuringPendingPageScrollKeepsItOpen(bool overlay)
    {
        var combo = new ComboBox
        {
            Width = 200,
            Margin = new Thickness(0, 100, 0, 0),
            ItemsSource = new[] { "All systems", "Selected system" },
            SelectedIndex = 0,
        };
        var content = new StackPanel { Height = 800 };
        content.Children.Add(combo);
        var scroll = new ScrollViewer { Content = content };
        var window = new Window
        {
            Content = scroll,
            Width = 480,
            Height = 350,
        };
        try
        {
            window.Show();
            Render(window);
            Popup popup = Assert.Single(combo.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = overlay;
            scroll.Offset = new Vector(0, 20);
            combo.IsDropDownOpen = true;
            Render(window);
            Assert.True(combo.IsDropDownOpen);
            AssertAttached(combo, popup);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScrollingMenuOrPageKeepsTheDropdownOpenAndAttached(bool overlay)
    {
        var combo = new ComboBox
        {
            Width = 200,
            MaxDropDownHeight = 120,
            ItemsSource = Enumerable.Range(0, 30).Select(index => $"Item {index}").ToArray(),
            SelectedIndex = 0,
        };
        var content = new StackPanel { Height = 800 };
        content.Children.Add(combo);
        var scroll = new ScrollViewer { Content = content };
        var window = new Window
        {
            Content = scroll,
            Width = 480,
            Height = 350,
        };
        try
        {
            window.Show();
            Render(window);
            Popup popup = Assert.Single(combo.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = overlay;
            combo.IsDropDownOpen = true;
            Render(window);
            ScrollViewer menu = Assert.Single(popup.Child!.GetVisualDescendants().OfType<ScrollViewer>());
            menu.Offset = new Vector(0, 100);
            Render(window);
            Assert.True(combo.IsDropDownOpen);
            Assert.True(menu.Offset.Y > 0);
            scroll.Offset = new Vector(0, 20);
            Render(window);
            Assert.True(combo.IsDropDownOpen);
            AssertAttached(combo, popup);
            scroll.ScrollToHome();
            Render(window);
            combo.IsDropDownOpen = true;
            Render(window);
            Assert.True(combo.IsDropDownOpen);
            AssertAttached(combo, popup);
        }
        finally
        {
            window.Close();
        }
    }

    private static bool IsAttached(ComboBox combo, Popup popup)
    {
        PixelPoint targetTop = combo.PointToScreen(default);
        PixelPoint targetBottom = combo.PointToScreen(new Point(0, combo.Bounds.Height));
        PixelPoint popupTop = popup.Child!.PointToScreen(default);
        PixelPoint popupBottom = popup.Child!.PointToScreen(new Point(0, popup.Child!.Bounds.Height));
        return Math.Abs(popupTop.Y - targetBottom.Y) <= 2 || Math.Abs(popupBottom.Y - targetTop.Y) <= 2;
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
