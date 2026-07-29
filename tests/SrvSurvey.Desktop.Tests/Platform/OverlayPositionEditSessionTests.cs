using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayPositionEditSessionTests
{
    [Fact]
    public void CatalogGroupsEveryPositionableOverlayExactlyOnce()
    {
        Assert.Equal(5, OverlayLayoutCatalog.Categories.Count);
        Assert.Equal(26, OverlayLayoutCatalog.Supported.Count);
        Assert.Equal(
            OverlayLayoutCatalog.Supported.Count,
            OverlayLayoutCatalog.Supported
                .Select(definition => definition.Name)
                .Distinct(StringComparer.Ordinal)
                .Count());

        foreach (var category in OverlayLayoutCatalog.Categories)
        {
            var definitions = OverlayLayoutCatalog.ForCategory(category.Category);
            Assert.NotEmpty(definitions);
            Assert.All(definitions, definition =>
            {
                Assert.Equal(category.Category, definition.Category);
                Assert.True(definition.PreviewSize.Width > 0);
                Assert.True(definition.PreviewSize.Height > 0);
            });
        }
    }

    [Fact]
    public void MovesRemainIsolatedFromTheActiveLayoutUntilCommittedElsewhere()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBioStatus"] = new(
                    LegacyHorizontalAnchor.Center,
                    0,
                    LegacyVerticalAnchor.Top,
                    8,
                    0.75),
            },
            0.65,
            null);
        var session = new OverlayPositionEditSession(active);
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotBioStatus");
        var bounds = new PixelRect(100, 200, 1200, 800);
        var destination = new PixelPoint(530, 360);

        Assert.True(session.Move(
            definition.Name,
            destination,
            definition.PreviewSize,
            bounds));

        Assert.True(session.HasChanges);
        Assert.Single(session.Changes);
        Assert.Equal(
            destination,
            session.GetPosition(
                definition.Name,
                bounds,
                definition.PreviewSize));
        Assert.Equal(
            new PixelPoint(460, 208),
            active.GetPosition(
                definition.Name,
                bounds,
                definition.PreviewSize));
        Assert.Equal(0.75, session.GetPlacement(definition.Name).Opacity);
    }

    [Fact]
    public void GlobalAndPerOverlayOpacityChangesRemainInTheEditSession()
    {
        var active = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotBioStatus"] = new(
                    LegacyHorizontalAnchor.Center,
                    0,
                    LegacyVerticalAnchor.Top,
                    8,
                    null),
                ["PlotJumpInfo"] = new(
                    LegacyHorizontalAnchor.Center,
                    0,
                    LegacyVerticalAnchor.Top,
                    8,
                    0.75),
            },
            0.65,
            null);
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
            new LegacyOverlayLayout(
                new Dictionary<string, LegacyOverlayPlacement>(),
                null,
                null));

        Assert.True(session.SetDefaultOpacity(0.4));
        Assert.True(session.HasDefaultOpacityChange);
        Assert.True(session.SetDefaultOpacity(1d));

        Assert.False(session.HasDefaultOpacityChange);
        Assert.False(session.HasChanges);
        Assert.Equal(1d, session.DefaultOpacity);
    }
}
