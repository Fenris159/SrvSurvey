using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class MainWindowPlacementTests
{
    private static readonly MainWindowMonitor Primary = new(
        "DISPLAY1",
        "DISPLAY1 (Primary)",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1040),
        1,
        true
    );

    private static readonly MainWindowMonitor Secondary = new(
        "DISPLAY2",
        "DISPLAY2",
        new PixelRect(-2560, 120, 2560, 1440),
        new PixelRect(-2560, 120, 2560, 1400),
        1,
        false
    );

    [Fact]
    public void PreferredMonitorCentersScaledWindowInItsWorkingArea()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve([Primary, Secondary], "DISPLAY2", 125);

        Assert.Same(Secondary, result.Monitor);
        Assert.True(result.UsedPreferredMonitor);
        Assert.Equal(1475, result.Width);
        Assert.Equal(950, result.Height);
        Assert.Equal(new PixelPoint(-2018, 345), result.Position);
        Assert.Equal(1.25, result.ApplicationScale);
    }

    [Fact]
    public void MissingPreferredMonitorFallsBackToPrimaryAndStaysVisible()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve([Secondary, Primary], "DISPLAY3", 100);

        Assert.Same(Primary, result.Monitor);
        Assert.False(result.UsedPreferredMonitor);
        Assert.Equal(new PixelPoint(370, 140), result.Position);
    }

    [Fact]
    public void LastConnectedPositionIsRestoredBeforeDefaultMonitor()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary, Secondary],
            preferredMonitorId: "DISPLAY1",
            applicationScalePercent: 100,
            lastPosition: new ApplicationWindowPosition(-2200, 200, "DISPLAY2")
        );

        Assert.Same(Secondary, result.Monitor);
        Assert.Equal(new PixelPoint(-2200, 200), result.Position);
        Assert.False(result.UsedPreferredMonitor);
    }

    [Fact]
    public void RestoredPositionIsClampedAfterWorkingAreaShrinks()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary],
            preferredMonitorId: null,
            applicationScalePercent: 100,
            lastPosition: new ApplicationWindowPosition(1800, 1000, "DISPLAY1")
        );

        Assert.Equal(new PixelPoint(740, 280), result.Position);
    }

    [Fact]
    public void DisconnectedLastMonitorUsesConfiguredDefaultMonitor()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary],
            preferredMonitorId: "DISPLAY1",
            applicationScalePercent: 100,
            lastPosition: new ApplicationWindowPosition(-2200, 200, "DISPLAY2")
        );

        Assert.Same(Primary, result.Monitor);
        Assert.Equal(new PixelPoint(370, 140), result.Position);
        Assert.True(result.UsedPreferredMonitor);
    }

    [Fact]
    public void DisconnectedLastMonitorWithAutomaticPreferenceCentersOnPrimary()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary],
            preferredMonitorId: null,
            applicationScalePercent: 100,
            lastPosition: new ApplicationWindowPosition(-2200, 200, "DISPLAY2")
        );

        Assert.Same(Primary, result.Monitor);
        Assert.Equal(new PixelPoint(370, 140), result.Position);
    }

    [Fact]
    public void AutomaticMonitorDoesNotOverrideOperatingSystemPosition()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary, Secondary],
            preferredMonitorId: null,
            applicationScalePercent: 110,
            automaticMonitorId: "DISPLAY2"
        );

        Assert.Same(Secondary, result.Monitor);
        Assert.Null(result.Position);
        Assert.Equal(1.1, result.ApplicationScale, precision: 10);
    }

    [Fact]
    public void OversizedScaleIsReducedToFitWorkingArea()
    {
        MainWindowMonitor smallMonitor = Primary with
        {
            Bounds = new PixelRect(0, 0, 1280, 720),
            WorkingArea = new PixelRect(0, 0, 1280, 680),
        };

        MainWindowPlacementResult result = MainWindowPlacement.Resolve([smallMonitor], "DISPLAY1", 150);

        Assert.Equal(632.0 / 760.0, result.ApplicationScale, precision: 10);
        Assert.Equal(new PixelPoint(149, 24), result.Position);
        Assert.True(result.Width < MainWindowPlacement.DefaultWidth);
        Assert.True(result.Height < MainWindowPlacement.DefaultHeight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidSavedGeometryRestoresSizeAndClampsUsingItsActualDimensions(bool maximized)
    {
        MainWindowPlacementResult result = ResolveGeometry(
            new ApplicationWindowGeometry(1370, 870, maximized, 100, 1, 1920, 1080)
        );
        Assert.Equal(1370, result.Width);
        Assert.Equal(870, result.Height);
        Assert.Equal(new PixelPoint(550, 170), result.Position);
        Assert.Equal(maximized, result.Maximized);
    }

    [Theory]
    [InlineData(125, 1, 1920, 1080)]
    [InlineData(100, 1.25, 1920, 1080)]
    [InlineData(100, 1, 2560, 1080)]
    [InlineData(100, 1, 1920, 1440)]
    public void ChangedScaleOrResolutionFallsBackToNormalDefault(int scale, double dpi, int width, int height)
    {
        MainWindowPlacementResult result = ResolveGeometry(
            new ApplicationWindowGeometry(1370, 870, true, scale, dpi, width, height)
        );
        AssertDefaults(result);
    }

    [Theory]
    [InlineData(double.NaN, 870)]
    [InlineData(1370, double.PositiveInfinity)]
    [InlineData(-1, 870)]
    [InlineData(1370, 0)]
    [InlineData(700, 870)]
    [InlineData(1370, 500)]
    [InlineData(2000, 870)]
    [InlineData(1370, 1100)]
    public void UnusableSavedSizeFallsBackToDefault(double width, double height)
    {
        AssertDefaults(ResolveGeometry(new ApplicationWindowGeometry(width, height, true, 100, 1, 1920, 1080)));
    }

    [Fact]
    public void DisconnectedMonitorInvalidatesSavedGeometry()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary],
            null,
            100,
            lastPosition: new ApplicationWindowPosition(100, 100, "Disconnected"),
            lastGeometry: new ApplicationWindowGeometry(1370, 870, true, 100, 1, 1920, 1080)
        );
        AssertDefaults(result);
    }

    [Fact]
    public void ExplicitMonitorChangeCanKeepAValidShape()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve(
            [Primary],
            "DISPLAY1",
            100,
            lastGeometry: new ApplicationWindowGeometry(1370, 870, true, 100, 1, 1920, 1080)
        );
        Assert.Equal(1370, result.Width);
        Assert.True(result.Maximized);
        Assert.Equal(new PixelPoint(275, 85), result.Position);
    }

    private static MainWindowPlacementResult ResolveGeometry(ApplicationWindowGeometry geometry) =>
        MainWindowPlacement.Resolve(
            [Primary],
            null,
            100,
            lastPosition: new ApplicationWindowPosition(1800, 1000, "DISPLAY1"),
            lastGeometry: geometry
        );

    private static void AssertDefaults(MainWindowPlacementResult result)
    {
        Assert.Equal(MainWindowPlacement.DefaultWidth, result.Width);
        Assert.Equal(MainWindowPlacement.DefaultHeight, result.Height);
        Assert.False(result.Maximized);
    }

    [Fact]
    public void MissingScreenDataStillAppliesRequestedApplicationScale()
    {
        MainWindowPlacementResult result = MainWindowPlacement.Resolve([], "DISPLAY2", 90);

        Assert.Null(result.Monitor);
        Assert.Null(result.Position);
        Assert.Equal(1062, result.Width);
        Assert.Equal(684, result.Height);
        Assert.Equal(0.9, result.ApplicationScale);
    }
}
