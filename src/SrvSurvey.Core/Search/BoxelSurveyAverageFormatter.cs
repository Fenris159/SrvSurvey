using System.Globalization;

namespace SrvSurvey.Core.Search;

public readonly record struct BoxelSurveyAverageFormat(
    int MinSystemsForAverages = 10);

public static class BoxelSurveyAverageFormatter
{
    public const string Placeholder = "\u2014";

    public static string Format(
        int? count,
        int visitedSystems,
        BoxelSurveyAverageFormat format = default)
    {
        var minSystems = format.MinSystemsForAverages <= 0
            ? 10
            : format.MinSystemsForAverages;
        if (count is null || count.Value == 0)
        {
            return Placeholder;
        }

        if (visitedSystems < minSystems)
        {
            return Placeholder;
        }

        if (visitedSystems >= count.Value)
        {
            var inverse = visitedSystems / (double)count.Value;
            return string.Create(CultureInfo.CurrentCulture, $"1 in {inverse:0.#}");
        }

        return (count.Value / (double)visitedSystems)
            .ToString("0.##", CultureInfo.CurrentCulture);
    }
}
