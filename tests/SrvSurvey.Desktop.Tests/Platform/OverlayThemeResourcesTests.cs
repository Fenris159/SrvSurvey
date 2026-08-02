using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

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
        Assert.Equal(new Thickness(5), surface.Padding);
        Assert.Same(Brushes.Black, surface.Background);
        Assert.Null(surface.BorderBrush);
        Assert.Equal(new Thickness(0), surface.BorderThickness);
        Assert.Equal(new CornerRadius(5), surface.CornerRadius);
        Assert.Equal(1d, surface.Opacity);
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
        Assert.Equal(new Thickness(5), surface.Padding);
        Assert.Same(Brushes.Black, surface.Background);
        Assert.Same(Brushes.Yellow, surface.BorderBrush);
        Assert.Equal(new Thickness(2), surface.BorderThickness);
        Assert.Equal(new CornerRadius(5), surface.CornerRadius);
        Assert.Equal(0.42, surface.Opacity);
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
        Assert.Equal(28, OverlayLayoutCatalog.Supported.Count);
        Assert.All(OverlayLayoutCatalog.Supported, definition =>
            Assert.Equal(
                definition.PreviewSize.Width,
                OverlayThemeResources.GetLegacyFormFactorWidth(
                    definition.Name)));
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

        OverlayThemeResources.NormalizeLegacyOverlayControl(heading, root);
        OverlayThemeResources.NormalizeLegacyOverlayControl(stack, root);
        OverlayThemeResources.NormalizeLegacyOverlayControl(grid, root);

        Assert.False(heading.IsVisible);
        Assert.Equal(12, heading.FontSize);
        Assert.Equal(3, stack.Spacing);
        Assert.Equal(3, grid.RowSpacing);
        Assert.Equal(5, grid.ColumnSpacing);
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
    }
}
