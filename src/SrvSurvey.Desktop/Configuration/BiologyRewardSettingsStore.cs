using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class BiologyRewardSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public BiologyRewardSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public BiologyRewardThresholds Load()
    {
        var settings = documentStore.Load()["BiologyRewards"] as JsonObject;
        return BiologyRewardThresholds.Normalize(
            GetDouble(settings, "BucketOneMillions", 3),
            GetDouble(settings, "BucketTwoMillions", 7),
            GetDouble(settings, "BucketThreeMillions", 12));
    }

    public void Save(BiologyRewardThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var normalized = BiologyRewardThresholds.Normalize(
            thresholds.BucketOneMillions,
            thresholds.BucketTwoMillions,
            thresholds.BucketThreeMillions);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["BiologyRewards"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["BiologyRewards"] = settings;
            }

            settings["BucketOneMillions"] = normalized.BucketOneMillions;
            settings["BucketTwoMillions"] = normalized.BucketTwoMillions;
            settings["BucketThreeMillions"] = normalized.BucketThreeMillions;
        });
    }

    private static double GetDouble(
        JsonObject? settings,
        string propertyName,
        double fallback)
    {
        if (settings?[propertyName] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return value.TryGetValue<int>(out var integer) ? integer : fallback;
    }
}

public sealed record BiologyRewardThresholds(
    double BucketOneMillions,
    double BucketTwoMillions,
    double BucketThreeMillions)
{
    public static BiologyRewardThresholds Default { get; } = new(3, 7, 12);

    public static BiologyRewardThresholds Normalize(
        double bucketOneMillions,
        double bucketTwoMillions,
        double bucketThreeMillions)
    {
        var one = NormalizeValue(bucketOneMillions, 3);
        var two = Math.Max(one, NormalizeValue(bucketTwoMillions, 7));
        var three = Math.Max(two, NormalizeValue(bucketThreeMillions, 12));
        return new BiologyRewardThresholds(one, two, three);
    }

    private static double NormalizeValue(double value, double fallback)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0, 20) : fallback;
    }
}
