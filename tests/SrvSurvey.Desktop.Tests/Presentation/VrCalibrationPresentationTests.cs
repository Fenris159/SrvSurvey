using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class VrCalibrationPresentationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "srvsurvey-vr-ui-" + Guid.NewGuid().ToString("N")
    );

    [AvaloniaTheory]
    [InlineData(420)]
    [InlineData(760)]
    public void AllSevenNumberFieldsKeepReadableTextBoxesAtNarrowWidths(int width)
    {
        var view = new VrCalibrationView { DataContext = CreateViewModel() };
        var window = new Window
        {
            Width = width,
            Height = 1000,
            Content = view,
        };
        try
        {
            window.Show();
            window.CaptureRenderedFrame()?.Dispose();
            VrCalibrationNumberInput[] fields = view.GetVisualDescendants()
                .OfType<VrCalibrationNumberInput>()
                .ToArray();
            Assert.Equal(7, fields.Length);
            foreach (VrCalibrationNumberInput field in fields)
            {
                TextBox text = Assert.Single(field.GetVisualDescendants().OfType<TextBox>());
                Assert.InRange(text.Bounds.Width, 60, 132);
                Assert.False(string.IsNullOrWhiteSpace(text.Text));
                Rect bounds = field.Bounds.WithX(field.TranslatePoint(default, view)!.Value.X);
                Assert.InRange(bounds.Right, 1, width);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.None, 1.5)]
    [InlineData(RawInputModifiers.Shift, 1.05)]
    [InlineData(RawInputModifiers.Control, 3.5)]
    public void WheelAdjustsPositionWithNormalFineAndCoarseSteps(RawInputModifiers modifiers, double expected)
    {
        VrOverlayViewModel model = CreateViewModel();
        var view = new VrCalibrationView { DataContext = model };
        var window = new Window
        {
            Width = 760,
            Height = 1000,
            Content = view,
        };
        try
        {
            window.Show();
            window.CaptureRenderedFrame()?.Dispose();
            VrCalibrationNumberInput field = view.FindControl<VrCalibrationNumberInput>("PositionXInput")!;
            Point point = field.TranslatePoint(new Point(20, 15), window)!.Value;
            window.MouseWheel(point, new Vector(0, 1), modifiers);
            Assert.Equal(expected, model.PositionX, 4);
            window.MouseWheel(point, new Vector(0, -1), modifiers);
            Assert.Equal(1, model.PositionX, 4);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ControllerWheelChangesTheSharedModelUsingSelectedPrecision()
    {
        VrOverlayViewModel model = CreateViewModel();
        using var session = new VrOverlayCalibrationSession();
        IReadOnlyList<RegisteredOverlayWindow> registrations = session.Refresh(model, null);
        RegisteredOverlayWindow controls = registrations.Single(item => item.PlotterName == "PlotAdjustVR");
        controls.Window.CaptureRenderedFrame()?.Dispose();
        VrCalibrationNumberInput field = controls
            .Window.GetVisualDescendants()
            .OfType<VrCalibrationNumberInput>()
            .Single(input => input.Name == "YawInput");
        Point point = field.TranslatePoint(new Point(20, 15), controls.RenderSource)!.Value;
        using var router = new VrOverlayInputRouter();
        model.AdjustmentStepIndex = 2;
        router.Dispatch(
            controls,
            new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.Wheel, point.X, point.Y, WheelY: 1)
        );
        Assert.Equal(10, model.RotationYaw);
        model.AdjustmentStepIndex = 0;
        router.Dispatch(
            controls,
            new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.Wheel, point.X, point.Y, WheelY: -1)
        );
        Assert.Equal(9.9, model.RotationYaw, 4);

        RegisteredOverlayWindow sample = registrations.Single(item => item.PlotterName == "PlotJumpInfo");
        Assert.True(session.Owns(sample));
        Assert.False(session.Owns(new RegisteredOverlayWindow(new Window(), "Other")));
        Assert.Equal(controls, session.Refresh(model, null).Single(item => item.PlotterName == "PlotAdjustVR"));
        model.CancelCommand.Execute(null);
        Assert.Empty(session.Refresh(model, null));
        Assert.False(controls.Window.IsVisible);
        Assert.False(sample.Window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("blue-light")]
    [InlineData("green-light")]
    [InlineData("orange-dark")]
    public void HeadsetCalibrationUsesReadableMatchingApplicationThemeColors(string palette)
    {
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
        );
        string previous = theme.Current.Key;
        using var session = new VrOverlayCalibrationSession();
        try
        {
            theme.Select(palette);
            RegisteredOverlayWindow controls = session
                .Refresh(CreateViewModel(), null)
                .Single(item => item.PlotterName == "PlotAdjustVR");
            controls.Window.CaptureRenderedFrame()?.Dispose();
            Border background = Assert.IsType<Border>(controls.RenderSource);
            TextBlock title = controls
                .Window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == "VR overlay calibration");
            Assert.Equal(
                Color.Parse(RavenThemeCatalog.Get(palette).WindowColor),
                Assert.IsType<ISolidColorBrush>(background.Background, exactMatch: false).Color
            );
            Assert.Equal(
                Color.Parse(RavenThemeCatalog.Get(palette).TextColor),
                Assert.IsType<ISolidColorBrush>(title.Foreground, exactMatch: false).Color
            );
        }
        finally
        {
            theme.Select(previous);
        }
    }

    private VrOverlayViewModel CreateViewModel()
    {
        Directory.CreateDirectory(directory);
        string factory = Path.Combine(directory, "factory.json");
        File.WriteAllText(factory, "{\"PlotJumpInfo\":\"center:0, top:0 { s: 8, p: <1, 2, 3>, r: <4, 5, 6>}\"}");
        var model = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(directory, "settings.json")),
            new VrOverlayCalibrationStore(Path.Combine(directory, "data"), factory)
        );
        Assert.True(model.BeginAdjustment());
        return model;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
