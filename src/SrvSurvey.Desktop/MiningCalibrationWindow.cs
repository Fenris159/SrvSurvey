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
    private bool positioning;

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
        var tools = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20))
        };
        tools.Children.Add(new TextBlock { Text = "RIG CALIBRATION", FontSize = 10, Margin = new Thickness(3) });
        AddButton(tools, "−", "Smaller HUD circles", () => ResizeCircles(-.01));
        AddButton(tools, "+", "Larger HUD circles", () => ResizeCircles(.01));
        AddButton(tools, "M−", "Less movement allowance", () => ResizeMargin(-.02));
        AddButton(tools, "M+", "More movement allowance", () => ResizeMargin(.02));
        test.IsCheckedChanged += (_, _) =>
        {
            canvas.ShowGuides = test.IsChecked != true;
            if (test.IsChecked == true) model.RequestReference();
            else model.StopCalibrationTest();
            canvas.InvalidateVisual();
        };
        tools.Children.Add(test);
        panel.Children.Add(tools);
        status.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        status.Margin = new Thickness(4, 0, 15, 4);
        status.Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20));
        status.Foreground = Brushes.White;
        status.IsHitTestVisible = false;
        panel.Children.Add(status);
        Content = panel;
        OverlayThemeResources.Apply(this);
        SetBounds(model.Settings.GetBounds(viewport), scaling);
        Opened += (_, _) => { SetBounds(model.Settings.GetBounds(viewport), RenderScaling); model.IsCalibrating = true; };
        PositionChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        canvas.PointerPressed += OnPressed;
        model.PropertyChanged += OnDetectionChanged;
        Closed += (_, _) =>
        {
            model.IsCalibrating = false;
            model.StopCalibrationTest();
            model.PropertyChanged -= OnDetectionChanged;
        };
        OnDetectionChanged(null, new(null));
    }

    private static void AddButton(Panel panel, string text, string tip, Action action)
    {
        var button = new Button { Content = text, FontSize = 10, Padding = new Thickness(3), MinHeight = 20 };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }
    private void ResizeCircles(double delta) => model.UpdateCalibration(model.Settings with
    { CircleWidth = model.Settings.CircleWidth + delta });
    private void ResizeMargin(double delta) => model.UpdateCalibration(model.Settings with
    { MotionMargin = model.Settings.MotionMargin + delta });

    private void SetBounds(PixelRect bounds, double scaling)
    {
        positioning = true;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        Position = bounds.Position;
        positioning = false;
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
        ToolTip.SetTip(canvas, $"Drag dots onto circle centres. Drag empty space to move; lower-right corner to resize.\n"
            + $"Circle width: {model.Settings.CircleWidth:P0}; movement: {model.Settings.MotionMargin:P0}.\n"
            + model.StatusText);
        canvas.InvalidateVisual();
    }

    private sealed class CalibrationCanvas(MiningDetectionViewModel model) : Control
    {
        private int dragged = -1;
        public bool ShowGuides { get; set; } = true;
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), new Rect(Bounds.Size));
            var pen = new Pen(Brushes.Gold, 1);
            context.DrawRectangle(null, pen, new Rect(Bounds.Size).Deflate(1));
            context.DrawLine(pen, new Point(Bounds.Width - 15, Bounds.Height - 2), new Point(Bounds.Width - 2, Bounds.Height - 15));
            if (!ShowGuides) return;
            var radius = Bounds.Width * model.Settings.CircleWidth / 2;
            for (var i = 0; i < 6; i++)
            {
                var p = model.Settings.Markers[i];
                var center = new Point(p.X * Bounds.Width, p.Y * Bounds.Height);
                context.DrawEllipse(Brushes.Red, null, center, 3, 3);
                context.DrawEllipse(null, new Pen(Brushes.IndianRed, 1), center, radius, radius * .65);
                context.DrawRectangle(null, new Pen(Brushes.Cyan, 1),
                    new Rect(center.X - radius * 28 / 22, center.Y - radius * 24 / 22,
                        radius * 16 / 22, radius * 14 / 22));
                var text = new FormattedText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11, Brushes.White);
                context.DrawText(text, center + new Vector(-radius, -radius * .65 - 15));
                context.DrawLine(new Pen(Brushes.Cyan, 2),
                    center + new Vector(-radius * .6, radius * .65 + 5),
                    center + new Vector(radius * .6, radius * .65 + 5));
            }
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
