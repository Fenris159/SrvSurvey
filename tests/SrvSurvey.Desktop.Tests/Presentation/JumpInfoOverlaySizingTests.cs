using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class JumpInfoOverlaySizingTests
{
    [AvaloniaFact]
    public void LongLookupWarningCannotExpandRuntimeOrPreviewWidth()
    {
        var window = new JumpInfoOverlayWindow();
        var model = Assert.IsType<JumpInfoOverlayViewModel>(window.DataContext);
        model.JumpInfo.InstallEditorPreview(
            new JumpInfoRoutePlan(new JumpTarget("Beta", 3), JumpInfoRouteSource.Direct, 0, [], null),
            new SystemSummary("Beta", 3, null, null, null, 0, 0, null, null, null,
                null, new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0), []), [],
            dataStatusText: string.Concat(
            Enumerable.Repeat("EDSM data is unavailable: a very long network error. ", 20)));
        var preview = new JumpInfoOverlayPresentation { DataContext = model };
        preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(600, preview.DesiredSize.Width);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(600, window.Bounds.Width);
            OverlayThemeResources.ApplyScale(window, 13, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1200, window.Bounds.Width);
        }
        finally
        {
            window.Close();
            model.Dispose();
            model.JumpInfo.Dispose();
        }
    }
}
