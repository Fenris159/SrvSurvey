using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Discord;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Stores the local opt-in and webhook, excluded from the profile-sync settings allowlist.</summary>
public sealed class DiscordCarrierSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore document = new(path);

    public DiscordCarrierPreferences Load()
    {
        try
        {
            DiscordCarrierPreferences preferences =
                document.Load()["DiscordCarrierAnnouncements"]?.Deserialize<DiscordCarrierPreferences>() ?? new();
            return preferences with { WebhookUrl = preferences.WebhookUrl ?? string.Empty };
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(DiscordCarrierPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        document.Update(root => root["DiscordCarrierAnnouncements"] = JsonSerializer.SerializeToNode(preferences));
    }
}
