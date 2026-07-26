using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Configuration;

public sealed class CommanderPreferenceSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public CommanderPreferenceSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public CommanderPreferencePreferences Load()
    {
        var settings = documentStore.Load()["CommanderPreference"] as JsonObject;
        return new CommanderPreferencePreferences(
            NormalizeName(GetString(settings, "PreferredCommanderName")),
            NormalizeFrontierId(GetString(settings, "PreferredFrontierId")));
    }

    public void Save(CommanderPreferencePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var commanderName = NormalizeName(preferences.PreferredCommanderName);
        var frontierId = NormalizeFrontierId(preferences.PreferredFrontierId);
        if (preferences.PreferredFrontierId is not null && frontierId is null)
        {
            throw new ArgumentException(
                "The preferred Frontier ID is invalid.",
                nameof(preferences));
        }

        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["CommanderPreference"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["CommanderPreference"] = settings;
            }

            settings["PreferredCommanderName"] = commanderName;
            settings["PreferredFrontierId"] = frontierId;
        });
    }

    private static string? GetString(JsonObject? settings, string propertyName)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static string? NormalizeName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeFrontierId(string? value)
    {
        var normalized = value?.Trim();
        return normalized is not null
            && normalized.Length > 1
            && normalized[0] is 'F' or 'f'
            && normalized[1..].All(char.IsAsciiDigit)
                ? normalized.ToUpperInvariant()
                : null;
    }
}

public sealed record CommanderPreferencePreferences(
    string? PreferredCommanderName,
    string? PreferredFrontierId);

public sealed class CommanderPreferenceResolver(
    CommanderPreferenceSettingsStore settingsStore,
    CommanderProfileCatalog profileCatalog)
{
    public async Task<CommanderPreferenceResolution> ResolveAsync(
        string? commandLineFrontierId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(commandLineFrontierId))
        {
            return new CommanderPreferenceResolution(
                commandLineFrontierId.Trim().ToUpperInvariant(),
                true,
                "The command-line Frontier ID overrides the saved commander preference for this instance.");
        }

        var preference = settingsStore.Load();
        if (!string.IsNullOrWhiteSpace(preference.PreferredFrontierId))
        {
            return new CommanderPreferenceResolution(
                preference.PreferredFrontierId,
                false,
                preference.PreferredCommanderName is null
                    ? $"Startup is pinned to {preference.PreferredFrontierId}."
                    : $"Startup is pinned to {preference.PreferredCommanderName} ({preference.PreferredFrontierId}).");
        }

        if (string.IsNullOrWhiteSpace(preference.PreferredCommanderName))
        {
            return CommanderPreferenceResolution.Automatic;
        }

        CommanderProfileCatalogResult catalog;
        try
        {
            catalog = await profileCatalog.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new CommanderPreferenceResolution(
                null,
                false,
                $"The imported commander preference '{preference.PreferredCommanderName}' could not be resolved because profiles could not be read: {exception.Message}");
        }

        var matches = catalog.Profiles
            .Where(profile => string.Equals(
                profile.CommanderName,
                preference.PreferredCommanderName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            var reason = matches.Length == 0
                ? "no imported profile has that exact name"
                : "more than one imported profile has that name";
            return new CommanderPreferenceResolution(
                null,
                false,
                $"The imported commander preference '{preference.PreferredCommanderName}' was not applied because {reason}. Automatic newest-journal selection remains active.");
        }

        var match = matches[0];
        try
        {
            settingsStore.Save(new CommanderPreferencePreferences(
                match.CommanderName,
                match.FrontierId));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            return new CommanderPreferenceResolution(
                match.FrontierId,
                false,
                $"Resolved the imported commander preference to {match.CommanderName} ({match.FrontierId}), but could not persist the stable identity: {exception.Message}");
        }

        var warningSuffix = catalog.Warnings.Count == 0
            ? string.Empty
            : $" {catalog.Warnings.Count:N0} unrelated malformed profile file(s) were ignored.";
        return new CommanderPreferenceResolution(
            match.FrontierId,
            false,
            $"Resolved the imported commander preference to {match.CommanderName} ({match.FrontierId}) and saved its stable identity.{warningSuffix}");
    }
}

public sealed record CommanderPreferenceResolution(
    string? TargetFrontierId,
    bool IsCommandLineOverride,
    string? StatusMessage)
{
    public static CommanderPreferenceResolution Automatic { get; } =
        new(null, false, null);
}
