using System.Text.Json;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MiningWorkspaceViewModel
{
    private readonly MiningOverlaySettingsStore overlaySettings;
    private MiningOverlayPreferences lastOverlayPreferences = new();
    private string? overlayCommander;
    private string overlaySettingsStatus = "";

    public string OverlaySettingsStatus
    {
        get => overlaySettingsStatus;
        private set
        {
            if (Set(ref overlaySettingsStatus, value))
            {
                Changed(nameof(HasOverlaySettingsStatus));
            }
        }
    }

    public bool HasOverlaySettingsStatus => OverlaySettingsStatus.Length > 0;

    private void LoadOverlayPreferences(bool readCommanderDefaults = false)
    {
        try
        {
            overlaySettings
                .Load(
                    overlayCommander,
                    () =>
                        readCommanderDefaults && overlayCommander is not null
                            ? MiningOverlayPreferences.From(store.Load(overlayCommander).Settings)
                            : MiningOverlayPreferences.From(Settings)
                )
                .ApplyTo(Settings);
            lastOverlayPreferences = MiningOverlayPreferences.From(Settings);
            OverlaySettingsStatus = "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            OverlaySettingsStatus = "Mining overlay settings could not be loaded: " + exception.Message;
        }
    }

    private void SaveOverlayPreferences()
    {
        try
        {
            var current = MiningOverlayPreferences.From(Settings);
            overlaySettings.Save(overlayCommander, lastOverlayPreferences, current);
            lastOverlayPreferences = current;
            OverlaySettingsStatus = "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            OverlaySettingsStatus = "Mining overlay settings could not be saved: " + exception.Message;
        }
    }
}
