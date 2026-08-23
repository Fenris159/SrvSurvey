using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
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
    public void BackgroundTransformMatchesMarkerTransform()
    {
        var proximity = new GuardianSiteProximitySnapshot(
            12,
            5,
            -8,
            5,
            -8,
            null,
            null);
        var center = new Point(320, 240);
        const double heading = 73;
        const double scale = 1.7;
        var matrix = GuardianSiteMapControl.CreateMapTransform(
            proximity,
            heading,
            center,
            scale);
        var mapPoint = new Point(41, -22);
        var transformedBackgroundPoint = new Point(
            (mapPoint.X * matrix.M11) + (mapPoint.Y * matrix.M21) + matrix.M31,
            (mapPoint.X * matrix.M12) + (mapPoint.Y * matrix.M22) + matrix.M32);
        var transformedMarkerPoint = GuardianSiteMapControl.TransformMapPoint(
            mapPoint.X,
            mapPoint.Y,
            proximity,
            heading,
            center,
            scale);

        Assert.Equal(transformedMarkerPoint.X, transformedBackgroundPoint.X, 9);
        Assert.Equal(transformedMarkerPoint.Y, transformedBackgroundPoint.Y, 9);
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
    public void LegendMatchesLegacyRuinsAndStructureKeysExactly()
    {
        var ruins = new GuardianSiteMapProjection(
            "Alpha",
            [],
            [],
            1,
            IsRuins: true);
        var structure = new GuardianSiteMapProjection(
            "Lacrosse",
            [],
            [],
            1);

        var ruinsLabels = GuardianSiteMapControl.CreateLegendLabels(ruins);
        var structureLabels = GuardianSiteMapControl.CreateLegendLabels(structure);

        Assert.Equal(
            [
                "Relic Tower",
                "Orb",
                "Casket",
                "Tablet",
                "Totem",
                "Urn",
                "Empty puddle",
                "Obelisk",
                "Site heading",
                "Tower heading",
                "Survey needed",
            ],
            ruinsLabels);
        Assert.Equal(
            [
                "Relic Tower",
                "Orb",
                "Casket",
                "Tablet",
                "Totem",
                "Urn",
                "Empty puddle",
                "Obelisk",
                "Energy pylon",
                "Component tower",
                "Site heading",
                "Tower heading",
                "Survey needed",
            ],
            structureLabels);
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
    public void SurveyMarkerRetainsLegacySiteSpecificSizingAndPalette()
    {
        Assert.Equal(
            (17.8d, 16.8d),
            GuardianSiteMapControl.GetSurveyMarkerRadii(
                GuardianPoiType.Relic,
                isRuins: true));
        Assert.Equal(
            (13d, 12d),
            GuardianSiteMapControl.GetSurveyMarkerRadii(
                GuardianPoiType.Totem,
                isRuins: true));
        Assert.Equal(
            (13d, 12d),
            GuardianSiteMapControl.GetSurveyMarkerRadii(
                GuardianPoiType.Relic,
                isRuins: false));
        Assert.Equal(
            (10d, 9d),
            GuardianSiteMapControl.GetSurveyMarkerRadii(
                GuardianPoiType.Totem,
                isRuins: false));
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
    public void GuardianMarkersScaleWithTheMapLikeLegacyRenderer()
    {
        var center = new Point(20, 30);
        var full = GuardianLegacyMapDrawing.CreateGlyphPoints(
            GuardianPoiType.Relic,
            center,
            rotation: 0,
            scale: 1);
        var half = GuardianLegacyMapDrawing.CreateGlyphPoints(
            GuardianPoiType.Relic,
            center,
            rotation: 0,
            scale: 0.5);
        var ruins = new GuardianSiteMapProjection(
            "Alpha",
            [],
            [],
            1,
            IsRuins: true);

        Assert.Equal(center + new Vector(-8, -8), full[0]);
        Assert.Equal(center + new Vector(-4, -4), half[0]);
        Assert.Equal(
            8,
            GuardianLegacyMapDrawing.GetPuddleRadius(
                ruins,
                Point("P1", GuardianPoiType.Orb)));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(0.5, 1)]
    [InlineData(4, 4)]
    [InlineData(12, 10)]
    public void InteractiveViewportZoomUsesBoundedLegacyRange(
        double requested,
        double expected)
    {
        Assert.Equal(
            expected,
            GuardianSiteMapControl.NormalizeViewportZoom(requested));
    }

    [Fact]
    public void InteractiveViewportPanKeepsPartOfTheMapReachable()
    {
        Assert.Equal(
            default,
            GuardianSiteMapControl.ClampViewportOffset(
                new Vector(100, -100),
                new Size(720, 640),
                zoom: 1));
        Assert.Equal(
            new Vector(360, -320),
            GuardianSiteMapControl.ClampViewportOffset(
                new Vector(900, -900),
                new Size(720, 640),
                zoom: 2));
    }

    [AvaloniaFact]
    public void HoveringProjectedPointExposesMarkerSelectionFeedback()
    {
        var control = new GuardianSiteMapControl
        {
            Projection = new GuardianSiteMapProjection(
                "Lacrosse",
                [RenderPoint(
                    "P1",
                    GuardianPoiType.Orb,
                    0,
                    0,
                    GuardianPoiStatus.Present)],
                [],
                1),
            MapScale = 4,
            AllowViewportInteraction = true,
            ShowLegend = false,
        };
        var window = new Window
        {
            Width = 720,
            Height = 640,
            Content = control,
        };

        try
        {
            window.Show();
            window.MouseMove(
                new Point(360, 320),
                RawInputModifiers.None);

            Assert.Equal("P1", control.HoveredPointName);
            window.MouseDown(
                new Point(360, 320),
                MouseButton.Left,
                RawInputModifiers.None);
            window.MouseUp(
                new Point(360, 320),
                MouseButton.Left,
                RawInputModifiers.None);
            Assert.Equal("P1", control.SelectedPointName);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var outputPath = Environment.GetEnvironmentVariable(
                "SRVSURVEY_GUARDIAN_MAP_SELECTION_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                frame.Save(outputPath, PngBitmapEncoderOptions.Default);
            }

            window.MouseDown(
                new Point(40, 40),
                MouseButton.Left,
                RawInputModifiers.None);
            window.MouseUp(
                new Point(40, 40),
                MouseButton.Left,
                RawInputModifiers.None);
            Assert.Null(control.SelectedPointName);
        }
        finally
        {
            window.Close();
        }
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

    [AvaloniaFact]
    public void CompleteGuardianSurveyProjectionRendersEveryLegacyMapLayer()
    {
        var points = new GuardianProjectedPoint[]
        {
            RenderPoint(
                "T1",
                GuardianPoiType.Relic,
                -18,
                -12,
                GuardianPoiStatus.Unknown,
                relicHeading: 210,
                hasIndividualRelicHeading: true),
            RenderPoint("ORB", GuardianPoiType.Orb, -9, -5, GuardianPoiStatus.Present),
            RenderPoint("CASKET", GuardianPoiType.Casket, 4, -7, GuardianPoiStatus.Absent),
            RenderPoint("TABLET", GuardianPoiType.Tablet, 13, -4, GuardianPoiStatus.Empty),
            RenderPoint("TOTEM", GuardianPoiType.Totem, -15, 5, GuardianPoiStatus.Unknown),
            RenderPoint("URN", GuardianPoiType.Urn, -4, 9, GuardianPoiStatus.Present),
            RenderPoint("EMPTY", GuardianPoiType.EmptyPuddle, 8, 7, GuardianPoiStatus.Empty),
            RenderPoint(
                "COMP",
                GuardianPoiType.Component,
                17,
                11,
                GuardianPoiStatus.Present,
                materials:
                [
                    GuardianComponentMaterial.Cell,
                    GuardianComponentMaterial.Unknown,
                    GuardianComponentMaterial.Tech,
                ]),
            RenderPoint("PYLON", GuardianPoiType.Pylon, 3, 18, GuardianPoiStatus.Present),
            RenderPoint(
                "A01",
                GuardianPoiType.Obelisk,
                -12,
                17,
                GuardianPoiStatus.Unknown,
                isActiveObelisk: true,
                isScannedObelisk: true,
                isRamTahNeededObelisk: true),
            RenderPoint("BROKEN", GuardianPoiType.BrokenObelisk, 12, 17, GuardianPoiStatus.Absent),
            RenderPoint(
                "PANEL",
                GuardianPoiType.DestructiblePanel,
                20,
                -17,
                GuardianPoiStatus.Present,
                materials: [GuardianComponentMaterial.Conduit]),
            RenderPoint(
                "UNKNOWN-PANEL",
                GuardianPoiType.DestructiblePanel,
                -21,
                -17,
                GuardianPoiStatus.Unknown),
            RenderPoint("UNKNOWN", GuardianPoiType.Unknown, 0, -20, GuardianPoiStatus.Unknown),
        };
        var projection = new GuardianSiteMapProjection(
            "Alpha",
            points,
            [new GuardianProjectedGroup("A", 0, 14, 0, 14)],
            30,
            IsRuins: true,
            SiteHeading: 120,
            RelicTowerHeading: 210);
        var nearestPoi = new GuardianPointOfInterest(
            "PYLON",
            GuardianPoiType.Pylon,
            0,
            18,
            0);
        var control = new GuardianSiteMapControl
        {
            Projection = projection,
            Proximity = new GuardianSiteProximitySnapshot(
                8,
                1,
                2,
                1,
                2,
                new GuardianNearbyPoint(nearestPoi, 20, 3, 18, null),
                null),
            MapScale = 5,
            CommanderHeading = 35,
            TargetPointName = "T1",
            MapBackground = Brushes.Black,
            GridBrush = Brushes.DarkSlateGray,
            AccentBrush = Brushes.Cyan,
            MutedBrush = Brushes.Wheat,
            PresentBrush = Brushes.LimeGreen,
            AbsentBrush = Brushes.Red,
            EmptyBrush = Brushes.Goldenrod,
            ShowLegend = true,
        };

        Assert.True(Render(control));
    }

    [AvaloniaFact]
    public void PackagedLegacyMapArtworkIsAvailableForMappedSiteTypes()
    {
        string[] mappedSiteTypes =
        [
            "Alpha",
            "Beta",
            "Gamma",
            "Bear",
            "Fistbump",
            "Robolobster",
            "Lacrosse",
            "Turtle",
            "Crossroads",
            "Hammerbot",
            "Bowl",
        ];
        var templates = GuardianSiteTemplateCatalog.LoadEmbedded();
        var projector = new GuardianSiteMapProjector();
        foreach (var siteType in mappedSiteTypes)
        {
            var projection = projector.Project(
                templates.Find(siteType)
                ?? throw new InvalidOperationException(
                    $"{siteType} template is missing."));

            Assert.NotNull(GuardianMapImageCatalog.Find(projection));
        }

        foreach (var siteType in new[] { "Squid", "Stickyhand" })
        {
            var projection = projector.Project(
                templates.Find(siteType)
                ?? throw new InvalidOperationException(
                    $"{siteType} template is missing."));

            Assert.Null(GuardianMapImageCatalog.Find(projection));
        }

        var beta = projector.Project(
            templates.Find("Beta")
            ?? throw new InvalidOperationException("Beta template is missing."));
        Assert.Equal(
            "beta-background.png",
            GuardianMapImageCatalog.ResolveFileName(beta));
        Assert.Equal(
            new Size(1024, 1024),
            GuardianMapImageCatalog.Find(beta)?.Size);
    }

    [AvaloniaFact]
    public void ExternalLegendRendersWithoutDrawingTheMapSurface()
    {
        var control = new GuardianSiteMapControl
        {
            Projection = new GuardianSiteMapProjection(
                "Lacrosse",
                [
                    RenderPoint(
                        "P1",
                        GuardianPoiType.Pylon,
                        0,
                        0,
                        GuardianPoiStatus.Present),
                    RenderPoint(
                        "C1",
                        GuardianPoiType.Component,
                        1,
                        1,
                        GuardianPoiStatus.Present),
                ],
                [],
                1),
            IsLegendOnly = true,
            ShowLegend = false,
            MutedBrush = Brushes.Wheat,
        };

        Assert.True(Render(control));
    }

    [AvaloniaFact]
    public void MapAlsoRendersFittedNorthUpWithoutCommanderOrLegend()
    {
        var control = new GuardianSiteMapControl
        {
            Projection = new GuardianSiteMapProjection(
                "Lacrosse",
                [RenderPoint("ORB", GuardianPoiType.Orb, 2, 3, GuardianPoiStatus.Present)],
                [],
                10,
                SiteHeading: -1,
                RelicTowerHeading: -1),
            MapScale = double.NaN,
            CommanderHeading = double.NaN,
            ShowLegend = false,
        };

        Assert.True(Render(control));
    }

    private static bool Render(GuardianSiteMapControl control)
    {
        var size = new Size(720, 640);
        var window = new Window
        {
            Width = size.Width,
            Height = size.Height,
            Content = control,
        };

        try
        {
            window.Show();
            var frame = window.CaptureRenderedFrame();
            return frame?.PixelSize == new PixelSize(720, 640);
        }
        finally
        {
            window.Close();
        }
    }

    private static GuardianProjectedPoint RenderPoint(
        string name,
        GuardianPoiType type,
        double x,
        double y,
        GuardianPoiStatus status,
        bool isActiveObelisk = false,
        bool isScannedObelisk = false,
        int relicHeading = -1,
        bool hasIndividualRelicHeading = false,
        bool isRamTahNeededObelisk = false,
        IReadOnlyList<GuardianComponentMaterial>? materials = null)
    {
        var distance = Math.Sqrt(x * x + y * y);
        var angle = Math.Atan2(y, x) * 180 / Math.PI;
        return new GuardianProjectedPoint(
            name,
            type,
            x,
            y,
            angle,
            distance,
            25,
            status,
            isActiveObelisk,
            isScannedObelisk,
            string.Empty,
            materials ?? [],
            relicHeading,
            hasIndividualRelicHeading,
            isRamTahNeededObelisk);
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
