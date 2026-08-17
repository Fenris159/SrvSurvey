using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayThemeResourcesTests
{
    private static readonly IReadOnlyDictionary<string, string>
        ExpectedConfigurationBindings = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["PlotBioStatus"] = "SystemSurvey.AutoShowBioStatus",
            ["PlotBioSystem"] = "SystemSurvey.AutoShowBioSystem",
            ["PlotBodyInfo"] = "SystemSurvey.AutoShowBodyInfo",
            ["PlotBuildCommodities"] =
                "Colonization.AutoShowCommodityOverlay",
            ["PlotFlightWarning"] = "SystemSurvey.AutoShowFlightWarnings",
            ["PlotFloatie"] = "Notifications.Enabled",
            ["PlotFootCombat"] = "Combat.AutoShowFootCombat",
            ["PlotFSS"] = "SystemSurvey.AutoShowLastFssBody",
            ["PlotFSSInfo"] = "SystemSurvey.AutoShowFssInfo",
            ["PlotGalMap"] = "GalaxyMap.AutoShow",
            ["PlotGrounded"] = "SystemSurvey.AutoShowSurfaceRadar",
            ["PlotGuardians"] = "Guardian.EnableGuardianSites",
            ["PlotGuardianStatus"] = "Guardian.EnableGuardianSites",
            ["PlotGuardianSystem"] = "Guardian.AutoShowGuardianSummary",
            ["PlotHumanSite"] = "HumanSite.AutoShow",
            ["PlotJumpInfo"] = "JumpInfo.AutoShow",
            ["PlotFleetCarrierRoute"] = "FleetCarrierRoute.IsActive",
            ["PlotRouteBio"] = "Route.IsActive",
            ["PlotMassacre"] = "Combat.AutoShowMassacreMissions",
            ["PlotMiniTrack"] = "SystemSurvey.AutoShowMiniTrack",
            ["PlotMultiGameCommander"] =
                "OverlayBehavior.HideMultiGameCommanderOverlay",
            ["PlotPriorScans"] = "SystemSurvey.AutoShowPriorScans",
            ["PlotPulse"] = "PulseOverlay.Enabled",
            ["PlotQuestMini"] = "QuestWorkspace.IsEnabled",
            ["PlotRamTah"] = "Guardian.AutoShowRamTah",
            ["PlotSphericalSearch"] =
                "Search, BoxelSearch, or Route active",
            ["PlotStationInfo"] = "StationInfo.AutoShow",
            ["PlotSysStatus"] = "SystemSurvey.AutoShowSystemStatus",
            ["PlotTrackTarget"] = "GroundTarget.ShouldShow",
        };

    [Fact]
    public void RuntimeSurfaceUsesCompactBorderlessPreviewChrome()
    {
        var surface = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(18),
            BorderBrush = Brushes.Cyan,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Opacity = 0.97,
        };

        OverlayThemeResources.ApplySurfaceChrome(
            surface,
            isEditorPreview: false,
            Brushes.Black,
            Brushes.Yellow);

        Assert.Equal(new Thickness(0), surface.Margin);
        Assert.Equal(new Thickness(4), surface.Padding);
        Assert.Same(Brushes.Black, surface.Background);
        Assert.Null(surface.BorderBrush);
        Assert.Equal(new Thickness(0), surface.BorderThickness);
        Assert.Equal(new CornerRadius(5), surface.CornerRadius);
        Assert.Equal(1d, surface.Opacity);
    }

    [AvaloniaFact]
    public void ApplyThemesAndRefreshesAWindowSurfaceWithoutChangingBadgePalette()
    {
        var surface = new Border
        {
            Background = Brushes.DarkBlue,
            BorderBrush = Brushes.Cyan,
            BorderThickness = new Thickness(4),
            Opacity = 0.5,
        };
        var window = new Window { Content = surface };

        OverlayThemeResources.Apply(window);
        OverlayThemeResources.Apply(window);
        OverlayThemeResources.RefreshAll();

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.Contains(
            OverlayThemeResources.OverlayTypographyClass,
            window.Classes);
        Assert.Equal(new Thickness(0), surface.Margin);
        Assert.Equal(new Thickness(4), surface.Padding);
        Assert.Null(surface.BorderBrush);
        Assert.Equal(new Thickness(0), surface.BorderThickness);
        Assert.Equal(1d, surface.Opacity);
    }

    [AvaloniaFact]
    public void EveryOverlayUsesBundledRoleBasedTypography()
    {
        var primary = new TextBlock { Text = "Primary" };
        var header = new TextBlock { Text = "Header", Classes = { "overlay-header" } };
        var eyebrow = new TextBlock { Text = "Eyebrow", Classes = { "eyebrow" } };
        var muted = new TextBlock { Text = "Muted", Classes = { "muted" } };
        var value = new TextBlock
        {
            Text = "Value",
            Classes = { "monospace", "overlay-value" },
        };
        var compactBySize = new TextBlock
        {
            Text = "Size alone remains primary",
            FontSize = 9,
        };
        var detail = new TextBlock
        {
            Text = "Longer detail",
            Classes = { "overlay-detail" },
        };
        var caption = new TextBlock
        {
            Text = "Caption",
            Classes = { "overlay-caption" },
        };
        var guardianPrimary = new TextBlock
        {
            Text = "Guardian primary",
            Classes = { "guardian-legacy-middle" },
        };
        var guardianCompact = new TextBlock
        {
            Text = "Guardian compact",
            Classes = { "guardian-legacy-small" },
        };
        var window = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    primary,
                    header,
                    eyebrow,
                    muted,
                    value,
                    compactBySize,
                    detail,
                    caption,
                    guardianPrimary,
                    guardianCompact,
                },
            },
        };

        OverlayThemeResources.Apply(window);
        window.Show();

        Assert.Contains("Oxanium", window.FontFamily.Name);
        Assert.Contains("Oxanium", primary.FontFamily.Name);
        Assert.Contains("Rajdhani", header.FontFamily.Name);
        Assert.Equal(10, header.FontSize);
        Assert.Contains("Oxanium", guardianPrimary.FontFamily.Name);
        Assert.Contains("Rajdhani", eyebrow.FontFamily.Name);
        Assert.Contains("Rajdhani", muted.FontFamily.Name);
        Assert.Contains("Oxanium", value.FontFamily.Name);
        Assert.Contains("Oxanium", compactBySize.FontFamily.Name);
        Assert.Contains("Rajdhani", detail.FontFamily.Name);
        Assert.Contains("Rajdhani", caption.FontFamily.Name);
        Assert.Equal(12, value.FontSize);
        Assert.Equal(10, detail.FontSize);
        Assert.Equal(9, caption.FontSize);
        Assert.Contains("Rajdhani", guardianCompact.FontFamily.Name);

        window.Close();
    }

    [Fact]
    public void OverlayFontFilesAndLicensesArePackaged()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "SrvSurvey.Desktop.csproj"));

        Assert.Contains("Assets\\Fonts\\**\\*.ttf", project);
        Assert.Contains("Assets\\Fonts\\**\\OFL.txt", project);
        Assert.True(new FileInfo(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "Fonts",
            "Oxanium",
            "Oxanium-Variable.ttf")).Length > 0);
        Assert.True(new FileInfo(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "Fonts",
            "Rajdhani",
            "Rajdhani-Regular.ttf")).Length > 0);
        Assert.True(File.Exists(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "Fonts",
            "Oxanium",
            "OFL.txt")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "Fonts",
            "Rajdhani",
            "OFL.txt")));
    }

    [AvaloniaFact]
    public void FullApplyTracksPerPanelOpacityScaleAndBaseSizeChanges()
    {
        var definition = OverlayLayoutCatalog.GetRequired("PlotJumpInfo");
        var placement = definition.DefaultPlacement with
        {
            Opacity = 0.42,
            ScaleIndex = 3,
        };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(StringComparer.Ordinal)
            {
                [definition.Name] = placement,
            },
            defaultOpacity: 0.9,
            error: null);
        layout.SetScaleIndex(2);
        var originalContent = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Route and destination intelligence" },
                new Border
                {
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(8),
                    Child = new TextBlock { Text = "Details", FontSize = 18 },
                },
            },
        };
        var surface = new Border { Child = originalContent };
        var window = new Window
        {
            Width = 400,
            Height = 100,
            MinWidth = 700,
            MaxWidth = 500,
            Content = surface,
        };

        OverlayThemeResources.Apply(window, layout, definition.Name);

        var scaleContainer = Assert.IsType<LayoutTransformControl>(window.Content);
        Assert.Same(surface, scaleContainer.Child);
        Assert.IsType<ScaleTransform>(scaleContainer.LayoutTransform);
        Assert.Equal(0.42, window.Opacity);
        Assert.Equal(definition.PreviewSize.Width * 1.2, window.Width, 5);
        // Shared *Presentation hosts own their title/chrome; legacy header
        // injection is skipped so the original surface content stays intact.
        Assert.Same(originalContent, surface.Child);

        Assert.True(layout.SetPlacement(
            definition.Name,
            placement with { Opacity = 0.75, ScaleIndex = 1 }));
        Assert.Equal(0.75, window.Opacity);
        Assert.Equal(definition.PreviewSize.Width, window.Width, 5);

        OverlayThemeResources.SetBaseSize(window, layout, 250, 125);
        OverlayThemeResources.SetBaseSize(window, layout, 250, 125);
        Assert.Equal(250, window.Width, 5);
        Assert.Equal(125, window.Height, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayThemeResources.SetBaseSize(window, layout, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayThemeResources.SetBaseSize(window, layout, 100, double.NaN));

        OverlayThemeResources.Apply(window, layout, definition.Name);
        var otherLayout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(StringComparer.Ordinal),
            defaultOpacity: null,
            error: null);
        Assert.Throws<InvalidOperationException>(() =>
            OverlayThemeResources.Apply(
                window,
                otherLayout,
                definition.Name));

        window.Show();
        window.Close();
        Assert.True(layout.SetPlacement(
            definition.Name,
            placement with { Opacity = 0.5, ScaleIndex = 2 }));
    }

    [AvaloniaFact]
    public void ScaleAndFormFactorGracefullyHandleContentlessOrUnknownWindows()
    {
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(StringComparer.Ordinal),
            defaultOpacity: null,
            error: null);
        var contentless = new Window { Width = 123 };

        OverlayThemeResources.ApplyScale(contentless, layout);
        OverlayThemeResources.ApplyScale(contentless, layout, "PlotJumpInfo");
        OverlayThemeResources.ApplyScale(contentless, scaleIndex: 25, renderScaling: 2);
        OverlayThemeResources.ApplyLegacyFormFactor(contentless, "UnknownPanel");

        Assert.Equal(123, contentless.Width);
        Assert.Null(OverlayThemeResources.GetLegacyFormFactorWidth("UnknownPanel"));
    }

    [Fact]
    public void EditorPreviewAddsOnlyTheYellowPositionGuide()
    {
        var surface = new Border { Opacity = 0.42 };

        OverlayThemeResources.ApplySurfaceChrome(
            surface,
            isEditorPreview: true,
            Brushes.Black,
            Brushes.Yellow);

        Assert.Equal(new Thickness(1), surface.Margin);
        Assert.Equal(new Thickness(4), surface.Padding);
        Assert.Same(Brushes.Black, surface.Background);
        Assert.Same(Brushes.Yellow, surface.BorderBrush);
        Assert.Equal(new Thickness(2), surface.BorderThickness);
        Assert.Equal(new CornerRadius(5), surface.CornerRadius);
        Assert.Equal(0.42, surface.Opacity);
    }

    [AvaloniaFact]
    public void GuardianEditorPreviewsUseTheLivePresentationControls()
    {
        var viewModel = GuardianOverlayViewModel.CreateEditorPreview();
        (string PlotterName, Type PresentationType, Window LiveWindow)[] cases =
        [
            (
                "PlotGuardians",
                typeof(GuardianSiteOverlayPresentation),
                new GuardianOverlayWindow(viewModel)),
            (
                "PlotGuardianStatus",
                typeof(GuardianStatusOverlayPresentation),
                new GuardianStatusOverlayWindow(viewModel)),
            (
                "PlotGuardianSystem",
                typeof(GuardianSystemOverlayPresentation),
                new GuardianSystemOverlayWindow(viewModel)),
            (
                "PlotRamTah",
                typeof(RamTahOverlayPresentation),
                new RamTahOverlayWindow(viewModel)),
        ];

        foreach (var testCase in cases)
        {
            var definition = OverlayLayoutCatalog.GetRequired(
                testCase.PlotterName);
            var preview = new OverlayPositionPreviewWindow(definition);
            var liveSurface = Assert.IsType<Border>(
                testCase.LiveWindow.Content);

            Assert.Equal(
                testCase.PresentationType,
                Assert.IsType<Control>(liveSurface.Child, exactMatch: false)
                    .GetType());
            Assert.Equal(
                testCase.PresentationType,
                Assert.IsType<Control>(
                    preview.RuntimePresentation,
                    exactMatch: false).GetType());
            Assert.IsType<GuardianOverlayViewModel>(
                preview.RuntimePresentation?.DataContext);
        }
    }

    [Fact]
    public void GuardianEditorPreviewsUseCompactContentDrivenFormFactors()
    {
        var expected = new Dictionary<string, PixelSize>(StringComparer.Ordinal)
        {
            ["PlotGuardians"] = new PixelSize(300, 400),
            ["PlotGuardianStatus"] = new PixelSize(260, 108),
            ["PlotGuardianSystem"] = new PixelSize(190, 96),
            ["PlotRamTah"] = new PixelSize(190, 224),
        };

        Assert.All(expected, pair => Assert.Equal(
            pair.Value,
            OverlayLayoutCatalog.GetRequired(pair.Key).PreviewSize));
    }

    [AvaloniaFact]
    public void DedicatedGuardianPresentationBypassesGenericHeaderAndCardNormalization()
    {
        var definition = OverlayLayoutCatalog.GetRequired("PlotGuardianSystem");
        var presentation = new GuardianSystemOverlayPresentation
        {
            DataContext = GuardianOverlayViewModel.CreateEditorPreview(),
        };
        var surface = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Child = presentation,
        };
        var window = new Window
        {
            Width = definition.PreviewSize.Width,
            Content = surface,
        };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(
                StringComparer.Ordinal),
            defaultOpacity: null,
            error: null);

        OverlayThemeResources.Apply(window, layout, definition.Name);

        var scaled = Assert.IsType<LayoutTransformControl>(window.Content);
        Assert.Same(surface, scaled.Child);
        Assert.Same(presentation, surface.Child);
        Assert.Equal(new Thickness(0), surface.Padding);
        Assert.Equal(new CornerRadius(0), surface.CornerRadius);
        Assert.Null(surface.BorderBrush);
        Assert.Equal(new Thickness(0), surface.BorderThickness);
    }

    [Fact]
    public void RuntimeWindowUsesTheSameLegacyWidthAsItsEditorPreview()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(candidate =>
            candidate.Name == "PlotBioSystem");

        Assert.Equal(
            definition.PreviewSize.Width,
            OverlayThemeResources.GetLegacyFormFactorWidth(
                definition.Name));
    }

    [Fact]
    public void EveryRuntimeOverlayUsesItsEditorCatalogWidth()
    {
        Assert.Equal(29, OverlayLayoutCatalog.Supported.Count);
        Assert.All(OverlayLayoutCatalog.Supported, definition =>
            Assert.Equal(
                definition.PreviewSize.Width,
                OverlayThemeResources.GetLegacyFormFactorWidth(
                definition.Name)));
    }

    [Fact]
    public void ContentDrivenLegacyPanelsRetainTheirWidestSampleRows()
    {
        var expected = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["PlotBioSystem"] = 240,
            ["PlotBuildCommodities"] = 270,
            ["PlotMassacre"] = 190,
            ["PlotQuestMini"] = 220,
            ["PlotStationInfo"] = 220,
        };

        Assert.All(expected, pair => Assert.Equal(
            pair.Value,
            OverlayThemeResources.GetLegacyFormFactorWidth(pair.Key)));
    }

    [Fact]
    public void EveryPassivePanelIsMappedToItsConfigurationSource()
    {
        Assert.Equal(
            ExpectedConfigurationBindings.Keys.Order(StringComparer.Ordinal),
            OverlayLayoutCatalog.Supported
                .Select(definition => definition.Name)
                .Order(StringComparer.Ordinal));
        Assert.All(OverlayLayoutCatalog.Supported, definition =>
            Assert.Equal(
                ExpectedConfigurationBindings[definition.Name],
                definition.ConfigurationBinding));
    }

    [Fact]
    public void UnknownPassivePanelIdentityIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutCatalog.GetRequired("PlotWrongPanel"));
    }

    [Fact]
    public void AvaloniaCardChromeIsRemovedFromRuntimeOverlayContent()
    {
        var root = new Border();
        var card = new Border
        {
            Padding = new Thickness(10),
            Background = Brushes.DarkBlue,
            BorderBrush = Brushes.Cyan,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        OverlayThemeResources.NormalizeLegacyOverlayControl(card, root);

        Assert.Equal(new Thickness(0), card.Padding);
        Assert.Same(Brushes.Transparent, card.Background);
        Assert.Null(card.BorderBrush);
        Assert.Equal(new Thickness(0), card.BorderThickness);
        Assert.Equal(new CornerRadius(0), card.CornerRadius);
    }

    [Fact]
    public void RuntimeRouteBadgesRetainTheirTranslucentPillChrome()
    {
        var root = new Border();
        var background = new SolidColorBrush(Color.FromArgb(96, 20, 60, 90));
        var badge = new Border
        {
            Padding = new Thickness(7, 2),
            Background = background,
            CornerRadius = new CornerRadius(999),
        };
        badge.Classes.Add("badge");

        OverlayThemeResources.NormalizeLegacyOverlayControl(badge, root);

        Assert.Equal(new Thickness(7, 2), badge.Padding);
        Assert.Same(background, badge.Background);
        Assert.Equal(new CornerRadius(999), badge.CornerRadius);
    }

    [Fact]
    public void RuntimeTypographyAndSpacingUseTheCompactPreviewScale()
    {
        var root = new Border();
        var heading = new TextBlock { FontSize = 20 };
        heading.Classes.Add("eyebrow");
        var stack = new StackPanel { Spacing = 10 };
        var grid = new Grid { RowSpacing = 9, ColumnSpacing = 12 };
        var progress = new ProgressBar { Height = 9 };

        OverlayThemeResources.NormalizeLegacyOverlayControl(heading, root);
        OverlayThemeResources.NormalizeLegacyOverlayControl(stack, root);
        OverlayThemeResources.NormalizeLegacyOverlayControl(grid, root);
        OverlayThemeResources.NormalizeLegacyOverlayControl(progress, root);
        OverlayThemeResources.NormalizeLegacyOverlayControl(root, root);

        Assert.False(heading.IsVisible);
        Assert.Equal(12, heading.FontSize);
        Assert.Equal(3, stack.Spacing);
        Assert.Equal(3, grid.RowSpacing);
        Assert.Equal(5, grid.ColumnSpacing);
        Assert.Equal(3, progress.Height);
    }

    [Fact]
    public void LegacyHeaderDetachesExistingContentBeforeReparentingIt()
    {
        var original = new StackPanel();
        var surface = new Border { Child = original };
        var replacement = new StackPanel();
        replacement.Children.Add(new TextBlock { Text = "Panel title" });

        OverlayThemeResources.ReplaceSurfaceContent(surface, replacement);

        Assert.Same(replacement, surface.Child);
        Assert.Equal(2, replacement.Children.Count);
        Assert.Same(original, replacement.Children[1]);

        var emptySurface = new Border();
        var emptyReplacement = new StackPanel();
        OverlayThemeResources.ReplaceSurfaceContent(
            emptySurface,
            emptyReplacement);
        Assert.Same(emptyReplacement, emptySurface.Child);
        Assert.Empty(emptyReplacement.Children);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
