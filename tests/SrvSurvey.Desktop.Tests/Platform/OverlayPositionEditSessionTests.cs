using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayPositionEditSessionTests
{
    [Fact]
    public void CatalogGroupsEveryPositionableOverlayExactlyOnce()
    {
        Assert.Equal(7, OverlayLayoutCatalog.Categories.Count);
        Assert.Equal(37, OverlayLayoutCatalog.Supported.Count);
        Assert.Equal(
            OverlayLayoutCatalog.Supported.Count,
            OverlayLayoutCatalog
                .Supported.Select(definition => definition.Name)
                .Distinct(StringComparer.Ordinal)
                .Count()
        );

        foreach (OverlayLayoutCategoryDefinition category in OverlayLayoutCatalog.Categories)
        {
            IReadOnlyList<OverlayLayoutDefinition> definitions = OverlayLayoutCatalog.ForCategory(category.Category);
            Assert.NotEmpty(definitions);
            Assert.All(
                definitions,
                definition =>
                {
                    Assert.Equal(category.Category, definition.Category);
                    Assert.True(definition.PreviewSize.Width > 0);
                    Assert.True(definition.PreviewSize.Height > 0);
                }
            );
        }
    }

    [Fact]
    public void GuardianOverlaysHaveTheirOwnEditorCategory()
    {
        Assert.Equal(
            ["PlotGuardians", "PlotGuardianStatus", "PlotGuardianSystem", "PlotRamTah"],
            OverlayLayoutCatalog.ForCategory(OverlayLayoutCategory.Guardian).Select(definition => definition.Name)
        );
        Assert.DoesNotContain(
            OverlayLayoutCatalog.ForCategory(OverlayLayoutCategory.SitesAndQuests),
            definition =>
                definition.Name.StartsWith("PlotGuardian", StringComparison.Ordinal) || definition.Name == "PlotRamTah"
        );
    }

    [Fact]
    public void MovesRemainIsolatedFromTheActiveLayoutUntilCommittedElsewhere()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBioStatus"] = new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, 0.75),
            },
            0.65,
            null
        );
        var session = new OverlayPositionEditSession(active);
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotBioStatus"
        );
        var bounds = new PixelRect(100, 200, 1200, 800);
        var destination = new PixelPoint(530, 360);

        Assert.True(session.Move(definition.Name, destination, definition.PreviewSize, bounds));

        Assert.True(session.HasChanges);
        Assert.Single(session.Changes);
        Assert.Equal(destination, session.GetPosition(definition.Name, bounds, definition.PreviewSize));
        Assert.Equal(new PixelPoint(570, 208), active.GetPosition(definition.Name, bounds, definition.PreviewSize));
        Assert.Equal(0.75, session.GetPlacement(definition.Name).Opacity);
    }

    [Fact]
    public void OriginalPlacementRemainsAvailableAfterWorkingMove()
    {
        var original = new LegacyOverlayPlacement(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, 0.75);
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement> { ["PlotBioStatus"] = original },
            0.65,
            null
        );
        var session = new OverlayPositionEditSession(active);

        Assert.True(
            session.Move(
                "PlotBioStatus",
                new PixelPoint(530, 360),
                new PixelSize(480, 80),
                new PixelRect(100, 200, 1200, 800)
            )
        );

        Assert.Equal(original, session.GetOriginalPlacement("PlotBioStatus"));
        Assert.NotEqual(original, session.GetPlacement("PlotBioStatus"));
    }

    [Fact]
    public void DraggingRestoresThePanelsStableDefaultAnchors()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Center, -28, LegacyVerticalAnchor.Middle, -862, 0.7),
            },
            null,
            null
        );
        var session = new OverlayPositionEditSession(active);
        var bounds = new PixelRect(100, 200, 1200, 800);
        var size = new PixelSize(600, 100);
        var destination = new PixelPoint(380, 240);

        Assert.True(session.Move("PlotJumpInfo", destination, size, bounds));

        LegacyOverlayPlacement placement = session.GetPlacement("PlotJumpInfo");
        Assert.Equal(LegacyHorizontalAnchor.Center, placement.Horizontal);
        Assert.Equal(LegacyVerticalAnchor.Top, placement.Vertical);
        Assert.Equal(destination, session.GetPosition("PlotJumpInfo", bounds, size));
        Assert.Equal(
            destination.Y,
            session.GetPosition("PlotJumpInfo", bounds, new PixelSize(size.Width, size.Height + 18)).Y
        );
    }

    [Fact]
    public void CenteringWithDefaultAnchorsKeepsDynamicPaneTopEdgeStable()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Left, 315, LegacyVerticalAnchor.Top, 470, 0.7),
                ["PlotGuardians"] = new(LegacyHorizontalAnchor.Right, 40, LegacyVerticalAnchor.Bottom, 60, 0.8),
            },
            null,
            null
        );
        var session = new OverlayPositionEditSession(active);
        var bounds = new PixelRect(100, 200, 1200, 800);
        var size = new PixelSize(600, 100);
        var center = new PixelPoint(400, 550);

        Assert.True(session.MoveWithDefaultAnchors("PlotJumpInfo", center, size, bounds));

        LegacyOverlayPlacement placement = session.GetPlacement("PlotJumpInfo");
        Assert.Equal(LegacyHorizontalAnchor.Center, placement.Horizontal);
        Assert.Equal(0, placement.HorizontalOffset);
        Assert.Equal(LegacyVerticalAnchor.Top, placement.Vertical);
        Assert.Equal(350, placement.VerticalOffset);
        Assert.Equal(0.7, placement.Opacity);
        Assert.Equal(active.Placements["PlotGuardians"], session.GetPlacement("PlotGuardians"));
        Assert.Equal(center, session.GetPosition("PlotJumpInfo", bounds, size));
        Assert.Equal(center, session.GetPosition("PlotJumpInfo", bounds, new PixelSize(600, 118)));
    }

    [Fact]
    public void ScaleTracksTheActiveSettingWithoutBecomingAnEditorChange()
    {
        var active = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        int originalIndex = OverlayScaleCatalog.GetIndex(50);
        int updatedIndex = OverlayScaleCatalog.GetIndex(150);
        active.SetScaleIndex(originalIndex);
        var session = new OverlayPositionEditSession(active);

        Assert.Equal(originalIndex, session.ScaleIndex);

        session.SetScaleIndex(updatedIndex);

        Assert.Equal(updatedIndex, session.ScaleIndex);
        Assert.Equal(originalIndex, active.ScaleIndex);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void PerOverlayScaleOverrideIsAnIsolatedEditorChange()
    {
        var active = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        int globalIndex = OverlayScaleCatalog.GetIndex(50);
        int customIndex = OverlayScaleCatalog.GetIndex(150);
        active.SetScaleIndex(globalIndex);
        var session = new OverlayPositionEditSession(active);

        Assert.True(session.SetScaleOverride("PlotRouteBio", customIndex));

        Assert.True(session.HasChanges);
        Assert.Equal(customIndex, session.GetScaleIndex("PlotRouteBio"));
        Assert.Equal(globalIndex, session.GetScaleIndex("PlotJumpInfo"));
        Assert.Null(active.Placements.GetValueOrDefault("PlotRouteBio")?.ScaleIndex);

        Assert.True(session.SetScaleOverride("PlotRouteBio", null));
        Assert.False(session.HasChanges);
        Assert.Equal(globalIndex, session.GetScaleIndex("PlotRouteBio"));
    }

    [Fact]
    public void PerOverlayTypographyIsAnIsolatedEditorChangeAndZeroRemovesTheOverride()
    {
        var active = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        var session = new OverlayPositionEditSession(active);
        var scale = new OverlayTypographyScale(0, 25, 0, -10, 0, 0);

        Assert.True(session.SetTypographyScale("PlotFSSInfo", scale));

        Assert.True(session.HasChanges);
        Assert.Equal(scale, session.GetTypographyScale("PlotFSSInfo"));
        Assert.Equal(OverlayTypographyScale.Default, session.GetTypographyScale("PlotJumpInfo"));
        Assert.Null(active.Placements.GetValueOrDefault("PlotFSSInfo")?.TypographyScale);

        Assert.True(session.SetTypographyScale("PlotFSSInfo", OverlayTypographyScale.Default));
        Assert.False(session.HasChanges);
        Assert.Null(session.GetPlacement("PlotFSSInfo").TypographyScale);
    }

    [Theory]
    [InlineData(OverlayTypographyRole.Header)]
    [InlineData(OverlayTypographyRole.Title)]
    [InlineData(OverlayTypographyRole.Value)]
    [InlineData(OverlayTypographyRole.Body)]
    [InlineData(OverlayTypographyRole.Detail)]
    [InlineData(OverlayTypographyRole.Caption)]
    public void TypographyRolesNormalizeIndependentPercentages(OverlayTypographyRole role)
    {
        OverlayTypographyScale updated = OverlayTypographyScale.Default.WithPercent(role, 13);

        Assert.Equal(15, updated.GetPercent(role));
        Assert.False(updated.IsDefault);
        Assert.Equal(-50, OverlayTypographyScale.Normalize(-80));
        Assert.Equal(0, OverlayTypographyScale.Normalize(double.NaN));
    }

    [Fact]
    public void GlobalAndPerOverlayOpacityChangesRemainInTheEditSession()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBioStatus"] = new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, null),
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Center, 0, LegacyVerticalAnchor.Top, 8, 0.75),
            },
            0.65,
            null
        );
        var session = new OverlayPositionEditSession(active);

        Assert.True(session.SetDefaultOpacity(0.5));
        Assert.True(session.SetOpacityOverride("PlotBioStatus", 0.8));
        Assert.True(session.SetOpacityOverride("PlotJumpInfo", null));

        Assert.True(session.HasChanges);
        Assert.True(session.HasDefaultOpacityChange);
        Assert.Equal(0.5, session.DefaultOpacity);
        Assert.Equal(0.8, session.GetOpacity("PlotBioStatus"));
        Assert.Equal(0.5, session.GetOpacity("PlotJumpInfo"));
        Assert.Equal(0.65, active.DefaultOpacity);
        Assert.Null(active.Placements["PlotBioStatus"].Opacity);
        Assert.Equal(0.75, active.Placements["PlotJumpInfo"].Opacity);
    }

    [Fact]
    public void ReturningAnImplicitFullOpacityDefaultClearsTheChange()
    {
        var session = new OverlayPositionEditSession(
            new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null)
        );

        Assert.True(session.SetDefaultOpacity(0.4));
        Assert.True(session.HasDefaultOpacityChange);
        Assert.True(session.SetDefaultOpacity(1d));

        Assert.False(session.HasDefaultOpacityChange);
        Assert.False(session.HasChanges);
        Assert.Equal(1d, session.DefaultOpacity);
    }
}
