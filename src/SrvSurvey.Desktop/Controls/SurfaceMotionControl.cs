using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Animates only visible navigation drawings; authoritative view-model state stays untouched.</summary>
public abstract class SurfaceMotionControl : Control
{
    public static readonly StyledProperty<EliteStatus?> MotionStatusProperty = AvaloniaProperty.Register<
        SurfaceMotionControl,
        EliteStatus?
    >(nameof(MotionStatus));

    private readonly SurfaceVisualMotion motion = new();
    private bool framePending;
    private int attachment;

    internal TimeProvider MotionClock { get; set; } = TimeProvider.System;

    public EliteStatus? MotionStatus
    {
        get => GetValue(MotionStatusProperty);
        set => SetValue(MotionStatusProperty, value);
    }

    protected SurfaceVisualPose? VisualPose { get; private set; }

    private TimeSpan Now => MotionClock.GetElapsedTime(0);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MotionStatusProperty)
        {
            motion.Update(MotionStatus, Now);
            InvalidateVisual();
            ScheduleFrame();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        motion.Update(null, Now);
        motion.Update(MotionStatus, Now);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attachment++;
        framePending = false;
        motion.Update(null, Now);
        base.OnDetachedFromVisualTree(e);
    }

    protected void BeginMotionFrame()
    {
        VisualPose = motion.GetPose(Now);
        ScheduleFrame();
    }

    protected double VisualHeading(double referenceHeading)
    {
        return VisualPose is { } pose && MotionStatus is { } status
            ? SurfaceNavigation.NormalizeDegrees(referenceHeading + pose.Heading - status.NormalizedHeading)
            : referenceHeading;
    }

    protected (double X, double Y) VisualDisplacement(double referenceHeading)
    {
        if (VisualPose is not { } pose || MotionStatus is not { } status)
        {
            return (0, 0);
        }

        (double east, double north) = SurfaceVisualMotion.GetOffset(
            new SurfaceCoordinate(status.Latitude, status.Longitude),
            pose.Location,
            (double)status.PlanetRadius
        );
        double angle = (status.NormalizedHeading - referenceHeading) * Math.PI / 180;
        return (east * Math.Cos(angle) - north * Math.Sin(angle), east * Math.Sin(angle) + north * Math.Cos(angle));
    }

    protected (double X, double Y) VisualRelativePoint(double bearing, double distance)
    {
        double angle = bearing * Math.PI / 180;
        (double x, double y) = VisualDisplacement(0);
        double rotation = VisualHeading(0) * Math.PI / 180;
        double east = Math.Sin(angle) * distance - x;
        double north = Math.Cos(angle) * distance - y;
        return (
            east * Math.Cos(rotation) - north * Math.Sin(rotation),
            east * Math.Sin(rotation) + north * Math.Cos(rotation)
        );
    }

    private void ScheduleFrame()
    {
        if (
            !framePending
            && this.IsEffectivelyVisible
            && motion.IsAnimating(Now)
            && TopLevel.GetTopLevel(this) is { } top
        )
        {
            framePending = true;
            int epoch = attachment;
            top.RequestAnimationFrame(_ => OnFrame(epoch));
        }
    }

    private void OnFrame(int epoch)
    {
        if (epoch != attachment)
        {
            return;
        }
        framePending = false;
        if (this.IsEffectivelyVisible)
        {
            InvalidateVisual();
            ScheduleFrame();
        }
    }
}
