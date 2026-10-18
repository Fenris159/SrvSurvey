using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BiologyOverlayRefreshStabilityTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-stability-{Guid.NewGuid():N}"
    );

    /// <summary>Frequent flight status preserves realized biology rows and geometry, while new signal data still appears.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void OverlayRefreshKeepsBiologyRowsAndWindowSteady(double scaling)
    {
        var survey = new SystemSurveyViewModel(new SystemSurveySettingsStore(Path.Combine(directory, "ui.json")));
        var status = new EliteStatus { Flags = StatusFlags.InMainShip, GuiFocus = GuiFocus.SystemMap };
        survey.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(
                    """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","Landable":true,"MassEM":1,"DistanceFromArrivalLS":100}"""
                ),
                Signals(3),
            ],
            status
        );
        var model = new SystemSurveyOverlayViewModel(
            survey,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows)
        );
        var window = new BiologySurveyOverlayWindow(model);
        try
        {
            window.SetRenderScaling(scaling);
            OverlayThemeResources.Apply(
                window,
                LegacyOverlayLayout.Empty,
                "PlotBioSystem",
                new OverlayWindowRegistry()
            );
            window.Show();
            Settle(window);
            Size initialSize = window.ClientSize;
            string bodyName = Assert.Single(survey.BiologySurveyDisplay.Bodies).Name;
            Control[] initialRows = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Text == bodyName && text.IsEffectivelyVisible)
                .Cast<Control>()
                .ToArray();
            Assert.NotEmpty(initialRows);
            var sizes = new List<Size>();
            window.SizeChanged += (_, _) => sizes.Add(window.ClientSize);
            for (int index = 0; index < 12; index++)
            {
                survey.ApplyUpdate([], status with { Heading = index * 10 });
                Settle(window);
                Assert.All(initialRows, row => Assert.Same(window, TopLevel.GetTopLevel(row)));
                Assert.Equal(initialSize, window.ClientSize);
            }
            Assert.All(sizes, size => Assert.Equal(initialSize, size));
            survey.ApplyUpdate([Signals(4)], status);
            Settle(window);
            Assert.Contains(
                window.GetVisualDescendants().OfType<ItemsControl>(),
                control =>
                    control.ItemsSource is IReadOnlyList<BiologyBodyRowViewModel> bodies
                    && bodies.Count == 1
                    && bodies[0].SignalCount == 4
            );
        }
        finally
        {
            window.Close();
        }
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static JournalEventEnvelope Signals(int count) =>
        Parse(
            $$"""{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":{{count}}}]}"""
        );

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? result, out string? error), error);
        return Assert.IsType<JournalEventEnvelope>(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
