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
    private double scalePercent;
    private string settingsStatus = string.Empty;

    public OverlayScaleSettingsViewModel(
        OverlayScaleSettingsStore settingsStore,
        LegacyOverlayLayout activeLayout,
        OverlayWindowRegistry? windowRegistry = null
    )
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.activeLayout = activeLayout ?? throw new ArgumentNullException(nameof(activeLayout));
        this.windowRegistry = windowRegistry ?? OverlayWindowRegistry.Shared;
        OverlayScalePreferences preferences = settingsStore.Load();
        scalePercent = OverlayScaleCatalog.GetPercent(preferences.Index);
        activeLayout.SetScaleIndex(preferences.Index);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double ScalePercent
    {
        get => scalePercent;
        set
        {
            int normalized = OverlayScaleCatalog.NormalizePercent(value);
            if (Math.Abs(scalePercent - normalized) <= 0.0001d)
            {
                return;
            }

            double previous = scalePercent;
            int index = OverlayScaleCatalog.GetIndex(normalized);
            try
            {
                settingsStore.Save(new OverlayScalePreferences(index));
                scalePercent = normalized;
                activeLayout.SetScaleIndex(index);
                foreach (RegisteredOverlayWindow registered in windowRegistry.Snapshot())
                {
                    OverlayThemeResources.ApplyScale(registered.Window, activeLayout, registered.PlotterName);
                }

                SettingsStatus = $"Overlay scale changed to {ScaleLabel} from the operating-system baseline.";
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScaleLabel));
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                            or ArgumentException
                )
            {
                scalePercent = previous;
                SettingsStatus = "Overlay scale was not changed: " + exception.Message;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScaleLabel));
            }
        }
    }

    public string ScaleLabel => OverlayScaleCatalog.FormatPercent((int)ScalePercent);

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

    public OverlayScaleMigrationResult MigrateLegacyScale(double renderScaling)
    {
        OverlayScaleMigrationResult result = settingsStore.MigrateLegacyScale(renderScaling);
        if (!result.Migrated)
        {
            return result;
        }

        scalePercent = OverlayScaleCatalog.GetPercent(result.MigratedIndex);
        activeLayout.SetScaleIndex(result.MigratedIndex);
        OnPropertyChanged(nameof(ScalePercent));
        OnPropertyChanged(nameof(ScaleLabel));
        return result;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
