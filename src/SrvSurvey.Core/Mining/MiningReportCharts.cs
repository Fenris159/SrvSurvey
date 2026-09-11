using System.Globalization;
using System.Net;
using System.Text;

namespace SrvSurvey.Core.Mining;

/// <summary>Offline SVG reports from recorded observations, without reconstructing missing journal history.</summary>
internal static class MiningReportCharts
{
    public static void AppendTimeline(StringBuilder html, MiningSession session)
    {
        var yields = session
            .Prospects.SelectMany(p =>
                p.Materials.Select(m => new
                {
                    m.Name,
                    Minutes = (p.Time - session.Started).TotalMinutes,
                    m.Percentage,
                })
            )
            .Where(p => p.Minutes >= 0 && double.IsFinite(p.Percentage))
            .OrderBy(p => p.Minutes)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var collections = session
            .Collections.Where(c => !c.Engineering && c.Time >= session.Started)
            .OrderBy(c => c.Time)
            .ToArray();
        if (yields.Length == 0 && collections.Length == 0)
        {
            html.Append("<p>No timed observations were recorded; charts are unavailable for summary-only imports.</p>");
            return;
        }
        html.Append(
            "<h3>Yield over time</h3><p>Minutes since session start, including pauses. Percentages are observed surface yields; core-only finds have no percentage.</p>"
        );
        foreach (var mineral in yields)
        {
            Chart(
                html,
                mineral.Key + " — yield",
                mineral.Select(p => new Point(p.Minutes, p.Percentage)).ToArray(),
                "%",
                false
            );
        }

        html.Append(
            "<h3>Cumulative refining over time</h3><p>Recorded refined tonnage; transfers, purchases and engineering materials are excluded.</p>"
        );
        foreach (var mineral in collections.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var total = 0d;
            var points = mineral
                .Select(c =>
                {
                    total += c.Count;
                    return new Point((c.Time - session.Started).TotalMinutes, total);
                })
                .Prepend(new Point(0, 0))
                .ToArray();
            Chart(html, mineral.Key + " — refined", points, "t", true);
        }
    }

    public static void AppendMaterialComparison(StringBuilder html, IReadOnlyList<MiningSession> sessions)
    {
        var minerals = sessions
            .SelectMany(s => s.Collections)
            .Where(c => !c.Engineering)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (minerals.Length == 0)
        {
            return;
        }

        html.Append(
            "<h2>Materials across sessions</h2><p>Refined tons and tons per active hour for each mineral. Imported summaries without collection observations are marked unavailable.</p><div style='overflow-x:auto'><table><thead><tr><th>Session</th>"
        );
        foreach (var mineral in minerals)
        {
            html.Append($"<th>{H(mineral)}</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var session in sessions)
        {
            html.Append($"<tr><th>{H(session.System)} · {H(session.Started.ToString("g"))}</th>");
            foreach (var mineral in minerals)
            {
                if (session.Imported is not null)
                {
                    html.Append("<td>Not recorded</td>");
                    continue;
                }
                var tons = session
                    .Collections.Where(c =>
                        !c.Engineering && c.Name.Equals(mineral, StringComparison.OrdinalIgnoreCase)
                    )
                    .Sum(c => c.Count);
                var rate = session.ActiveDuration.TotalHours > 0 ? tons / session.ActiveDuration.TotalHours : 0;
                html.Append(CultureInfo.InvariantCulture, $"<td>{tons:0.##} t · {rate:0.##} t/h</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table></div>");
    }

    private static void Chart(StringBuilder html, string label, IReadOnlyList<Point> points, string unit, bool stepped)
    {
        var maxX = Math.Max(1, points.Max(p => p.Minutes));
        var maxY = Math.Max(1, points.Max(p => p.Value));
        double X(Point p) => 60 + p.Minutes / maxX * 680;
        double Y(Point p) => 200 - p.Value / maxY * 170;
        html.Append(
            $"<figure style='margin:20px 0'><figcaption>{H(label)}</figcaption><svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 780 240' role='img' aria-label='{H(label)}' style='width:100%;max-height:300px'>"
        );
        html.Append(
            "<path d='M60 25 V200 H740' stroke='currentColor' fill='none'/><g fill='currentColor' font-size='12'>"
        );
        html.Append(
            CultureInfo.InvariantCulture,
            $"<text x='10' y='35'>{maxY:0.##} {unit}</text><text x='35' y='205'>0</text><text x='60' y='225'>0 min</text><text x='690' y='225'>{maxX:0.##} min</text></g>"
        );
        var path = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (i > 0 && stepped)
            {
                path.Append(CultureInfo.InvariantCulture, $" L{X(p):0.##},{Y(points[i - 1]):0.##}");
            }

            path.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : " L")}{X(p):0.##},{Y(p):0.##}");
        }
        html.Append($"<path d='{path}' stroke='var(--accent)' stroke-width='2' fill='none'/>");
        foreach (var p in points)
        {
            html.Append(
                CultureInfo.InvariantCulture,
                $"<circle cx='{X(p):0.##}' cy='{Y(p):0.##}' r='4' fill='var(--accent)'><title>{p.Minutes:0.##} min · {p.Value:0.##} {unit}</title></circle>"
            );
        }

        html.Append("</svg></figure>");
    }

    private sealed record Point(double Minutes, double Value);

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
