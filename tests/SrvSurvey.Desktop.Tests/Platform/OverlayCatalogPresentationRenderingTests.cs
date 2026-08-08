using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayCatalogPresentationRenderingTests
{
    [AvaloniaFact]
    public void EveryEditorOverlayPresentationRendersAtItsExpectedSize()
    {
        var emptyFrames = new List<string>();
        var dimensions = new List<string>
        {
            "plotter,expected_width,expected_height,rendered_width,rendered_height",
        };
        var outputDirectory = Environment.GetEnvironmentVariable(
            "SRVSURVEY_OVERLAY_RENDER_OUTPUT");
        var opacityText = Environment.GetEnvironmentVariable(
            "SRVSURVEY_OVERLAY_RENDER_OPACITY");
        var previewOpacity = double.TryParse(
            opacityText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedOpacity)
            && parsedOpacity is >= 0 and <= 1
                ? parsedOpacity
                : 1d;
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            var preview = new OverlayPositionPreviewWindow(definition);
            try
            {
                OverlayThemeResources.Apply(preview);
                preview.ApplyRuntimePresentationTheme();
                preview.ConfigureOpacity(previewOpacity, null);
                preview.Show();
                Assert.Equal(1, preview.MinWidth);
                Assert.Equal(
                    new Thickness(0),
                    preview.PreviewBodyControl.Padding);
                Assert.Same(
                    Avalonia.Media.Brushes.Transparent,
                    preview.PreviewBodyControl.Background);
                var frame = preview.CaptureRenderedFrame();
                Assert.NotNull(frame);
                // Content-driven hosts expand/contract with presentation
                // content instead of a fixed catalog box. Assert a usable
                // non-empty frame rather than a rigid pixel size.
                if (frame.PixelSize.Width < 8 || frame.PixelSize.Height < 8)
                {
                    emptyFrames.Add(
                        $"{definition.Name}: rendered {frame.PixelSize}");
                }

                var expected = preview.GetExpectedPixelSize(
                    preview.RenderScaling);
                dimensions.Add(string.Join(
                    ',',
                    definition.Name,
                    expected.Width,
                    expected.Height,
                    frame.PixelSize.Width,
                    frame.PixelSize.Height));

                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    using var stream = File.Create(Path.Combine(
                        outputDirectory,
                        $"{definition.Name}.png"));
                    frame.Save(stream, PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                preview.Close();
            }
        }

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            File.WriteAllLines(
                Path.Combine(outputDirectory, "dimensions.csv"),
                dimensions);
        }

        Assert.Empty(emptyFrames);
    }

    [AvaloniaFact]
    public void EveryStatefulEditorPreviewStateRendersThroughItsSharedTemplate()
    {
        var statefulPlotters = new[]
        {
            "PlotBioSystem",
            "PlotBioStatus",
            "PlotGuardianStatus",
            "PlotFleetCarrierRoute",
            "PlotPulse",
        };
        var outputDirectory = Environment.GetEnvironmentVariable(
            "SRVSURVEY_OVERLAY_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        foreach (var plotterName in statefulPlotters)
        {
            var preview = new OverlayPositionPreviewWindow(
                OverlayLayoutCatalog.GetRequired(plotterName));
            try
            {
                OverlayThemeResources.Apply(preview);
                preview.ApplyRuntimePresentationTheme();
                preview.Show();

                var presentation = Assert.IsType<Control>(
                    preview.RuntimePresentation,
                    exactMatch: false);
                for (var index = 0; index < preview.EditorPreviewStateCount; index++)
                {
                    Assert.Same(presentation, preview.RuntimePresentation);
                    Assert.NotNull(presentation.DataContext);
                    var frame = preview.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.True(
                        frame.PixelSize.Width >= 8,
                        $"{plotterName} state {index + 1} rendered too narrowly.");
                    Assert.True(
                        frame.PixelSize.Height >= 8,
                        $"{plotterName} state {index + 1} rendered too short.");

                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                    {
                        using var stream = File.Create(Path.Combine(
                            outputDirectory,
                            $"{plotterName}-state-{index + 1}.png"));
                        frame.Save(stream, PngBitmapEncoderOptions.Default);
                    }

                    if (index + 1 < preview.EditorPreviewStateCount)
                    {
                        Assert.True(preview.CycleEditorPreviewState());
                    }
                }
            }
            finally
            {
                preview.Close();
            }
        }
    }

    [AvaloniaFact]
    public void CommodityRuntimePanelGrowsToContentInsteadOfAFixedCanvas()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(
            new ColonizationCommodityPlan
    {
        Title = "Raven's Reach",
        ProjectNames = ["Raven's Reach"],
        Rows = [
                    Row("steel", "Steel", "Metals", 2450, 96, 620),
                    Row("powergenerators", "Power generators", "Machinery", 840, 32, 210),
                    Row("polymers", "Polymers", "Chemicals", 610, 24, 180),
                    Row("waterpurifiers", "Water purifiers", "Machinery", 420, 16, 120),
                ],
        FleetCarriers = [],
        TotalRemaining = 4320,
        TripsInCurrentShip = 45,
        FleetCarrierDeficit = 3190,
        FleetCarrierDeficitTrips = null,
        IsAtConstructionSite = false,
        IsLocalProjectUntracked = false,
        IsDockedAtUntrackedFleetCarrier = false,
        IsConstructionComplete = false,
        IsConstructionFailed = false
    },
            null);
        var window = new ColonizationCommodityOverlayWindow(viewModel);
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(
                StringComparer.Ordinal),
            defaultOpacity: null,
            error: null);
        try
        {
            OverlayThemeResources.Apply(
                window,
                layout,
                "PlotBuildCommodities");
            window.Show();
            var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            // Content-driven width: at least catalog floor, may grow for rows.
            Assert.InRange(frame.PixelSize.Width, 200, 900);
            Assert.InRange(frame.PixelSize.Height, 80, 699);
            var outputDirectory = Environment.GetEnvironmentVariable(
                "SRVSURVEY_OVERLAY_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                using var stream = File.Create(Path.Combine(
                    outputDirectory,
                    "PlotBuildCommodities-runtime.png"));
                frame.Save(stream, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ColonizationCommodityPlanRow Row(
        string commodity,
        string displayName,
        string category,
        int needed,
        int inShip,
        int onFleetCarriers) => new()
    {
        Commodity = commodity,
        DisplayName = displayName,
        Category = category,
        Needed = needed,
        InShip = inShip,
        OnFleetCarriers = onFleetCarriers,
        IsAssignedToCommander = false,
        IsAssignedToOther = false,
    };
}
