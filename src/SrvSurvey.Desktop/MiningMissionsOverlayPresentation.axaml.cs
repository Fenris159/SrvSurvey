using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class MiningMissionsOverlayPresentation
    : UserControl,
        IOverlayPanelSizePolicy,
        IOverlayPanelViewportAware
{
    private bool viewportUpdatePending;
    public bool AutoWidth => true;

    public bool AutoHeight => true;

    void IOverlayPanelViewportAware.SetAvailableHeight(double height) => MissionPanel.MaxHeight = height;

    public MiningMissionsOverlayPresentation()
    {
        InitializeComponent();
        DestinationItems.LayoutUpdated += (_, _) => ScheduleViewportUpdate();
        AttachedToVisualTree += (_, _) => ScheduleViewportUpdate();
    }

    private void ScheduleViewportUpdate()
    {
        if (viewportUpdatePending || !this.IsAttachedToVisualTree())
        {
            return;
        }
        viewportUpdatePending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                viewportUpdatePending = false;
                UpdateViewport();
            },
            DispatcherPriority.Loaded
        );
    }

    /// <summary>Counts full destination cards at their rendered heights, including every commodity row.</summary>
    private void UpdateViewport()
    {
        if (DataContext is not MiningMissionsOverlayViewModel model)
        {
            return;
        }
        double height = double.PositiveInfinity;
        if (model.Destinations.Count > model.VisibleDestinationLimit)
        {
            height = 0;
            for (int index = 0; index < model.VisibleDestinationLimit; index++)
            {
                if (DestinationItems.ContainerFromIndex(index) is not Control container || container.Bounds.Height <= 0)
                {
                    return;
                }
                height += container.Bounds.Height;
            }
            height = Math.Ceiling(height);
        }
        if (
            double.IsPositiveInfinity(DestinationScroller.MaxHeight) != double.IsPositiveInfinity(height)
            || double.IsFinite(height) && Math.Abs(DestinationScroller.MaxHeight - height) > 0.5
        )
        {
            DestinationScroller.MaxHeight = height;
        }
    }
}
