using System.Globalization;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record PowerplayPowerLineViewModel(string Name, double? Progress)
{
    private static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Aisling Duval"] = "#0099FF",
        ["Edmund Mahon"] = "#019C00",
        ["A. Lavigny-Duval"] = "#7F00FF",
        ["Arissa Lavigny-Duval"] = "#7F00FF",
        ["Nakato Kaine"] = "#A3F127",
        ["Felicia Winters"] = "#FFC400",
        ["Denton Patreus"] = "#00FFFF",
        ["Jerome Archer"] = "#DF1DE4",
        ["Zemina Torval"] = "#0040FF",
        ["Pranav Antal"] = "#FFFF00",
        ["Li Yong-Rui"] = "#33D688",
        ["Archon Delaine"] = "#FF0000",
        ["Yuri Grom"] = "#FF8000",
    };

    public string Display =>
        Progress is { } progress
            ? Name + " " + (progress * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%"
            : Name + " —";

    public string ColorHex => ColorFor(Name);

    public static string ColorFor(string power) => Colors.GetValueOrDefault(power, "#DDDDDD");

    public static IReadOnlyList<PowerplayPowerLineViewModel> From(
        IReadOnlyList<string> powers,
        IReadOnlyList<PowerplayProgress> progress,
        string controllingPower = "",
        double? controlProgress = null
    ) =>
        powers
            .Concat(progress.Select(entry => entry.Power))
            .Append(controllingPower)
            .Where(power => !string.IsNullOrWhiteSpace(power))
            .DistinctBy(PowerplayPlan.SpanshPowerName, StringComparer.OrdinalIgnoreCase)
            .Select(power => new PowerplayPowerLineViewModel(
                power,
                controlProgress is { } control && PowerplayPlan.SamePower(power, controllingPower)
                    ? control
                    : progress
                        .Where(entry => PowerplayPlan.SamePower(entry.Power, power))
                        .Select(entry => (double?)entry.Progress)
                        .Max()
            ))
            .OrderByDescending(power => power.Progress ?? -1)
            .ThenBy(power => power.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
