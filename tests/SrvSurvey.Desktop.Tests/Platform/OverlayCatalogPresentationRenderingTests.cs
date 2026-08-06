using Avalonia;
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
        var mismatches = new List<string>();
        var dimensions = new List<string>
        {
            "plotter,expected_width,expected_height,rendered_width,rendered_height",
        };
        var outputDirectory = Environment.GetEnvironmentVariable(
            "SRVSURVEY_OVERLAY_RENDER_OUTPUT");
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
                var expected = preview.GetExpectedPixelSize(
                    preview.RenderScaling);
                preview.Show();
                var frame = preview.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (expected != frame.PixelSize)
                {
                    mismatches.Add(
                        $"{definition.Name}: expected {expected}, rendered {frame.PixelSize}");
                }
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

        Assert.Empty(mismatches);
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
            Assert.Equal(440, frame.PixelSize.Width);
            Assert.InRange(frame.PixelSize.Height, 160, 699);
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
