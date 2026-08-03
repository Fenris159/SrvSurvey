using Avalonia;
using Avalonia.Media;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

public sealed class GuardianSiteMapControlTests
{
    [Fact]
    public void CommanderRemainsAtViewportCenter()
    {
        var center = new Point(250, 300);
        var proximity = new GuardianSiteProximitySnapshot(
            50,
            40,
            -20,
            40,
            -20,
            null,
            null);
        const double scale = 3;

        var commander = GuardianSiteMapControl.TransformMapPoint(
            proximity.MapX,
            proximity.MapY,
            proximity,
            217,
            center,
            scale);

        Assert.Equal(center, commander);
    }

    [Fact]
    public void MapRotatesSoCommanderHeadingPointsUp()
    {
        var proximity = new GuardianSiteProximitySnapshot(
            0,
            0,
            0,
            0,
            0,
            null,
            null);
        var center = new Point(100, 100);

        var eastWhileFacingNorth = GuardianSiteMapControl.TransformMapPoint(
            10,
            0,
            proximity,
            0,
            center,
            2);
        var eastWhileFacingEast = GuardianSiteMapControl.TransformMapPoint(
            10,
            0,
            proximity,
            90,
            center,
            2);

        Assert.Equal(new Point(120, 100), eastWhileFacingNorth);
        Assert.Equal(100, eastWhileFacingEast.X, precision: 9);
        Assert.Equal(80, eastWhileFacingEast.Y, precision: 9);
    }

