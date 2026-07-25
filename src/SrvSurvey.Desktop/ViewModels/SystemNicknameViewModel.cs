using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SystemNicknameViewModel : INotifyPropertyChanged
{
    private readonly SystemNicknameCatalog catalog;
    private readonly SystemNicknameSettingsStore settingsStore;
    private bool enabled;
    private string statusMessage;

    public SystemNicknameViewModel(
        SystemNicknameCatalog catalog,
        SystemNicknameSettingsStore settingsStore)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        enabled = settingsStore.LoadEnabled();
        statusMessage = catalog.Warnings.Count == 0
            ? $"Loaded {catalog.LocalCount:N0} personal and "
                + $"{catalog.RavenCount:N0} Raven system nickname(s)."
            : string.Join(" ", catalog.Warnings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? NamesChanged;

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (value == enabled)
            {
                return;
            }

            try
            {
                settingsStore.SaveEnabled(value);
                enabled = value;
                OnPropertyChanged();
                StatusMessage = value
                    ? $"System nicknames are active: {catalog.LocalCount:N0} "
                        + "personal and "
                        + $"{catalog.RavenCount:N0} Raven name(s) loaded."
                    : "System nicknames are off; canonical names are shown.";
                NamesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                StatusMessage = "The system nickname preference could not be saved: "
                    + exception.Message;
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string Resolve(string? systemName)
    {
        return catalog.Resolve(systemName, Enabled);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
