using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class StreamOverlayViewModel : INotifyPropertyChanged
{
    private readonly StreamOverlaySettingsStore settingsStore;
    private bool enabled;
    private string statusMessage;
    private string[] activeOverlayNames = [];

    public StreamOverlayViewModel(StreamOverlaySettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
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
            if (!value)
            {
                ActiveOverlayNames = [];
            }
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

    /// <summary>Names only the panels whose pixels are present in the current joined capture.</summary>
    public IReadOnlyList<string> ActiveOverlayNames
    {
        get => activeOverlayNames;
        internal set
        {
            if (activeOverlayNames.SequenceEqual(value, StringComparer.Ordinal))
            {
                return;
            }
            activeOverlayNames = value.ToArray();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveOverlays));
        }
    }

    public bool HasActiveOverlays => activeOverlayNames.Length > 0;

    public void Toggle()
    {
        Enabled = !Enabled;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
