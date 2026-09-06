using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

/// <summary>The transparent window itself is the capture rectangle; no editor chrome offsets are saved.</summary>
public sealed class MiningCalibrationWindow : Window
{
    private readonly MiningDetectionViewModel model;
    private readonly PixelRect viewport;
    private readonly CalibrationCanvas canvas;
    private readonly TextBlock status = new() { FontSize = 11 };
    private readonly CheckBox test = new() { Content = "Test", FontSize = 10 };
    private readonly CheckBox showSearch = new() { Content = "Search bounds", FontSize = 10 };
    private readonly TextBlock values = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) };
    private readonly Button lessMovement;
    private readonly Button moreMovement;
    private bool positioning;
    internal Window ToolsWindow { get; }

    public MiningCalibrationWindow(MiningDetectionViewModel model, PixelRect viewport, double scaling)
    {
        this.model = model;
        this.viewport = viewport;
        Title = "SrvSurvey mining rig calibration";
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = true;
        MinWidth = 160;
        MinHeight = 120;
        canvas = new CalibrationCanvas(model);
        var panel = new Grid();
        panel.Children.Add(canvas);
        var tools = new WrapPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20))
        };
        tools.Children.Add(new TextBlock { Text = "RIG CALIBRATION", FontSize = 10, Margin = new Thickness(3) });
        AddButton(tools, "Size−", "Reduce circle diameter by 2 pixels", () => ResizeCircles(-2));
        AddButton(tools, "Size+", "Increase circle diameter by 2 pixels", () => ResizeCircles(2));
        AddButton(tools, "R−", "Rotate outlines and bar guides counterclockwise by 2 degrees", () => Rotate(-2));
        AddButton(tools, "R+", "Rotate outlines and bar guides clockwise by 2 degrees", () => Rotate(2));
        AddButton(tools, "Height−", "Flatter HUD outlines", () => ResizeHeight(-.05));
        AddButton(tools, "Height+", "Rounder HUD outlines", () => ResizeHeight(.05));
        lessMovement = AddButton(tools, "Search−", "Reduce movement search by 8 pixels", () => ResizeMargin(-8));
        moreMovement = AddButton(tools, "Search+", "Increase movement search by 8 pixels", () => ResizeMargin(8));
        test.IsCheckedChanged += (_, _) =>
        {
            canvas.ShowGuides = test.IsChecked != true;
            if (test.IsChecked == true) model.RequestReference();
            else model.StopCalibrationTest();
            canvas.InvalidateVisual();
        };
        tools.Children.Add(test);
        showSearch.IsCheckedChanged += (_, _) =>
        {
            canvas.ShowSearchArea = showSearch.IsChecked == true;
            canvas.InvalidateVisual();
        };
        tools.Children.Add(showSearch);
        var toolbar = new StackPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20))
        };
        toolbar.Children.Add(tools);
        toolbar.Children.Add(values);
        ToolsWindow = new Window
        {
            Title = "Mining calibration controls",
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            Topmost = true,
            CanResize = false,
            Width = 450,
            SizeToContent = SizeToContent.Height,
            Content = toolbar,
        };
        status.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        status.Margin = new Thickness(4, 0, 15, 4);
        status.Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20));
        status.Foreground = Brushes.White;
        status.IsHitTestVisible = false;
        panel.Children.Add(status);
        Content = panel;
        OverlayThemeResources.Apply(this);
        OverlayThemeResources.Apply(ToolsWindow);
        SetBounds(model.Settings.GetBounds(viewport), scaling);
        Opened += (_, _) =>
        {
            SetBounds(model.Settings.GetBounds(viewport), RenderScaling);
            model.IsCalibrating = true;
            ToolsWindow.Show(this);
            PositionTools();
        };
        ToolsWindow.SizeChanged += (_, _) => PositionTools();
        PositionChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        canvas.PointerPressed += OnPressed;
        model.PropertyChanged += OnDetectionChanged;
        Closed += (_, _) =>
        {
            ToolsWindow.Close();
            model.IsCalibrating = false;
            model.StopCalibrationTest();
            model.PropertyChanged -= OnDetectionChanged;
        };
        OnDetectionChanged(null, new(null));
    }

    private static Button AddButton(Panel panel, string text, string tip, Action action)
    {
        var button = new Button { Content = text, FontSize = 10, Padding = new Thickness(3), MinHeight = 20 };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }
    private void ResizeCircles(double delta) => model.UpdateCalibration(model.Settings with
    { CircleWidth = model.Settings.CircleWidth + delta / model.Settings.GetBounds(viewport).Width });
    private void Rotate(double delta) => model.UpdateCalibration(model.Settings with
    { RotationDegrees = model.Settings.RotationDegrees + delta });
    private void ResizeHeight(double delta) => model.UpdateCalibration(model.Settings with
    { CircleAspectRatio = model.Settings.CircleAspectRatio + delta });
    private void ResizeMargin(double delta)
    {
        showSearch.IsChecked = true;
        model.UpdateCalibration(model.Settings with
        { MotionMargin = model.Settings.MotionMargin + delta / model.Settings.GetBounds(viewport).Width });
    }

    private void SetBounds(PixelRect bounds, double scaling)
    {
        positioning = true;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        Position = bounds.Position;
        positioning = false;
        PositionTools();
    }
    private void PositionTools()
    {
        if (!ToolsWindow.IsVisible) return;
        var width = (int)Math.Ceiling(ToolsWindow.Bounds.Width * ToolsWindow.RenderScaling);
        var height = (int)Math.Ceiling(ToolsWindow.Bounds.Height * ToolsWindow.RenderScaling);
        var top = Position.Y - height - 6;
        if (top < viewport.Y) top = Position.Y + (int)Math.Ceiling(Height * RenderScaling) + 6;
        ToolsWindow.Position = new PixelPoint(
            Math.Clamp(Position.X, viewport.X, Math.Max(viewport.X, viewport.Right - width)),
            Math.Clamp(top, viewport.Y, Math.Max(viewport.Y, viewport.Bottom - height)));
    }
    private void SaveBounds()
    {
        if (positioning || !IsVisible || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        model.UpdateCalibration(model.Settings.WithBounds(new PixelRect(Position,
            new PixelSize((int)Math.Round(Bounds.Width * RenderScaling),
                (int)Math.Round(Bounds.Height * RenderScaling))), viewport));
        SetBounds(model.Settings.GetBounds(viewport), RenderScaling);
    }
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (point.X >= Bounds.Width - 18 && point.Y >= Bounds.Height - 18)
            BeginResizeDrag(WindowEdge.SouthEast, e);
        else ManagedOverlayWindowDragSession.Begin(this, e);
        e.Handled = true;
    }
    private void OnDetectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        test.IsChecked = model.IsCalibrationTesting;
        canvas.ShowGuides = !model.IsCalibrationTesting;
        status.Text = model.SlotsText;
        var settings = model.Settings;
        var frameWidth = settings.GetBounds(viewport).Width;
        values.Text = $"Size {settings.CircleWidth * frameWidth:F0} px · Height {settings.CircleAspectRatio:P0}"
            + $" · Rotation {settings.RotationDegrees:0}° · Search ±{settings.GetMovementAllowance(frameWidth):F0} px";
        lessMovement.IsEnabled = settings.MotionMargin > 0;
        moreMovement.IsEnabled = settings.MotionMargin < 120d / MiningDetectionSettings.GetWorkingWidth(settings.CircleWidth) - .000001;
        ToolTip.SetTip(canvas, $"Drag dots onto circle centres. Drag empty space to move; lower-right corner to resize.\n"
            + "Resize the frame to change the capture area. Size, Height and R change the HUD outlines. Search changes movement allowance.\n"
            + model.StatusText);
        canvas.InvalidateVisual();
    }

    private sealed class CalibrationCanvas(MiningDetectionViewModel model) : Control
    {
        private int dragged = -1;
        public bool ShowGuides { get; set; } = true;
        public bool ShowSearchArea { get; set; }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), new Rect(Bounds.Size));
            var pen = new Pen(Brushes.Gold, 1);
            context.DrawRectangle(null, pen, new Rect(Bounds.Size).Deflate(1));
            context.DrawLine(pen, new Point(Bounds.Width - 15, Bounds.Height - 2), new Point(Bounds.Width - 2, Bounds.Height - 15));
            if (!ShowGuides) return;
            var radius = Bounds.Width * model.Settings.CircleWidth / 2;
            var geometry = new MiningHudGeometry(model.Settings);
            if (ShowSearchArea)
            {
                var margin = model.Settings.GetMovementAllowance(Bounds.Width);
                var markers = model.Settings.Markers;
                var search = new Rect(new Point(markers.Min(p => p.X) * Bounds.Width - margin,
                        markers.Min(p => p.Y) * Bounds.Height - margin),
                    new Point(markers.Max(p => p.X) * Bounds.Width + margin,
                        markers.Max(p => p.Y) * Bounds.Height + margin));
                context.DrawRectangle(null, new Pen(Brushes.Gold, 1, DashStyle.Dash), search);
            }
            for (var i = 0; i < 6; i++)
            {
                var p = model.Settings.Markers[i];
                var center = new Point(p.X * Bounds.Width, p.Y * Bounds.Height);
                context.DrawEllipse(Brushes.Red, null, center, 3, 3);
                var outline = Enumerable.Range(0, 65).Select(n =>
                {
                    var angle = n * Math.PI / 32;
                    return center + geometry.RingPoint(angle, radius);
                }).ToArray();
                DrawPolyline(context, new Pen(Brushes.IndianRed, 1), outline);
                var text = new FormattedText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11, Brushes.White);
                context.DrawText(text, center + new Vector(6, -14));
                DrawPolyline(context, new Pen(Brushes.Cyan, 1.5), MiningBarShape.GuidePoints
                    .Select(p => center + geometry.Transform(p.X, p.Y, radius)).ToArray());
            }
        }
        private static void DrawPolyline(DrawingContext context, Pen pen, IReadOnlyList<Point> points)
        {
            for (var i = 1; i < points.Count; i++) context.DrawLine(pen, points[i - 1], points[i]);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!ShowGuides || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(this);
            for (var i = 0; i < 6; i++)
            {
                var p = model.Settings.Markers[i];
                var dx = point.X - p.X * Bounds.Width;
                var dy = point.Y - p.Y * Bounds.Height;
                if (dx * dx + dy * dy > 144) continue;
                dragged = i;
                e.Pointer.Capture(this);
                e.Handled = true;
                break;
            }
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (dragged < 0) return;
            var point = e.GetPosition(this);
            var markers = model.Settings.Markers.ToArray();
            markers[dragged] = new(point.X / Bounds.Width, point.Y / Bounds.Height);
            model.UpdateCalibration(model.Settings with { Markers = markers });
            e.Handled = true;
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (dragged < 0) return;
            dragged = -1;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            dragged = -1;
        }
    }
}
