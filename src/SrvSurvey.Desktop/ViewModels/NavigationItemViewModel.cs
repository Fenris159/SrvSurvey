using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record NavigationItemViewModel(
    string Key,
    string Label,
    string Description,
    bool HasOverlaySettings = false) : INotifyPropertyChanged
{
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
