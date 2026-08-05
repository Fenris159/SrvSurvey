using System.Globalization;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayScaleSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayScaleSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public OverlayScalePreferences Load()
    {
        var settings = documentStore.Load()["OverlayScale"] as JsonObject;
        return new OverlayScalePreferences(
            OverlayScaleCatalog.NormalizeIndex(GetIndex(settings?["Index"])));
    }

    public void Save(OverlayScalePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!OverlayScaleCatalog.IsSupported(preferences.Index))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences),
                $"Overlay scale index {preferences.Index} is not supported.");
        }

        documentStore.Update(root =>
        {
            var settings = root["OverlayScale"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["OverlayScale"] = settings;
            }

            root["Version"] = 1;
            settings["Index"] = preferences.Index;
        });
    }

    private static int? GetIndex(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<double>(out var number)
            && double.IsFinite(number)
            && double.IsInteger(number)
            && number is >= int.MinValue and <= int.MaxValue)
        {
            return Convert.ToInt32(number, CultureInfo.InvariantCulture);
        }

        return null;
    }
}

public sealed record OverlayScalePreferences(int Index)
{
    public static OverlayScalePreferences Default { get; } = new(0);
}

public static class OverlayScaleCatalog
{
    private static readonly double?[] AbsoluteScales =
    [
        null,
        1d,
        1.1d,
        1.2d,
        1.25d,
        1.3d,
        1.4d,
        1.5d,
        1.6d,
        1.7d,
        1.75d,
        1.8d,
        1.9d,
        2d,
        2.1d,
        2.2d,
        2.25d,
        2.3d,
        2.4d,
        2.5d,
        0.9d,
        0.8d,
        0.75d,
        0.7d,
        0.6d,
        0.5d,
    ];

    public static IReadOnlyList<OverlayScaleOption> Options { get; } =
        AbsoluteScales.Select((scale, index) => new OverlayScaleOption(
                index,
                scale is null
                    ? "Match operating-system scale"
                    : scale.Value.ToString("0.##%", CultureInfo.InvariantCulture),
                scale))
            .ToArray();

    public static bool IsSupported(int index)
    {
        return index >= 0 && index < AbsoluteScales.Length;
    }

    public static int NormalizeIndex(int? index)
    {
        return index is { } value && IsSupported(value) ? value : 0;
    }

    public static double GetRelativeScale(int index, double renderScaling)
    {
        var normalized = NormalizeIndex(index);
        var absolute = AbsoluteScales[normalized];
        if (absolute is null)
        {
            return 1d;
        }

        var safeRenderScaling = double.IsFinite(renderScaling)
            && renderScaling > 0
                ? renderScaling
                : 1d;
        return absolute.Value / safeRenderScaling;
    }
}

public sealed record OverlayScaleOption(
    int Index,
    string DisplayName,
    double? AbsoluteScale)
{
    public override string ToString() => DisplayName;
}
