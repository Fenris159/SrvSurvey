using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed partial class ColonizationSettingsStore
{
    // Null follows Raven's saved live-project choice; an empty ID explicitly clears focus.
    internal string? LoadLocalProjectFocus(string commander) =>
        documentStore.Load()[ColonizationSectionKey] is JsonObject section
        && section["ProjectFocus"] is JsonObject choices
        && choices[commander.ToUpperInvariant()] is JsonValue value
        && value.TryGetValue<string>(out string? id)
            ? id
            : null;

    internal void SaveLocalProjectFocus(string commander, string? projectId)
    {
        documentStore.Update(root =>
        {
            if (root[ColonizationSectionKey] is not JsonObject section)
            {
                section = [];
                root[ColonizationSectionKey] = section;
            }
            if (section["ProjectFocus"] is not JsonObject choices)
            {
                choices = [];
                section["ProjectFocus"] = choices;
            }
            if (projectId is null)
            {
                choices.Remove(commander.ToUpperInvariant());
            }
            else
            {
                choices[commander.ToUpperInvariant()] = projectId;
            }
            root[VersionKey] = 1;
        });
    }
}
