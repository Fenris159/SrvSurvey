using System.ComponentModel;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Keeps the Canonn panel switch, availability editor and shortcut on one persisted state.</summary>
internal sealed class PriorScansPanelAvailabilityBinding : IDisposable
{
    private readonly SystemSurveyViewModel survey;
    private readonly OverlayPanelVisibilityViewModel availability;
    private readonly OverlayPanelVisibilityEntryViewModel panel;

    public PriorScansPanelAvailabilityBinding(
        SystemSurveyViewModel survey,
        OverlayPanelVisibilityViewModel availability
    )
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(availability);
        this.survey = survey;
        this.availability = availability;
        panel = availability.Panels.Single(item => item.PlotterName == "PlotPriorScans");
        if (availability.HasSavedVisibility(panel.PlotterName))
        {
            survey.ShowCanonnSignalsOnRadar = panel.IsEnabled;
        }
        else
        {
            panel.IsEnabled = survey.ShowCanonnSignalsOnRadar;
        }
        survey.PropertyChanged += OnSurveyChanged;
        availability.AvailabilityChanged += OnPanelChanged;
    }

    public void Dispose()
    {
        survey.PropertyChanged -= OnSurveyChanged;
        availability.AvailabilityChanged -= OnPanelChanged;
    }

    private void OnSurveyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemSurveyViewModel.ShowCanonnSignalsOnRadar))
        {
            panel.IsEnabled = survey.ShowCanonnSignalsOnRadar;
        }
    }

    private void OnPanelChanged(OverlayPanelVisibilityEntryViewModel changed)
    {
        if (ReferenceEquals(changed, panel))
        {
            survey.ShowCanonnSignalsOnRadar = panel.IsEnabled;
        }
    }
}
