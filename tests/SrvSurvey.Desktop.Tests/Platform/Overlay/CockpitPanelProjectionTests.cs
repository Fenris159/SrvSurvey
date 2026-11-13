using System.Numerics;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class CockpitPanelProjectionTests
{
    [Fact]
    public void EditorProjectionUsesTheGameViewportOriginAndCalibratedLensAtHighDpi()
    {
        Matrix transform = Assert.IsType<Matrix>(
            CockpitPanelProjection.CreateEditorPreview(
                new Rect(4200, 600, 600, 200),
                new Size(300, 100),
                new PixelRect(4000, 200, 2000, 2000),
                2,
                new CockpitPanelPose(Yaw: 45),
                new CockpitProjectionLens(1.25f, 2.5f, .1f)
            )
        );
        Point corner = transform.Transform(default);
        Assert.InRange(corner.X, 95.592482, 95.594482);
        Assert.InRange(corner.Y, 43.524214, 43.526214);
        Assert.Null(
            CockpitPanelProjection.CreateEditorPreview(
                new Rect(0, 0, 100, 100),
                new Size(100, 100),
                new PixelRect(0, 0, 1000, 1000),
                0,
                new CockpitPanelPose()
            )
        );
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    public void EditorProjectionKeepsCoordinateSpacesConsistent(double dpi, double panelScale)
    {
        Matrix transform = Assert.IsType<Matrix>(
            CockpitPanelProjection.CreateEditorPreview(
                new Rect(100 * dpi, 200 * dpi, 300 * panelScale * dpi, 100 * panelScale * dpi),
                new Size(300, 100),
                new PixelRect(0, 0, (int)(1000 * dpi), (int)(1000 * dpi)),
                dpi,
                new CockpitPanelPose(Roll: 90)
            )
        );
        AssertPoint(new Point(100, 200), transform.Transform(default));
        AssertPoint(new Point(200, -100), transform.Transform(new Point(300, 100)));
    }

    [Fact]
    public void MovingAPanelTwiceAsFarHalvesItsSizeAroundItsSavedCenter()
    {
        Matrix projection = Assert.IsType<Matrix>(
            CockpitPanelProjection.Create(
                new Rect(100, 200, 300, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(Depth: 2),
                Matrix4x4.Identity
            )
        );
        AssertPoint(new Point(250, 250), projection.Transform(new Point(150, 50)));
        AssertPoint(new Point(175, 225), projection.Transform(new Point(0, 0)));
        AssertPoint(new Point(325, 275), projection.Transform(new Point(300, 100)));
    }

    [Fact]
    public void CenteredViewPreservesEverySavedDesktopCorner()
    {
        Matrix transform = Assert.IsType<Matrix>(
            CockpitPanelProjection.Create(
                new Rect(100, 200, 300, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(),
                Matrix4x4.Identity
            )
        );

        AssertPoint(new Point(100, 200), transform.Transform(new Point(0, 0)));
        AssertPoint(new Point(400, 200), transform.Transform(new Point(300, 0)));
        AssertPoint(new Point(400, 300), transform.Transform(new Point(300, 100)));
        AssertPoint(new Point(100, 300), transform.Transform(new Point(0, 100)));
    }

    [Fact]
    public void PanelRollRotatesAroundItsOwnCenter()
    {
        Matrix transform = Assert.IsType<Matrix>(
            CockpitPanelProjection.Create(
                new Rect(100, 200, 300, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(Roll: 90),
                Matrix4x4.Identity
            )
        );

        AssertPoint(new Point(250, 250), transform.Transform(new Point(150, 50)));
        AssertPoint(new Point(200, 400), transform.Transform(new Point(0, 0)));
        AssertPoint(new Point(300, 100), transform.Transform(new Point(300, 100)));
    }

    [Fact]
    public void TiltChangesPerspectiveRatherThanOnlySkewingOrMovingThePanel()
    {
        Matrix transform = Assert.IsType<Matrix>(
            CockpitPanelProjection.Create(
                new Rect(300, 450, 400, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(Yaw: 45),
                Matrix4x4.Identity
            )
        );

        Point upperLeft = transform.Transform(new Point(0, 0));
        Point lowerLeft = transform.Transform(new Point(0, 100));
        Point upperRight = transform.Transform(new Point(400, 0));
        Point lowerRight = transform.Transform(new Point(400, 100));
        AssertPoint(new Point(500, 500), transform.Transform(new Point(200, 50)));
        Assert.InRange(transform.M13, -0.00078, -0.00076);
        Assert.InRange(lowerLeft.Y - upperLeft.Y, 86.6, 86.8);
        Assert.InRange(lowerRight.Y - upperRight.Y, 118.1, 118.3);
    }

    [Fact]
    public void PanelsBehindTheCameraAreCulled()
    {
        Matrix4x4 behind = Matrix4x4.Identity;
        behind.M44 = -1;
        Assert.Null(
            CockpitPanelProjection.Create(
                new Rect(0, 0, 100, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(),
                behind
            )
        );
    }

    [Fact]
    public void ExtractsTheActualGameLensFromARotatedRigidCockpitProjection()
    {
        var projection = new Matrix4x4(1.25f, 0, 0, 0, 0, 2.5f, 0, 0, 0, 0, 0, 1, 0, 0, 0.1f, 0);
        Matrix4x4 rigid = Matrix4x4.CreateRotationY(MathF.PI / 3) * Matrix4x4.CreateTranslation(4, -2, 3);
        CockpitProjectionLens lens = Assert.IsType<CockpitProjectionLens>(
            CockpitProjectionLens.FromRigidProjection(rigid * projection)
        );
        Assert.InRange(lens.HorizontalFocalScale, 1.24999f, 1.25001f);
        Assert.InRange(lens.VerticalFocalScale, 2.49999f, 2.50001f);
        Assert.InRange(lens.NearDepth, 0.09999f, 0.10001f);
        Assert.Null(CockpitProjectionLens.FromRigidProjection(new Matrix4x4()));

        Matrix transform = Assert.IsType<Matrix>(
            CockpitPanelProjection.Create(
                new Rect(100, 200, 300, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(),
                Matrix4x4.Identity,
                lens
            )
        );
        AssertPoint(new Point(100, 200), transform.Transform(new Point(0, 0)));
        AssertPoint(new Point(400, 300), transform.Transform(new Point(300, 100)));
        Assert.Null(
            CockpitPanelProjection.Create(
                new Rect(100, 200, 300, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(),
                Matrix4x4.Identity,
                new CockpitProjectionLens(0, 1, 1)
            )
        );
    }

    [Theory]
    [InlineData(double.NaN, 0, 0, 1)]
    [InlineData(0, 86, 0, 1)]
    [InlineData(0, 0, 181, 1)]
    [InlineData(0, 0, 0, 0.24)]
    [InlineData(0, 0, 0, 5.01)]
    public void InvalidPosesCannotProduceAProjection(double yaw, double pitch, double roll, double depth)
    {
        Assert.Null(
            CockpitPanelProjection.Create(
                new Rect(0, 0, 100, 100),
                new Size(1000, 1000),
                new CockpitPanelPose(yaw, pitch, roll, depth),
                Matrix4x4.Identity
            )
        );
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.InRange(actual.X, expected.X - 0.001, expected.X + 0.001);
        Assert.InRange(actual.Y, expected.Y - 0.001, expected.Y + 0.001);
    }
}
