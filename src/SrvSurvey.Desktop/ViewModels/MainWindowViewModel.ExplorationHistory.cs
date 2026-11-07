namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int selectedExplorationTabIndex;

    public ExplorationHistoryViewModel ExplorationHistory { get; }
    public int SelectedExplorationTabIndex
    {
        get => selectedExplorationTabIndex;
        set
        {
            if (SetField(ref selectedExplorationTabIndex, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(IsExplorationTotalsSelected));
                OnPropertyChanged(nameof(IsExplorationHistorySelected));
            }
        }
    }
    public bool IsExplorationTotalsSelected => SelectedExplorationTabIndex == 0;
    public bool IsExplorationHistorySelected => SelectedExplorationTabIndex == 1;
    public string ExplorationValueDescription => ExplorationHistory.ValueModeDescription;

    private void OnExplorationHistoryBalanceChanged(object? sender, EventArgs eventArgs)
    {
        if (disposed)
        {
            return;
        }
        if (ExplorationHistory.HasCommander)
        {
            UpdateExplorationDisplay(explorationState.CreateSnapshot());
        }
        OnPropertyChanged(nameof(ExplorationValueDescription));
    }
}