    [Fact]
    public void RejectsInvalidManualScale()
    {
        var proximity = new GuardianSiteProximitySnapshot(
            0,
            0,
            0,
            0,
            0,
            null,
            null);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuardianSiteMapControl.TransformMapPoint(
                0,
                0,
                proximity,
                0,
                new Point(10, 10),
                double.NaN));
    }

    [Fact]
    public void LegendRetainsLegacyLabelsAndAddsStructureMarkersWhenPresent()
    {
        var ruins = new GuardianSiteMapProjection("Alpha", [], [], 1);
        var structure = new GuardianSiteMapProjection(
            "Lacrosse",
            [
                Point("P1", GuardianPoiType.Pylon),
                Point("C1", GuardianPoiType.Component),
            ],
            [],
            1);

        var ruinsLabels = GuardianSiteMapControl.CreateLegendLabels(ruins);
        var structureLabels = GuardianSiteMapControl.CreateLegendLabels(structure);

        Assert.Contains("Relic tower", ruinsLabels);
        Assert.Contains("Empty puddle", ruinsLabels);
        Assert.Contains("Obelisk", ruinsLabels);
        Assert.Contains("Site heading", ruinsLabels);
        Assert.Contains("Tower heading", ruinsLabels);
        Assert.Contains("Survey needed", ruinsLabels);
        Assert.DoesNotContain("Energy pylon", ruinsLabels);
        Assert.DoesNotContain("Component tower", ruinsLabels);
        Assert.Contains("Energy pylon", structureLabels);
        Assert.Contains("Component tower", structureLabels);
    }

    [Theory]
    [InlineData(GuardianPoiType.Unknown)]
    [InlineData(GuardianPoiType.Relic)]
    [InlineData(GuardianPoiType.Orb)]
    [InlineData(GuardianPoiType.Casket)]
    [InlineData(GuardianPoiType.Tablet)]
    [InlineData(GuardianPoiType.Totem)]
    [InlineData(GuardianPoiType.Urn)]
    [InlineData(GuardianPoiType.Component)]
    [InlineData(GuardianPoiType.Pylon)]
    [InlineData(GuardianPoiType.DestructiblePanel)]
    public void UnknownSurveyPointsRetainLegacySurveyMarker(
        GuardianPoiType type)
    {
        Assert.True(GuardianSiteMapControl.RequiresSurveyMarker(
            Point("P1", type, GuardianPoiStatus.Unknown)));
    }

    [Theory]
    [InlineData(GuardianPoiType.Obelisk)]
    [InlineData(GuardianPoiType.BrokenObelisk)]
    [InlineData(GuardianPoiType.EmptyPuddle)]
    public void NonSurveyMarkersDoNotUseSurveyMarker(GuardianPoiType type)
    {
        Assert.False(GuardianSiteMapControl.RequiresSurveyMarker(
            Point("P1", type, GuardianPoiStatus.Unknown)));
    }

    [Theory]
    [InlineData(GuardianPoiStatus.Present)]
    [InlineData(GuardianPoiStatus.Absent)]
    [InlineData(GuardianPoiStatus.Empty)]
    public void RecordedSurveyPointsDoNotUseSurveyMarker(
        GuardianPoiStatus status)
    {
        Assert.False(GuardianSiteMapControl.RequiresSurveyMarker(
            Point("P1", GuardianPoiType.Orb, status)));
    }

    [Fact]
    public void SurveyMarkerRetainsLegacyHaloSizingAndPalette()
    {
        Assert.Equal(
            (13d, 12d),
            GuardianSiteMapControl.GetSurveyMarkerRadii(
                GuardianPoiType.Relic));
        Assert.Equal(
            (10d, 9d),
            GuardianSiteMapControl.GetSurveyMarkerRadii(
                GuardianPoiType.Totem));
        Assert.Equal(
            Color.FromArgb(160, 72, 61, 139),
            GuardianSurveyMarkerDrawing.HaloColor);
        Assert.Equal(
            Color.FromArgb(96, 0, 255, 255),
            GuardianSurveyMarkerDrawing.RingColor);
    }

    [Fact]
    public void SurveyMarkerCreatesVisibleDotsAroundTheWholeRing()
    {
        var center = new Point(25, 30);
        var dots = GuardianSurveyMarkerDrawing.CreateDotCenters(
            center,
            ringRadius: 18,
            dotRadius: 1);

        Assert.True(dots.Count >= 8);
        Assert.All(dots, dot => Assert.Equal(
            18,
            Math.Sqrt(
                Math.Pow(dot.X - center.X, 2)
                + Math.Pow(dot.Y - center.Y, 2)),
            precision: 9));
        Assert.Contains(dots, dot => dot.X > center.X);
        Assert.Contains(dots, dot => dot.X < center.X);
        Assert.Contains(dots, dot => dot.Y > center.Y);
        Assert.Contains(dots, dot => dot.Y < center.Y);
    }

    [Fact]
    public void ArtifactPaletteMatchesLegacyGuardianMap()
    {
        var expected = new Dictionary<GuardianPoiType, (Color Fill, Color Stroke)>
        {
            [GuardianPoiType.Orb] = (
                Color.FromRgb(255, 127, 39),
                Color.FromRgb(147, 58, 0)),
            [GuardianPoiType.Casket] = (
                Color.FromRgb(34, 177, 76),
                Color.FromRgb(17, 87, 38)),
            [GuardianPoiType.Tablet] = (
                Color.FromRgb(153, 217, 234),
                Color.FromRgb(33, 135, 160)),
            [GuardianPoiType.Totem] = (
                Color.FromRgb(63, 72, 204),
                Color.FromRgb(29, 34, 105)),
            [GuardianPoiType.Urn] = (
                Color.FromRgb(163, 73, 164),
                Color.FromRgb(84, 37, 84)),
        };

        foreach (var pair in expected)
        {
            var style = GuardianLegacyMapDrawing.GetPointStyle(
                pair.Key,
                GuardianPoiStatus.Present);
            Assert.Equal(pair.Value.Fill, style.Fill);
            Assert.Equal(pair.Value.Stroke, style.Stroke);
            Assert.True(style.HasFill);
        }

        var empty = GuardianLegacyMapDrawing.GetPointStyle(
            GuardianPoiType.EmptyPuddle,
            GuardianPoiStatus.Empty);
        Assert.Equal(Colors.Gold, empty.Fill);
        Assert.Equal(Colors.Yellow, empty.Stroke);
    }

    [Fact]
    public void StructureGlyphStatusStylesMatchLegacyGuardianMap()
    {
        var activeObelisk = GuardianLegacyMapDrawing.GetPointStyle(
            GuardianPoiType.Obelisk,
            GuardianPoiStatus.Unknown,
            isActiveObelisk: true);
        var inactiveObelisk = GuardianLegacyMapDrawing.GetPointStyle(
            GuardianPoiType.Obelisk,
            GuardianPoiStatus.Present);
        var pylon = GuardianLegacyMapDrawing.GetPointStyle(
            GuardianPoiType.Pylon,
            GuardianPoiStatus.Present);
        var component = GuardianLegacyMapDrawing.GetPointStyle(
            GuardianPoiType.Component,
            GuardianPoiStatus.Present);
        var absent = GuardianLegacyMapDrawing.GetPointStyle(
            GuardianPoiType.Pylon,
            GuardianPoiStatus.Absent);

        Assert.Equal(GuardianLegacyMapDrawing.Cyan, activeObelisk.Stroke);
        Assert.Equal(GuardianLegacyMapDrawing.DarkCyan, inactiveObelisk.Stroke);
        Assert.Equal(Colors.DodgerBlue, pylon.Stroke);
        Assert.Equal(Colors.Lime, component.Stroke);
        Assert.Equal(
            GuardianLegacyStrokePattern.Dash,
            component.Pattern);
        Assert.Equal(GuardianLegacyMapDrawing.MissingStroke, absent.Stroke);
    }

    [Fact]
    public void LegacyGuardianGlyphsRetainGeometryAndScreenRotation()
    {
        var projection = new GuardianSiteMapProjection(
            "Alpha",
            [],
            [],
            1,
            IsRuins: true,
            SiteHeading: 100,
            RelicTowerHeading: 210);
        var obelisk = Point(
            "A01",
            GuardianPoiType.Obelisk,
            rotation: 30);
        var pylon = Point("P1", GuardianPoiType.Pylon, rotation: 30);
        var component = Point("C1", GuardianPoiType.Component, rotation: 30);
        var relic = Point(
            "T1",
            GuardianPoiType.Relic,
            relicHeading: 240,
            hasIndividualRelicHeading: true);

        Assert.Equal(
            157.5,
            GuardianLegacyMapDrawing.GetGlyphRotation(
                obelisk,
                projection,
                commanderHeading: 40),
            precision: 9);
        Assert.Equal(
            350,
            GuardianLegacyMapDrawing.GetGlyphRotation(
                pylon,
                projection,
                commanderHeading: 40),
            precision: 9);
        Assert.Equal(
            305,
            GuardianLegacyMapDrawing.GetGlyphRotation(
                component,
                projection,
                commanderHeading: 40),
            precision: 9);
        Assert.Equal(
            280,
            GuardianLegacyMapDrawing.GetGlyphRotation(
                relic,
                projection,
                commanderHeading: 40),
            precision: 9);

        Assert.Equal(
            4,
            GuardianLegacyMapDrawing.CreateGlyphPoints(
                GuardianPoiType.Obelisk,
                new Point(),
                0).Count);
        var broken = GuardianLegacyMapDrawing.CreateGlyphPoints(
            GuardianPoiType.BrokenObelisk,
            new Point(),
            0);
        Assert.Equal(new Point(-0.5, 2.5), broken[0]);
        Assert.Equal(new Point(2.5, -0.5), broken[2]);
        Assert.Equal(
            5,
            GuardianLegacyMapDrawing.CreateGlyphPoints(
                GuardianPoiType.Pylon,
                new Point(),
                0).Count);
        Assert.Equal(
            8,
            GuardianLegacyMapDrawing.CreateGlyphPoints(
                GuardianPoiType.Component,
                new Point(),
                0).Count);
    }

    [Fact]
    public void ComponentMaterialDotsStayScreenAlignedLikeLegacyRenderer()
    {
        var center = new Point(50, 60);
        var dots = GuardianLegacyMapDrawing.CreateComponentMaterialCenters(center);

        Assert.Equal(3, dots.Count);
        Assert.All(dots, dot => Assert.Equal(
            8,
            Math.Sqrt(
                Math.Pow(dot.X - center.X, 2)
                + Math.Pow(dot.Y - center.Y, 2)),
            precision: 9));
        Assert.True(dots[0].X > center.X && dots[0].Y < center.Y);
        Assert.True(dots[1].X < center.X);
        Assert.True(dots[2].X > center.X && dots[2].Y > center.Y);
        Assert.Equal(
            Colors.Lime,
            GuardianLegacyMapDrawing.GetComponentMaterialColor(
                GuardianComponentMaterial.Cell));
        Assert.Equal(
            Colors.Cyan,
            GuardianLegacyMapDrawing.GetComponentMaterialColor(
                GuardianComponentMaterial.Conduit));
        Assert.Equal(
            Colors.OrangeRed,
            GuardianLegacyMapDrawing.GetComponentMaterialColor(
                GuardianComponentMaterial.Tech));
    }

    [Fact]
    public void ActiveObeliskEffectsUseLegacyMissionScanPrecedence()
    {
        Assert.Equal(
            GuardianLegacyMapDrawing.Cyan,
            GuardianLegacyMapDrawing.GetActiveObeliskEffectColor(Point(
                "A01",
                GuardianPoiType.Obelisk,
                isActiveObelisk: true,
                isScannedObelisk: true,
                isRamTahNeededObelisk: true)));
        Assert.Equal(
            Color.FromRgb(255, 111, 0),
            GuardianLegacyMapDrawing.GetActiveObeliskEffectColor(Point(
                "A01",
                GuardianPoiType.Obelisk,
                isActiveObelisk: true,
                isScannedObelisk: true)));
        Assert.Equal(
            Colors.LightGray,
            GuardianLegacyMapDrawing.GetActiveObeliskEffectColor(Point(
                "A01",
                GuardianPoiType.Obelisk,
                isActiveObelisk: true)));
    }

    [Fact]
    public void RelicHeadingLinePassesThroughTowerAtRecordedAngle()
    {
        var center = new Point(25, 40);
        var (start, end) = GuardianLegacyMapDrawing.CreateHeadingLine(
            center,
            100,
            90);

        Assert.Equal(center, new Point(
            (start.X + end.X) / 2,
            (start.Y + end.Y) / 2));
        Assert.Equal(125, start.X, precision: 9);
        Assert.Equal(-75, end.X, precision: 9);
        Assert.Equal(40, start.Y, precision: 9);
        Assert.Equal(40, end.Y, precision: 9);
    }

    [Fact]
    public void ActiveObeliskWedgeRetainsLegacyNinetyDegreeArc()
    {
        var center = new Point(10, 15);
        var wedge = GuardianLegacyMapDrawing.CreateWedge(
            center,
            radius: 30,
            rotation: 0,
            segments: 9);

        Assert.Equal(11, wedge.Count);
        Assert.Equal(center, wedge[0]);
        Assert.Equal(
            30,
            Math.Sqrt(
                Math.Pow(wedge[1].X - center.X, 2)
                + Math.Pow(wedge[1].Y - center.Y, 2)),
            precision: 9);
        var firstAngle = Math.Atan2(
            wedge[1].Y - center.Y,
            wedge[1].X - center.X) * 180 / Math.PI;
        var lastAngle = Math.Atan2(
            wedge[^1].Y - center.Y,
            wedge[^1].X - center.X) * 180 / Math.PI;
        Assert.Equal(-120, firstAngle, precision: 9);
        Assert.Equal(-30, lastAngle, precision: 9);
    }

    private static GuardianProjectedPoint Point(
        string name,
        GuardianPoiType type,
        GuardianPoiStatus status = GuardianPoiStatus.Present,
        double rotation = 0,
        bool isActiveObelisk = false,
        bool isScannedObelisk = false,
        int relicHeading = -1,
        bool hasIndividualRelicHeading = false,
        bool isRamTahNeededObelisk = false)
    {
        return new GuardianProjectedPoint(
            name,
            type,
            0,
            0,
            0,
            0,
            rotation,
            status,
            isActiveObelisk,
            isScannedObelisk,
            string.Empty,
            [],
            relicHeading,
            hasIndividualRelicHeading,
            isRamTahNeededObelisk);
    }
}
