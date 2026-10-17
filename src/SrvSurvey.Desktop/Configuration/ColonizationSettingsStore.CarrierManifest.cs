using System.Globalization;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed partial class ColonizationSettingsStore
{
    internal long LoadPlanningCarrier(string commander)
    {
        JsonObject root = documentStore.Load();
        var section = root[ColonizationSectionKey] as JsonObject;
        var choices = section?["PlanningCarriers"] as JsonObject;
        return
            choices?[commander.ToUpperInvariant()] is JsonValue value && value.TryGetValue<long>(out long id) && id >= 0
            ? id
            : 0;
    }

    internal void SavePlanningCarrier(string commander, long marketId)
    {
        documentStore.Update(root =>
        {
            if (root[ColonizationSectionKey] is not JsonObject section)
            {
                section = [];
                root[ColonizationSectionKey] = section;
            }
            if (section["PlanningCarriers"] is not JsonObject choices)
            {
                choices = [];
                section["PlanningCarriers"] = choices;
            }
            choices[commander.ToUpperInvariant()] = marketId;
            root[VersionKey] = 1;
        });
    }

    internal DateTimeOffset? LoadCarrierRefreshAttempt(long marketId)
    {
        JsonObject root = documentStore.Load();
        var section = root[ColonizationSectionKey] as JsonObject;
        var attempts = section?["CarrierRefreshAttempts"] as JsonObject;
        return
            attempts?[marketId.ToString(CultureInfo.InvariantCulture)] is JsonValue value
            && value.TryGetValue<DateTimeOffset>(out DateTimeOffset attempt)
            ? attempt
            : null;
    }

    internal void SaveCarrierRefreshAttempt(long marketId, DateTimeOffset attempt)
    {
        documentStore.Update(root =>
        {
            if (root[ColonizationSectionKey] is not JsonObject section)
            {
                section = [];
                root[ColonizationSectionKey] = section;
            }
            if (section["CarrierRefreshAttempts"] is not JsonObject attempts)
            {
                attempts = [];
                section["CarrierRefreshAttempts"] = attempts;
            }
            attempts[marketId.ToString(CultureInfo.InvariantCulture)] = JsonValue.Create(attempt);
            root[VersionKey] = 1;
        });
    }
}
