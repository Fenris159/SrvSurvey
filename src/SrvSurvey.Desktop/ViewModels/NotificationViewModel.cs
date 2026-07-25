using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class NotificationViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(6);
    private readonly NotificationSettingsStore settingsStore;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<string, MaterialState> materials = new(
        StringComparer.OrdinalIgnoreCase);
    private NotificationPreferences preferences;
    private IReadOnlyList<NotificationMessageViewModel> messages = [];
    private string settingsStatus = string.Empty;

    public NotificationViewModel(
        NotificationSettingsStore settingsStore,
        TimeProvider? timeProvider = null)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        preferences = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => preferences.Enabled;
        set => Update(preferences with { Enabled = value });
    }

    public bool MaterialCountAfterPickup
    {
        get => preferences.MaterialCountAfterPickup;
        set => Update(preferences with { MaterialCountAfterPickup = value });
    }

    public bool CargoMissionRemaining
    {
        get => preferences.CargoMissionRemaining;
        set => Update(preferences with { CargoMissionRemaining = value });
    }

    public bool CurrentBoxelSearchStatus
    {
        get => preferences.CurrentBoxelSearchStatus;
        set => Update(preferences with { CurrentBoxelSearchStatus = value });
    }

    public bool ShowNextBoxelToSearch
    {
        get => preferences.ShowNextBoxelToSearch;
        set => Update(preferences with { ShowNextBoxelToSearch = value });
    }

    public bool ShowScreenshot
    {
        get => preferences.ShowScreenshot;
        set => Update(preferences with { ShowScreenshot = value });
    }

    public IReadOnlyList<NotificationMessageViewModel> Messages
    {
        get => messages;
        private set
        {
            if (SetField(ref messages, value))
            {
                OnPropertyChanged(nameof(HasMessages));
                OnPropertyChanged(nameof(ShouldShow));
            }
        }
    }

    public bool HasMessages => Messages.Count > 0;

    public bool ShouldShow => Enabled && HasMessages;

    public double ProgressPercent
    {
        get
        {
            if (Messages.Count == 0)
            {
                return 0;
            }

            var remaining = (Messages.Max(message => message.ExpiresAtUtc)
                - timeProvider.GetUtcNow()).TotalMilliseconds;
            return Math.Clamp(
                remaining / MessageDuration.TotalMilliseconds * 100,
                0,
                100);
        }
    }

    public string SettingsStatus
    {
        get => settingsStatus;
        private set
        {
            if (SetField(ref settingsStatus, value))
            {
                OnPropertyChanged(nameof(HasSettingsStatus));
            }
        }
    }

    public bool HasSettingsStatus => !string.IsNullOrWhiteSpace(SettingsStatus);

    public void ApplyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool allowNotifications)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        foreach (var journalEvent in journalEvents)
        {
            switch (journalEvent.EventName)
            {
                case "Materials":
                    ResetMaterials(journalEvent.Payload);
                    break;

                case "MaterialCollected":
                    ApplyMaterialCollected(
                        journalEvent.Payload,
                        allowNotifications);
                    break;

                case "CargoDepot" when allowNotifications
                    && CargoMissionRemaining:
                    ApplyCargoDepot(journalEvent.Payload);
                    break;
            }
        }
    }

    public void ReportBoxelUpdate(
        BoxelSearchNotificationState before,
        BoxelSearchNotificationState after,
        bool hadFssAllBodiesFound,
        bool allowNotifications)
    {
        if (!allowNotifications
            || !hadFssAllBodiesFound
            || !after.IsActive
            || after.CompletionMode != BoxelCompletionMode.FssAllBodies)
        {
            return;
        }

        if (CurrentBoxelSearchStatus && after.TotalSystems > 0)
        {
            var progress = after.CompletedSystems / (double)after.TotalSystems;
            ShowMessage($"Current boxel {progress:P0} searched.");
        }

        if (ShowNextBoxelToSearch
            && !before.CurrentSystemsComplete
            && after.CurrentSystemsComplete
            && !string.IsNullOrWhiteSpace(after.NextSystem))
        {
            ShowMessage($"Next boxel to search: {after.NextSystem}");
        }
    }

    public void ReportScreenshotResult(
        ScreenshotProcessingResult result,
        bool includedBanner)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!ShowScreenshot)
        {
            return;
        }

        foreach (var conversion in result.Conversions)
        {
            ShowMessage(
                $"Saved '{Path.GetFileName(conversion.OutputPath)}' with"
                + (includedBanner ? string.Empty : " no")
                + " banner");
        }
    }

    public void ReportGreenGasGiantUploads(
        GreenGasGiantPublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var candidate in result.Published)
        {
            ShowMessage($"Congrats, {candidate.Tag} GGG uploaded!");
        }
    }

    public void ShowBannerPreference(bool enabled)
    {
        ShowMessage(enabled
            ? "Adding embedded banner to future screenshots"
            : "Future screenshots will have no embedded banner");
    }

    public void ShowMessage(string message)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var text = message.Trim();
        var expires = timeProvider.GetUtcNow() + MessageDuration;
        Messages = Messages
            .Where(existing => !string.Equals(
                existing.Text,
                text,
                StringComparison.Ordinal))
            .Append(new NotificationMessageViewModel(text, expires))
            .ToArray();
        OnPropertyChanged(nameof(ProgressPercent));
    }

    public void Refresh()
    {
        var now = timeProvider.GetUtcNow();
        var active = Messages
            .Where(message => message.ExpiresAtUtc > now)
            .ToArray();
        if (active.Length != Messages.Count)
        {
            Messages = active;
        }

        OnPropertyChanged(nameof(ProgressPercent));
    }

    private void ResetMaterials(JsonElement root)
    {
        materials.Clear();
        foreach (var category in new[] { "Raw", "Manufactured", "Encoded" })
        {
            if (!root.TryGetProperty(category, out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                var name = GetString(entry, "Name");
                var count = GetInt32(entry, "Count");
                if (string.IsNullOrWhiteSpace(name) || count is null)
                {
                    continue;
                }

                materials[GetMaterialKey(category, name)] = new MaterialState(
                    GetString(entry, "Name_Localised") ?? name,
                    Math.Max(0, count.Value));
            }
        }
    }

    private void ApplyMaterialCollected(
        JsonElement root,
        bool allowNotifications)
    {
        var category = GetString(root, "Category");
        var name = GetString(root, "Name");
        var count = GetInt32(root, "Count");
        if (string.IsNullOrWhiteSpace(category)
            || string.IsNullOrWhiteSpace(name)
            || count is not > 0)
        {
            return;
        }

        var key = GetMaterialKey(category, name);
        var existing = materials.GetValueOrDefault(key);
        var displayName = GetString(root, "Name_Localised")
            ?? existing?.DisplayName
            ?? name;
        var total = Math.Max(0, (existing?.Count ?? 0) + count.Value);
        materials[key] = new MaterialState(displayName, total);
        if (allowNotifications && MaterialCountAfterPickup)
        {
            ShowMessage(
                $"Collected: {count.Value}x {displayName}, new total {total}");
        }
    }

    private void ApplyCargoDepot(JsonElement root)
    {
        var updateType = GetString(root, "UpdateType");
        var delivered = GetInt32(root, "ItemsDelivered");
        var total = GetInt32(root, "TotalItemsToDeliver");
        var cargoType = GetString(root, "CargoType");
        if (!string.Equals(updateType, "Deliver", StringComparison.Ordinal)
            || delivered is null
            || total is null
            || delivered.Value >= total.Value
            || string.IsNullOrWhiteSpace(cargoType))
        {
            return;
        }

        ShowMessage(
            $"Deliver {cargoType}: {total.Value - delivered.Value} units remaining");
    }

    private void Update(NotificationPreferences updated)
    {
        if (preferences == updated)
        {
            return;
        }

        preferences = updated;
        OnPropertyChanged(string.Empty);
        if (!Enabled && Messages.Count > 0)
        {
            Messages = [];
        }

        try
        {
            settingsStore.Save(preferences);
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            SettingsStatus = "Notification preferences changed for this session "
                + "but could not be saved: "
                + exception.Message;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static string GetMaterialKey(string category, string name)
    {
        return category + "\0" + name;
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed record MaterialState(string DisplayName, int Count);
}

public sealed record NotificationMessageViewModel(
    string Text,
    DateTimeOffset ExpiresAtUtc);

public sealed record BoxelSearchNotificationState(
    bool IsActive,
    BoxelCompletionMode CompletionMode,
    int CompletedSystems,
    int TotalSystems,
    bool CurrentSystemsComplete,
    string? NextSystem);
