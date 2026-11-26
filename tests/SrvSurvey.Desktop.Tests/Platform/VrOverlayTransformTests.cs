using System.Numerics;
using OVRSharp.Math;
using SrvSurvey.Desktop.Platform.Overlay;
using Valve.VR;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrOverlayTransformTests
{
    [Fact]
    public void PlaceInFrontUsesCurrentHeadingAndPositionWithoutRetainingOldTiltOrOversizedWidth()
    {
        var calibration = new VrOverlayCalibration(20, new Vector3(0, 23, 20), new Vector3(15, -30, 12));
        VrOverlayCalibration placed = VrOverlayTransform.PlaceInFront(
            calibration,
            new VrOverlayHeadPose(new Vector3(2, 1, 3), MathF.PI / 2)
        );
        Assert.Equal(8, placed.Scale);
        Assert.Equal(10, placed.Position.X, 4);
        Assert.Equal(10, placed.Position.Y, 4);
        Assert.Equal(-30, placed.Position.Z, 4);
        Assert.Equal(0, placed.Rotation.X);
        Assert.Equal(90, placed.Rotation.Y, 4);
        Assert.Equal(0, placed.Rotation.Z);
    }

    [Fact]
    public void PlaceInFrontPreservesSmallerWidthsAndSupportsTheLegacyOpenVrRotationOffset()
    {
        var calibration = new VrOverlayCalibration(3, Vector3.Zero, Vector3.Zero);
        VrOverlayCalibration placed = VrOverlayTransform.PlaceInFront(
            calibration,
            new VrOverlayHeadPose(Vector3.Zero, 0, MathF.PI / 2)
        );
        Assert.Equal(3, placed.Scale);
        Assert.Equal(new Vector3(0, 0, 10), placed.Position);
        Assert.Equal(90, placed.Rotation.Y, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void PitchedAndRolledHeadsetPlacesThePanelAlongItsForwardHeadingWithOriginCompensation(float offsetDegrees)
    {
        var matrix = new HmdMatrix34_t
        {
            m0 = 0.7071068f,
            m1 = -0.7071068f,
            m2 = 0,
            m3 = 2,
            m4 = 0.5f,
            m5 = 0.5f,
            m6 = -0.7071068f,
            m7 = 1,
            m8 = 0.5f,
            m9 = 0.5f,
            m10 = 0.7071068f,
            m11 = 3,
        };
        float offset = offsetDegrees * MathF.PI / 180;
        var origin = Matrix4x4.CreateRotationY(offset);
        VrOverlayHeadPose pose = VrOverlayTransform.FromOpenVrHeadPose(matrix, offset, origin);
        VrOverlayCalibration placed = VrOverlayTransform.PlaceInFront(new(8, Vector3.Zero, Vector3.Zero), pose);
        Matrix4x4 world = VrOverlayTransform.Create(placed, offset, origin);
        Assert.Equal(2, world.M41, 4);
        Assert.Equal(1, world.M42, 4);
        Assert.Equal(2, world.M43, 4);
        Assert.Equal(0.8, world.M11, 4);
        Assert.Equal(0, world.M13, 4);
    }

    [Fact]
    public void LegacyScaleAndCoordinateConversionArePreserved()
    {
        var calibration = new VrOverlayCalibration(20, new Vector3(10, 20, 30), Vector3.Zero);

        Matrix4x4 matrix = VrOverlayTransform.Create(calibration, 0, Matrix4x4.Identity);

        Assert.Equal(2, matrix.M11, 5);
        Assert.Equal(2, matrix.M22, 5);
        Assert.Equal(2, matrix.M33, 5);
        Assert.Equal(1, matrix.M41, 5);
        Assert.Equal(2, matrix.M42, 5);
        Assert.Equal(-3, matrix.M43, 5);
    }

    [Fact]
    public void HeadsetYawIsExtractedUsingTheLegacyMatrixAxes()
    {
        var matrix = new HmdMatrix34_t { m0 = 0, m8 = -1 };

        Assert.Equal(MathF.PI / 2, VrOverlayTransform.ExtractYaw(matrix), 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(-60)]
    [InlineData(135)]
    public void PlaceCalibrationControlsCentersAndFacesPlayerCameraOnOpenXr(float yawDegrees)
    {
        float yaw = yawDegrees * MathF.PI / 180f;
        var headPosition = new Vector3(0.4f, 1.3f, -0.2f);
        var pose = new VrOverlayHeadPose(headPosition, yaw);

        VrOverlayCalibration calibration = VrOverlayTransform.PlaceCalibrationControls(pose);
        Assert.Equal(8f, calibration.Scale);

        var panelPosition = new Vector3(
            calibration.Position.X / 10f,
            calibration.Position.Y / 10f,
            -calibration.Position.Z / 10f
        );
        Vector3 expectedForward = new(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        Vector3 expectedPosition = headPosition + expectedForward + new Vector3(0f, -0.5f, 0f);
        Assert.Equal(expectedPosition.X, panelPosition.X, 4);
        Assert.Equal(expectedPosition.Y, panelPosition.Y, 4);
        Assert.Equal(expectedPosition.Z, panelPosition.Z, 4);

        Vector3 radians = calibration.Rotation * (MathF.PI / 180f);
        var orientation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
        var normal = Vector3.Transform(Vector3.UnitZ, orientation);
        var right = Vector3.Transform(Vector3.UnitX, orientation);
        var toCamera = Vector3.Normalize(headPosition - panelPosition);

        Assert.Equal(toCamera.X, normal.X, 4);
        Assert.Equal(toCamera.Y, normal.Y, 4);
        Assert.Equal(toCamera.Z, normal.Z, 4);
        Assert.Equal(0f, right.Y, 4);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 0)]
    [InlineData(-60, 0)]
    [InlineData(135, 90)]
    [InlineData(-35, 90)]
    public void PlaceCalibrationControlsAndPlaceInFrontFacePlayerCameraOnOpenVr(float yawDegrees, float offsetDegrees)
    {
        float yaw = yawDegrees * MathF.PI / 180f;
        float offset = offsetDegrees * MathF.PI / 180f;
        var headPosition = new Vector3(0.5f, 1.4f, -0.3f);
        // Column-vector OpenVR HMD pose rotated by +yaw around +Y.
        var hmdMatrix = new HmdMatrix34_t
        {
            m0 = MathF.Cos(yaw),
            m2 = MathF.Sin(yaw),
            m3 = headPosition.X,
            m5 = 1f,
            m7 = headPosition.Y,
            m8 = -MathF.Sin(yaw),
            m10 = MathF.Cos(yaw),
            m11 = headPosition.Z,
        };
        var origin = Matrix4x4.CreateRotationY(offset);
        VrOverlayHeadPose pose = VrOverlayTransform.FromOpenVrHeadPose(hmdMatrix, offset, origin);

        VrOverlayCalibration controls = VrOverlayTransform.PlaceCalibrationControls(pose);
        var controlsWorld = VrOverlayTransform.Create(controls, offset, origin).ToHmdMatrix34_t();
        var panelPosition = new Vector3(controlsWorld.m3, controlsWorld.m7, controlsWorld.m11);
        Vector3 expectedPosition = headPosition + new Vector3(-MathF.Sin(yaw), -0.5f, -MathF.Cos(yaw));
        Assert.Equal(expectedPosition.X, panelPosition.X, 4);
        Assert.Equal(expectedPosition.Y, panelPosition.Y, 4);
        Assert.Equal(expectedPosition.Z, panelPosition.Z, 4);

        var normal = Vector3.Normalize(new Vector3(controlsWorld.m2, controlsWorld.m6, controlsWorld.m10));
        var toCamera = Vector3.Normalize(headPosition - panelPosition);
        Assert.Equal(toCamera.X, normal.X, 4);
        Assert.Equal(toCamera.Y, normal.Y, 4);
        Assert.Equal(toCamera.Z, normal.Z, 4);
        Assert.Equal(0f, controlsWorld.m4, 4);

        // PlaceInFront also faces the player camera directly for non-zero head yaw on OpenVR.
        VrOverlayCalibration inFront = VrOverlayTransform.PlaceInFront(new(8, Vector3.Zero, Vector3.Zero), pose);
        var inFrontWorld = VrOverlayTransform.Create(inFront, offset, origin).ToHmdMatrix34_t();
        var inFrontNormal = Vector3.Normalize(new Vector3(inFrontWorld.m2, inFrontWorld.m6, inFrontWorld.m10));
        var expectedHorizontalNormal = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        Assert.Equal(expectedHorizontalNormal.X, inFrontNormal.X, 4);
        Assert.Equal(expectedHorizontalNormal.Y, inFrontNormal.Y, 4);
        Assert.Equal(expectedHorizontalNormal.Z, inFrontNormal.Z, 4);

        // Snapped 45-degree placement on OpenVR also faces the head with zero roll even with yaw offset.
        float snappedYaw = MathF.PI / 4f;
        VrOverlayCalibration snappedControls = VrOverlayTransform.PlaceCalibrationControls(pose, snappedYaw);
        var snappedWorld = VrOverlayTransform.Create(snappedControls, offset, origin).ToHmdMatrix34_t();
        var snappedPos = new Vector3(snappedWorld.m3, snappedWorld.m7, snappedWorld.m11);
        var snappedNormal = Vector3.Normalize(new Vector3(snappedWorld.m2, snappedWorld.m6, snappedWorld.m10));
        var snappedToCamera = Vector3.Normalize(headPosition - snappedPos);
        Assert.Equal(snappedToCamera.X, snappedNormal.X, 4);
        Assert.Equal(snappedToCamera.Y, snappedNormal.Y, 4);
        Assert.Equal(snappedToCamera.Z, snappedNormal.Z, 4);
        Assert.Equal(0f, snappedWorld.m4, 4);
    }

    [Theory]
    [InlineData(0f, null, 0f)]
    [InlineData(20f, null, 0f)]
    [InlineData(25f, null, 45f)]
    [InlineData(-40f, null, -45f)]
    [InlineData(80f, null, 90f)]
    [InlineData(140f, null, 90f)]
    [InlineData(-140f, null, -90f)]
    [InlineData(29f, 0f, 0f)]
    [InlineData(-29f, 0f, 0f)]
    [InlineData(31f, 0f, 45f)]
    [InlineData(-31f, 0f, -45f)]
    [InlineData(16f, 45f, 45f)]
    [InlineData(14f, 45f, 0f)]
    [InlineData(74f, 45f, 45f)]
    [InlineData(76f, 45f, 90f)]
    public void SnapCalibrationYawUsesFiveFortyFiveDegreeSlotsWithHysteresis(
        float rawDegrees,
        float? currentDegrees,
        float expectedDegrees
    )
    {
        float rawRadians = rawDegrees * MathF.PI / 180f;
        float? currentRadians = currentDegrees * MathF.PI / 180f;

        float actualDegrees = VrOverlayTransform.SnapCalibrationYaw(rawRadians, currentRadians) * 180f / MathF.PI;

        Assert.Equal(expectedDegrees, actualDegrees, 3);
    }

    [Fact]
    public void ApplyPlaneDragMovesAlongPanelLocalRightAndUpAxesOnOpenXrAndOpenVr()
    {
        var openXrPanel = new VrOverlayCalibration(8f, new Vector3(-6f, 0f, 9.5f), new Vector3(0f, 30f, 0f));
        VrOverlayCalibration movedXr = VrOverlayTransform.ApplyPlaneDrag(
            openXrPanel,
            deltaRightUnits: 2f,
            deltaUpUnits: 1.5f,
            usesLegacyOpenVrRotation: false
        );
        Assert.Equal(-6f + 2f * MathF.Cos(MathF.PI / 6f), movedXr.Position.X, 4);
        Assert.Equal(1.5f, movedXr.Position.Y, 4);
        Assert.Equal(9.5f + 2f * MathF.Sin(MathF.PI / 6f), movedXr.Position.Z, 4);

        // On legacy OpenVR, -30 deg in calibration produces the same +30 deg physical panel orientation.
        var openVrPanel = new VrOverlayCalibration(8f, new Vector3(-6f, 0f, 9.5f), new Vector3(0f, -30f, 0f));
        VrOverlayCalibration movedVr = VrOverlayTransform.ApplyPlaneDrag(
            openVrPanel,
            deltaRightUnits: 2f,
            deltaUpUnits: 1.5f,
            usesLegacyOpenVrRotation: true
        );
        Assert.Equal(movedXr.Position.X, movedVr.Position.X, 4);
        Assert.Equal(movedXr.Position.Y, movedVr.Position.Y, 4);
        Assert.Equal(movedXr.Position.Z, movedVr.Position.Z, 4);
    }

    [Fact]
    public void ApplyDepthStepMovesAlongPanelNormalOnOpenXrAndOpenVr()
    {
        // At yaw 0, positive depth increases Position.Z directly away from the viewer.
        var frontPanel = new VrOverlayCalibration(8f, new Vector3(0f, 2f, 10f), Vector3.Zero);
        VrOverlayCalibration pushedFront = VrOverlayTransform.ApplyDepthStep(
            frontPanel,
            deltaDepthUnits: 1.5f,
            usesLegacyOpenVrRotation: false
        );
        Assert.Equal(0f, pushedFront.Position.X, 4);
        Assert.Equal(2f, pushedFront.Position.Y, 4);
        Assert.Equal(11.5f, pushedFront.Position.Z, 4);

        // At +90 deg OpenXR yaw (left side panel facing +X), positive depth moves away along -X without drifting Z.
        var sideXr = new VrOverlayCalibration(8f, new Vector3(-5f, 2f, 0f), new Vector3(0f, 90f, 0f));
        VrOverlayCalibration pushedSideXr = VrOverlayTransform.ApplyDepthStep(
            sideXr,
            deltaDepthUnits: 2f,
            usesLegacyOpenVrRotation: false
        );
        Assert.Equal(-7f, pushedSideXr.Position.X, 4);
        Assert.Equal(2f, pushedSideXr.Position.Y, 4);
        Assert.Equal(0f, pushedSideXr.Position.Z, 4);

        // Legacy OpenVR uses -90 deg for the same physical orientation and must move identically.
        var sideVr = new VrOverlayCalibration(8f, new Vector3(-5f, 2f, 0f), new Vector3(0f, -90f, 0f));
        VrOverlayCalibration pushedSideVr = VrOverlayTransform.ApplyDepthStep(
            sideVr,
            deltaDepthUnits: 2f,
            usesLegacyOpenVrRotation: true
        );
        Assert.Equal(pushedSideXr.Position.X, pushedSideVr.Position.X, 4);
        Assert.Equal(pushedSideXr.Position.Y, pushedSideVr.Position.Y, 4);
        Assert.Equal(pushedSideXr.Position.Z, pushedSideVr.Position.Z, 4);
    }

    [Theory]
    [InlineData(0.6f)]
    [InlineData(-1.1f)]
    [InlineData(MathF.PI / 2f)]
    public void LegacyOpenVrDragAndDepthStayAlignedWithRenderedPanelPlaneAfterRecentering(float yawOffset)
    {
        var original = new VrOverlayCalibration(6f, new Vector3(4.5f, 14.2f, 9.8f), new Vector3(25f, -35f, 18f));
        var origin = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yawOffset);
        var beforeWorld = VrOverlayTransform.Create(original, yawOffset, origin).ToHmdMatrix34_t();
        var beforePos = new Vector3(beforeWorld.m3, beforeWorld.m7, beforeWorld.m11);
        var renderedRight = Vector3.Normalize(new Vector3(beforeWorld.m0, beforeWorld.m4, beforeWorld.m8));
        var renderedUp = Vector3.Normalize(new Vector3(beforeWorld.m1, beforeWorld.m5, beforeWorld.m9));
        var renderedNormal = Vector3.Normalize(new Vector3(beforeWorld.m2, beforeWorld.m6, beforeWorld.m10));

        // In-plane drag moves strictly along the rendered quad's right/up axes with zero drift along its normal.
        VrOverlayCalibration dragged = VrOverlayTransform.ApplyPlaneDrag(
            original,
            deltaRightUnits: 2.5f,
            deltaUpUnits: -1.75f,
            usesLegacyOpenVrRotation: true,
            openVrYawOffset: yawOffset
        );
        var draggedWorld = VrOverlayTransform.Create(dragged, yawOffset, origin).ToHmdMatrix34_t();
        Vector3 draggedDeltaMeters = new Vector3(draggedWorld.m3, draggedWorld.m7, draggedWorld.m11) - beforePos;
        Assert.Equal(0.25f, Vector3.Dot(draggedDeltaMeters, renderedRight), 4);
        Assert.Equal(-0.175f, Vector3.Dot(draggedDeltaMeters, renderedUp), 4);
        Assert.Equal(0f, Vector3.Dot(draggedDeltaMeters, renderedNormal), 4);

        // Depth step moves strictly along -renderedNormal with zero sideways or vertical drift.
        VrOverlayCalibration pushed = VrOverlayTransform.ApplyDepthStep(
            original,
            deltaDepthUnits: 3f,
            usesLegacyOpenVrRotation: true,
            openVrYawOffset: yawOffset
        );
        var pushedWorld = VrOverlayTransform.Create(pushed, yawOffset, origin).ToHmdMatrix34_t();
        Vector3 pushedDeltaMeters = new Vector3(pushedWorld.m3, pushedWorld.m7, pushedWorld.m11) - beforePos;
        Assert.Equal(0f, Vector3.Dot(pushedDeltaMeters, renderedRight), 4);
        Assert.Equal(0f, Vector3.Dot(pushedDeltaMeters, renderedUp), 4);
        Assert.Equal(-0.3f, Vector3.Dot(pushedDeltaMeters, renderedNormal), 4);
    }
}
