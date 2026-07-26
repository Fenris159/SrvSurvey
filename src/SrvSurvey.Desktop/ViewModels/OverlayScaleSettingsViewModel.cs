using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayScaleSettingsViewModel : INotifyPropertyChanged
{
    private readonly OverlayScaleSettingsStore settingsStore;
    private readonly LegacyOverlayLayout activeLayout;
    private readonly OverlayWindowRegistry windowRegistry;
    private OverlayScaleOption selectedOption;
    private string settingsStatus = string.Empty;

    public OverlayScaleSettingsViewModel(
        OverlayScaleSettingsStore settingsStore,
        LegacyOverlayLayout activeLayout,
        OverlayWindowRegistry? windowRegistry = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.activeLayout = activeLayout
            ?? throw new ArgumentNullException(nameof(activeLayout));
        this.windowRegistry = windowRegistry ?? OverlayWindowRegistry.Shared;
        Options = OverlayScaleCatalog.Options;
        var preferences = settingsStore.Load();
        selectedOption = Options.Single(option =>
            option.Index == preferences.Index);
        activeLayout.SetScaleIndex(preferences.Index);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<OverlayScaleOption> Options { get; }

    public OverlayScaleOption SelectedOption
    {
        get => selectedOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (selectedOption.Index == value.Index)
            {
                return;
            }

            var previous = selectedOption;
            try
            {
                settingsStore.Save(new OverlayScalePreferences(value.Index));
                selectedOption = value;
                activeLayout.SetScaleIndex(value.Index);
                foreach (var registered in windowRegistry.Snapshot())
                {
                    OverlayThemeResources.ApplyScale(
                        registered.Window,
                        activeLayout);
                }

                SettingsStatus =
                    $"Overlay scale changed to {value.DisplayName}.";
                OnPropertyChanged();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or ArgumentException)
            {
                selectedOption = previous;
                SettingsStatus = "Overlay scale was not changed: "
                    + exception.Message;
                OnPropertyChanged();
            }
        }
    }

    public string SettingsStatus
    {
        get => settingsStatus;
        private set
        {
            if (string.Equals(settingsStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            settingsStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSettingsStatus));
        }
    }

    public bool HasSettingsStatus => !string.IsNullOrWhiteSpace(SettingsStatus);

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
