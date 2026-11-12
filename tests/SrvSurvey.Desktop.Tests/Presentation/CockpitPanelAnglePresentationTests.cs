using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CockpitPanelAnglePresentationTests
{
    [AvaloniaTheory]
    [InlineData("monochrome-dark")]
    [InlineData("blue-light")]
    [InlineData("orange-dark")]
    public void OrbAndNumericFieldsStayAlignedAndEditTheSamePanelAcrossThemes(string theme)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cockpit-preview-{Guid.NewGuid():N}");
        Application application = Assert.IsType<Application>(Application.Current, exactMatch: false);
        var themes = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
        );
        Color originalFill = Assert
            .IsType<ISolidColorBrush>(application.Resources["RavenWindowBrush"], exactMatch: false)
            .Color;
        string previousTheme = RavenThemeCatalog
            .All.Single(palette => Color.Parse(palette.WindowColor) == originalFill)
            .Key;
        var model = new CockpitOverlayViewModel(
            new CockpitOverlaySettingsStore(Path.Combine(directory, "settings.json"))
        );
        model.BeginEdit();
        model.SelectPanel("PlotJumpInfo");
        model.Yaw = -25;
        model.Pitch = 15;
        model.Roll = -5;
        model.Depth = 1.2;
        var view = new CockpitPanelAngleView { DataContext = model };
        var window = new Window
        {
            Width = 590,
            Height = 310,
            Content = new Border { Padding = new Thickness(20), Child = view },
            Classes = { "app-window" },
        };
        try
        {
            themes.Select(theme == "blue-dark" ? "monochrome-dark" : "blue-dark");
            themes.Select(theme);
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            PanelRotationOrb orb = Assert.Single(view.GetVisualDescendants().OfType<PanelRotationOrb>());
            NumericUpDown[] fields = view.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Assert.Equal(4, fields.Length);
            Assert.Equal(-25m, fields[0].Value);
            Assert.Equal(15m, fields[1].Value);
            Assert.Equal(-5m, fields[2].Value);
            Assert.Equal(1.2m, fields[3].Value);
            string[] names = ["Yaw, degrees", "Pitch, degrees", "Roll, degrees", "Panel distance"];
            for (int index = 0; index < fields.Length; index++)
            {
                AutomationPeer peer = Assert.IsType<NumericUpDownAutomationPeer>(
                    ControlAutomationPeer.CreatePeerForElement(fields[index])
                );
                Assert.Equal(names[index], peer.GetName());
            }
            Assert.Equal(
                "Panel distance",
                ControlAutomationPeer
                    .CreatePeerForElement(Assert.Single(view.GetVisualDescendants().OfType<Slider>()))
                    .GetName()
            );
            foreach (NumericUpDown field in fields)
            {
                Point position = field.TranslatePoint(default, view)!.Value;
                Assert.InRange(position.X, 0, view.Bounds.Width - field.Bounds.Width + 0.1);
            }
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_COCKPIT_PREVIEW_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"cockpit-panel-angle-{theme}.png"), PngBitmapEncoderOptions.Default);
            }

            Point center = orb.TranslatePoint(new Point(65, 65), window)!.Value;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(center + new Vector(20, -10), RawInputModifiers.LeftMouseButton);
            window.MouseUp(center + new Vector(20, -10), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(-13, model.Yaw, 3);
            Assert.Equal(21, model.Pitch, 3);
            Assert.Equal(-5, model.Roll);

            window.MouseDown(center + new Vector(50, 0), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(center + new Vector(0, 50), RawInputModifiers.LeftMouseButton);
            window.MouseUp(center + new Vector(0, 50), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(-95, model.Roll, 3);
            Assert.True(orb.Focus());
            window.KeyPress(
                Key.Right,
                RawInputModifiers.Control | RawInputModifiers.Shift,
                PhysicalKey.ArrowRight,
                null
            );
            Assert.Equal(-100, model.Roll);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(22, model.Pitch);
        }
        finally
        {
            window.Close();
            themes.Select(previousTheme);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
