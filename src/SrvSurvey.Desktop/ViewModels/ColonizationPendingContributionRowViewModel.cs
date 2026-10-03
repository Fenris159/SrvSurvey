using System.ComponentModel;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>A separately selectable delivery whose uncertain Raven credit requires manual verification.</summary>
public sealed class ColonizationPendingContributionRowViewModel : INotifyPropertyChanged
{
    private readonly Action selectionChanged;
    private bool isSelected;

    /// <summary>Identifies one retained journal delivery and starts it unchecked so it cannot be replayed implicitly.</summary>
    public ColonizationPendingContributionRowViewModel(
        ColonizationPendingContribution contribution,
        Action selectionChanged
    )
    {
        EventId = contribution.EventId;
        Summary =
            $"{contribution.Commander}: {contribution.Cargo.Values.Sum(value => (long)value):N0} units for {contribution.BuildId}: {string.Join(", ", contribution.Cargo.Select(pair => $"{pair.Key} {pair.Value:N0}"))} (unconfirmed).";
        this.selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Distinguishes separate deliveries even when they contain identical cargo for the same project.</summary>
    public string EventId { get; }

    /// <summary>Shows the originating commander, project, and commodity amounts for comparison with Raven credit.</summary>
    public string Summary { get; }

    /// <summary>Notifies the recovery actions when this delivery is explicitly checked or unchecked.</summary>
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (value == isSelected)
            {
                return;
            }
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            selectionChanged();
        }
    }
}
