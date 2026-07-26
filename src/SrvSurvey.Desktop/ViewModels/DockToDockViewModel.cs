using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Travel;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class DockToDockViewModel : INotifyPropertyChanged
{
    private readonly DockToDockSettingsStore settingsStore;
    private readonly DockToDockLogService logService;
    private bool enabled;
    private string statusMessage;

    public DockToDockViewModel(
        DockToDockSettingsStore settingsStore,
        DockToDockLogService logService)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.logService = logService
            ?? throw new ArgumentNullException(nameof(logService));
        enabled = settingsStore.LoadEnabled();
        statusMessage = CreateReadyStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (!SetField(ref enabled, value))
            {
                return;
            }

            try
            {
                settingsStore.SaveEnabled(value);
                StatusMessage = CreateReadyStatus();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                StatusMessage =
                    "The dock-to-dock preference changed for this session but could not be saved: "
                    + exception.Message;
            }
        }
    }

    public string OutputPath => logService.OutputPath;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public void ApplyUpdate(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        CargoSnapshot? cargo,
        bool isBootstrapRead)
    {
        var result = logService.Apply(
            journalEvents,
            cargo,
            Enabled,
            isBootstrapRead);
        if (result.Error is not null)
        {
            StatusMessage = result.WrittenCount == 0
                ? "The dock-to-dock CSV was left unchanged: " + result.Error
                : $"Saved {result.WrittenCount:N0} completed trip(s), then stopped without appending the remaining row(s): "
                    + result.Error;
        }
        else if (result.WrittenCount > 0)
        {
            StatusMessage = result.WrittenCount == 1
                ? "Saved one completed dock-to-dock trip to " + OutputPath + "."
                : $"Saved {result.WrittenCount:N0} completed dock-to-dock trips to "
                    + OutputPath
                    + ".";
        }
    }

    private string CreateReadyStatus()
    {
        return Enabled
            ? "Completed live trips will be appended safely to " + OutputPath + "."
            : "Dock-to-dock CSV logging is off.";
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
