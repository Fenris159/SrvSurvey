using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class StreamOverlayViewModel : INotifyPropertyChanged
{
    private readonly StreamOverlaySettingsStore settingsStore;
    private bool enabled;
    private string statusMessage;

    public StreamOverlayViewModel(StreamOverlaySettingsStore settingsStore)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        enabled = settingsStore.LoadEnabled();
        statusMessage = enabled
            ? "Waiting for the Elite window before composing overlays."
            : "The joined stream overlay is disabled.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
            {
                return;
            }

            settingsStore.SaveEnabled(value);
            enabled = value;
            OnPropertyChanged();
            StatusMessage = value
                ? "Waiting for the Elite window before composing overlays."
                : "The joined stream overlay is disabled.";
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        internal set
        {
            if (string.Equals(statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
        }
    }

    public void Toggle()
    {
        Enabled = !Enabled;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
