using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CarrierReadinessPresentationTests
{
    private static readonly string[] CarrierNames =
    [
        "Asteria Logistics",
        "Northstar",
        "Pioneer",
        "Wayfarer",
        "Odyssey",
        "Vanguard",
        "Atlas",
        "Endeavour",
        "Horizon",
        "Aurora",
        "Meridian",
        "Deep Space Survey and Construction Auxiliary",
    ];
    private static readonly string[] BuildIds = ["first", "second"];

    /// <summary>
    /// Renders shared carrier colors, exact bar proportions, wrapping, and theme changes in every report mode.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("normal", 1180, "monochrome-dark")]
    [InlineData("normal", 1180, "blue-dark")]
    [InlineData("normal", 700, "blue-light")]
    [InlineData("combined", 1180, "blue-light")]
    [InlineData("nexus", 700, "blue-dark")]
    public async Task RealPreviewSharesColorsWithAWrappingLegendAndSupportsMoreThanTenCarriers(
        string mode,
        int width,
        string themeKey
    )
    {
        ColonizationProjectPreviewData data = Data();
        var reader = new ColonizationProjectPreviewViewModelTests.Reader(
            (id, _) =>
                Task.FromResult<ColonizationProjectPreviewData?>(
                    data with
                    {
                        Project = data.Project with { BuildId = id },
                    }
                )
        );
        using ColonizationProjectPreviewViewModel model = mode switch
        {
            "nexus" => new(_ => Task.FromResult<ColonizationNexus?>(Nexus(data)), null, () => true, () => 128),
            "combined" => new(reader, () => BuildIds),
            _ => new(reader, "build"),
        };
        RavenThemeDefinition theme = RavenThemeCatalog.Get(themeKey);
        var window = new ColonizationProjectPreviewWindow(model)
        {
            Width = width,
            Height = 920,
            RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light,
            Background = new SolidColorBrush(Color.Parse(theme.WindowColor)),
        };
        window.Resources["RavenSidebarBrush"] = new SolidColorBrush(Color.Parse(theme.SidebarColor));
        window.Resources["RavenSurfaceBrush"] = new SolidColorBrush(Color.Parse(theme.SurfaceColor));
        window.Resources["RavenRaisedSurfaceBrush"] = new SolidColorBrush(Color.Parse(theme.RaisedSurfaceColor));
        window.Resources["RavenBorderBrush"] = new SolidColorBrush(Color.Parse(theme.BorderColor));
        window.Resources["RavenTextBrush"] = new SolidColorBrush(Color.Parse(theme.TextColor));
        window.Resources["RavenMutedTextBrush"] = new SolidColorBrush(Color.Parse(theme.MutedTextColor));
        window.Resources["RavenControlAccentBrush"] = new SolidColorBrush(Color.Parse(theme.ControlAccentColor));
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(12, model.ReadyCarriers.Count);
            Assert.Equal(model.Snapshot!.ReadyOnCarriers, model.ReadyCarriers.Sum(carrier => carrier.Ready));
            CarrierReadinessBar bar = window.FindControl<CarrierReadinessBar>("CarrierReadyProgress")!;
            ItemsControl legend = window.FindControl<ItemsControl>("CarrierReadyLegend")!;
            Assert.Equal(12, bar.Children.Count);
            Assert.Equal(12, legend.Items.Count);
            CarrierColorSwatch[] swatches = legend.GetVisualDescendants().OfType<CarrierColorSwatch>().ToArray();
            Assert.Equal(12, swatches.Length);
            foreach (CarrierColorSwatch segment in bar.Children.OfType<CarrierColorSwatch>())
            {
                CarrierColorSwatch swatch = swatches.Single(item => item.MarketId == segment.MarketId);
                Color color = Assert.IsType<ISolidColorBrush>(segment.Background, exactMatch: false).Color;
                Assert.Equal(color, Assert.IsType<ISolidColorBrush>(swatch.Background, exactMatch: false).Color);
                Assert.Equal(CarrierColorPalette.GetColor(segment.MarketId, theme.IsDark), color);
            }
            Assert.InRange(
                bar.Children.Sum(child => child.Bounds.Width) / bar.Bounds.Width * 100,
                model.ReadyProgress - 0.5,
                model.ReadyProgress + 0.5
            );
            Assert.True(legend.Bounds.Height > 30);
            Assert.All(
                legend.GetVisualDescendants().OfType<WrapPanel>().Single().Children,
                child => Assert.True(child.Bounds.Right <= legend.Bounds.Width + 1)
            );
            string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_BUILD_PREVIEW_RENDER_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                frame.Save(
                    Path.Combine(directory, $"carrier-readiness-{mode}-{width}-{themeKey}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            await model.RefreshAsync();
            Assert.Equal(12, model.ReadyCarriers.Count);
            window.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Light : ThemeVariant.Dark;
            using WriteableBitmap? changed = window.CaptureRenderedFrame();
            Assert.NotNull(changed);
            Assert.All(
                bar.Children.OfType<CarrierColorSwatch>(),
                segment =>
                    Assert.Equal(
                        CarrierColorPalette.GetColor(segment.MarketId, !theme.IsDark),
                        Assert.IsType<ISolidColorBrush>(segment.Background, exactMatch: false).Color
                    )
            );
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Guards the bar against absent shares, invalid requirements, and excessive claimed coverage.</summary>
    [AvaloniaFact]
    public void EmptyUnknownOrOversizedDataNeverOverflowsTheBar()
    {
        var bar = new CarrierReadinessBar { Width = 400, Height = 16 };
        bar.Maximum = 100;
        bar.Carriers = [new(1, "First", 80), new(2, "Second", 80), new(3, "Unknown", null), new(4, "Empty", 0)];
        var window = new Window
        {
            Width = 500,
            Height = 100,
            Content = bar,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Equal(2, bar.Children.Count);
            Assert.Equal(400, bar.Children.Sum(child => child.Bounds.Width), 1);
            bar.Maximum = 0;
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Empty(bar.Children);
            bar.Carriers = null;
            Assert.Null(bar.Carriers);
            Assert.Single(bar.ColumnDefinitions);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Creates twelve carriers with varied stock and a long label to exercise real legend wrapping.</summary>
    private static ColonizationProjectPreviewData Data()
    {
        ColonizationProjectPreviewData original = ColonizationProjectPreviewViewModelTests.Data();
        return original with
        {
            Project = original.Project with
            {
                MaximumRequired = 6000,
                Commodities = new() { ["steel"] = 4000 },
                LinkedFleetCarriers = CarrierNames
                    .Select(
                        (name, index) =>
                            new ColonizationProjectFleetCarrier
                            {
                                MarketId = index + 1,
                                DisplayName = name,
                                Name = $"SRV-{index + 1:000}",
                            }
                    )
                    .ToList(),
            },
            CarrierCargo = CarrierNames
                .Select((_, index) => index + 1)
                .ToDictionary(
                    id => id.ToString(CultureInfo.InvariantCulture),
                    id => new Dictionary<string, int> { ["steel"] = 100 + id * 20 }
                ),
        };
    }

    /// <summary>Wraps the same stock fixture in a Nexus so report modes can be compared consistently.</summary>
    private static ColonizationNexus Nexus(ColonizationProjectPreviewData data) =>
        new()
        {
            Name = "Construction Fleet",
            Systems =
            [
                new()
                {
                    Name = "Example system",
                    Total = 6000,
                    Progress = 2000,
                    Needs = new() { ["steel"] = 4000 },
                },
            ],
            FleetCarriers = data
                .Project.LinkedFleetCarriers.Select(carrier => new ColonizationFleetCarrier
                {
                    MarketId = carrier.MarketId,
                    Name = carrier.Name,
                    DisplayName = carrier.DisplayName,
                    Cargo = data.CarrierCargo![carrier.MarketId.ToString(CultureInfo.InvariantCulture)],
                })
                .ToArray(),
        };
}
