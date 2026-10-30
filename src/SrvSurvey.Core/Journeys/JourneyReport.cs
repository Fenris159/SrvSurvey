using System.Globalization;
using System.Net;
using System.Text;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journeys;

/// <summary>An offline expedition report using portable relative links for its image bundle.</summary>
public static partial class JourneyReport
{
    public static string Html(
        JourneyDocument journey,
        IReadOnlyDictionary<long, string>? systemNotes = null,
        IReadOnlyDictionary<long, IReadOnlyList<JourneyReportImage>>? systemImages = null
    )
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (journey.EndTime is not { } endTime)
        {
            throw new InvalidOperationException("Complete the journey before exporting its report.");
        }
        JourneyQuickStatistics stats = JourneyStatistics.Calculate(journey);
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append(
                "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; base-uri 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src 'self' file:\">"
            )
            .Append("<title>")
            .Append(E(journey.Name))
            .Append(" — Journey report</title><style>")
            .Append(Styles)
            .Append(GalleryStyles)
            .Append("</style></head><body><main>");
        html.Append("<header class=\"report-heading\"><p class=\"brand\">SrvSurvey-XP · Expedition report</p><h1>")
            .Append(E(journey.Name))
            .Append("</h1><p class=\"commander\">Commander ")
            .Append(E(journey.CommanderName))
            .Append("</p><p class=\"description\">")
            .Append(E(journey.Description))
            .Append("</p><div class=\"period\">Departed ")
            .Append(Time(journey.StartTime))
            .Append("<span>Completed ")
            .Append(Time(endTime))
            .Append("</span><button type=\"button\" id=\"print-report\">Print / Save as PDF</button></div></header>");
        AppendRoute(html, journey);
        html.Append(
            "<section aria-labelledby=\"statistics-title\"><h2 id=\"statistics-title\">Expedition record</h2><dl class=\"metrics\">"
        );
        Metric(html, "Recorded route distance", N(stats.TotalDistance, "N1") + " ly");
        Metric(html, "Recorded arrivals", N(stats.JumpCount));
        Metric(html, "Distinct systems", N(stats.UniqueSystemCount));
        Metric(html, "Elapsed time", Duration(journey.StartTime, endTime));
        Metric(html, "Biology base value", N(stats.Counts.ExobiologyRewards) + " CR");
        Metric(html, "Estimated exploration value", N(stats.Counts.ExplorationRewards) + " CR");
        html.Append(
                "</dl><p class=\"muted\">Distance follows the recorded visits, starting at the first system. Biology values use catalog base rewards; exploration values are estimates.</p>"
            )
            .Append("<div class=\"stat-columns\"><div><h3>Survey and discovery</h3>");
        AppendCounts(html, stats.Counts);
        html.Append("</div><div><h3>Field activity</h3><dl class=\"facts\">");
        Fact(html, "Systems with complete FSS", N(stats.FssCompletedSystemCount));
        Fact(html, "Codex entries scanned", N(stats.CodexScanCount));
        Fact(html, "Landing sites by visit", N(stats.LandedBodyCount));
        Fact(html, "Recorded landings", N(stats.TotalLandingCount));
        html.Append("</dl>");
        AppendDictionary(html, "Discovery categories", stats.SubCategoryCounts);
        html.Append("</div></div></section>");
        html.Append(
                "<section aria-labelledby=\"history-title\"><div class=\"history-heading\"><div><h2 id=\"history-title\">System history</h2><p class=\"muted\">In travel order, including return visits. Times are UTC.</p></div><output id=\"visit-count\">"
            )
            .Append(N(journey.VisitedSystems.Count))
            .Append(" visits</output></div>")
            .Append(
                "<div class=\"history-tools\"><label for=\"system-search\">Find a system or note<input id=\"system-search\" type=\"search\" placeholder=\"Search this journey\"></label>"
            )
            .Append(
                "<label class=\"expand-control\"><input id=\"expand-visits\" type=\"checkbox\"> Show all visit details</label></div>"
            )
            .Append(
                "<p id=\"no-matches\" hidden>No visits match your search.</p><ol class=\"history\" aria-label=\"Visited systems\">"
            );
        for (int index = 0; index < journey.VisitedSystems.Count; index++)
        {
            JourneySystemVisit visit = journey.VisitedSystems[index];
            string notes = systemNotes?.GetValueOrDefault(visit.StarSystem.SystemAddress) ?? string.Empty;
            AppendVisit(
                html,
                journey,
                index,
                visit,
                notes,
                endTime,
                systemImages?.GetValueOrDefault(visit.StarSystem.SystemAddress)
            );
        }
        html.Append("</ol>");
        if (journey.VisitedSystems.Count == 0)
        {
            html.Append("<p class=\"muted\">No system visits were recorded for this journey.</p>");
        }
        html.Append(
                "</section><footer>Created with SrvSurvey-XP. Share this whole folder, including images, to keep the journey report portable. No connection is needed to read it.</footer></main>"
            )
            .Append(ImageViewer)
            .Append("<script>")
            .Append(Script)
            .Append(GalleryScript)
            .Append("</script></body></html>");
        return html.ToString();
    }

    private static void AppendVisit(
        StringBuilder html,
        JourneyDocument journey,
        int index,
        JourneySystemVisit visit,
        string notes,
        DateTimeOffset endTime,
        IReadOnlyList<JourneyReportImage>? images
    )
    {
        double leg = index == 0 ? 0 : journey.VisitedSystems[index - 1].StarSystem.DistanceTo(visit.StarSystem);
        html.Append("<li class=\"visit\" id=\"visit-")
            .Append(index + 1)
            .Append("\"><article><div class=\"visit-heading\"><span class=\"visit-number\">")
            .Append(index + 1)
            .Append("</span><div><h3>")
            .Append(E(visit.StarSystem.Name))
            .Append("</h3><p>")
            .Append(Time(visit.Arrived))
            .Append("</p></div><span class=\"leg-distance\">")
            .Append(index == 0 ? "Starting system" : N(leg, "N1") + " ly leg")
            .Append("</span></div><div class=\"visit-readout\">");
        Readout(html, "Bodies scanned", N(visit.Counts.BodyScans));
        Readout(html, "Mapped", N(visit.Counts.DetailedSurfaceScans));
        Readout(html, "Organisms", N(visit.Counts.Organisms));
        Readout(html, "New Codex", N(visit.Counts.NewCodexEntries));
        html.Append("</div>");
        AppendGallery(html, visit.StarSystem.Name, images);
        html.Append("<details><summary>Visit details</summary><div class=\"visit-details\"><dl class=\"facts\">");
        Fact(html, "System address", visit.StarSystem.SystemAddress.ToString(CultureInfo.InvariantCulture));
        GalacticCoordinate position = visit.StarSystem.Position;
        Fact(
            html,
            "Galactic coordinates (X, Y, Z)",
            $"{N(position.X, "0.##")}, {N(position.Y, "0.##")}, {N(position.Z, "0.##")} ly"
        );
        Fact(
            html,
            "Arrived (UTC)",
            visit.Arrived.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        );
        Fact(
            html,
            visit.Departed is null ? "Journey end (UTC)" : "Departed (UTC)",
            (visit.Departed ?? endTime).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        );
        Fact(html, "Time at this visit", Duration(visit.Arrived, visit.Departed ?? endTime));
        Fact(html, "FSS survey", visit.HasCompletedFss ? "Complete" : "Incomplete / not recorded");
        html.Append("</dl><div class=\"stat-columns\"><div>");
        AppendCounts(html, visit.Counts);
        html.Append("</div><div>");
        AppendDictionary(html, "Landings by body", visit.LandedOn);
        AppendDictionary(html, "Discovery categories", visit.SubCategories);
        AppendDictionary(html, "Surface signals", visit.SurfaceSignals);
        AppendDictionary(html, "FSS signals", visit.FssSignals);
        AppendSet(html, "Scanned body IDs", visit.BodiesScanned);
        AppendSet(html, "Scanned Codex entries", visit.CodexScanned);
        AppendSet(html, "New Codex entries", visit.CodexNew);
        html.Append("</div></div>");
        if (!string.IsNullOrWhiteSpace(notes))
        {
            html.Append("<section class=\"system-notes\"><h4>System notes</h4><p>")
                .Append(E(notes))
                .Append("</p></section>");
        }
        html.Append("</div></details></article></li>");
    }

    private static void AppendCounts(StringBuilder html, JourneyCounts counts)
    {
        html.Append("<dl class=\"facts\">");
        foreach (
            (string label, long value) in new (string, long)[]
            {
                ("Bodies catalogued", counts.BodyCount),
                ("Stars", counts.Stars),
                ("Bodies scanned", counts.BodyScans),
                ("Detailed surface scans", counts.DetailedSurfaceScans),
                ("Organisms analysed", counts.Organisms),
                ("New Codex entries", counts.NewCodexEntries),
                ("Journal touchdowns", counts.Touchdowns),
                ("Screenshot records", counts.Screenshots),
                ("Note records", counts.Notes),
                ("Biology base value (CR)", counts.ExobiologyRewards),
                ("Estimated exploration value (CR)", counts.ExplorationRewards),
            }
        )
        {
            Fact(html, label, N(value));
        }
        html.Append("</dl>");
    }

    private static void AppendDictionary(StringBuilder html, string title, IReadOnlyDictionary<string, int>? values)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }
        html.Append("<h4>").Append(E(title)).Append("</h4><dl class=\"facts\">");
        foreach ((string label, int value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Fact(html, label, N(value));
        }
        html.Append("</dl>");
    }

    private static void AppendSet<T>(StringBuilder html, string title, IReadOnlySet<T>? values)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }
        html.Append("<h4>")
            .Append(E(title))
            .Append("</h4><p class=\"identifiers\">")
            .Append(E(string.Join(", ", values.Order())))
            .Append("</p>");
    }

    private static void AppendRoute(StringBuilder html, JourneyDocument journey)
    {
        if (journey.VisitedSystems.Count == 0)
        {
            return;
        }
        GalacticCoordinate[] positions = journey.VisitedSystems.Select(visit => visit.StarSystem.Position).ToArray();
        double minX = positions.Min(p => p.X),
            maxX = positions.Max(p => p.X);
        double minZ = positions.Min(p => p.Z),
            maxZ = positions.Max(p => p.Z);
        double scale = Math.Min(744 / Math.Max(1, maxX - minX), 160 / Math.Max(1, maxZ - minZ));
        string Point(int index) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{400 + (positions[index].X - (minX + maxX) / 2) * scale:0.##},{110 - (positions[index].Z - (minZ + maxZ) / 2) * scale:0.##}"
            );
        var points = new StringBuilder();
        int stride = Math.Max(1, (positions.Length + 1023) / 1024);
        for (int index = 0; index < positions.Length; index += stride)
        {
            points.Append(Point(index)).Append(' ');
        }
        points.Append(Point(positions.Length - 1));
        html.Append("<figure class=\"route\"><div class=\"route-endpoints\"><div><span>Departure</span><strong>")
            .Append(E(journey.VisitedSystems[0].StarSystem.Name))
            .Append("</strong></div><div><span>Final recorded system</span><strong>")
            .Append(E(journey.VisitedSystems[^1].StarSystem.Name))
            .Append("</strong></div></div>")
            .Append(
                "<svg viewBox=\"0 0 800 220\" role=\"img\" aria-label=\"Recorded journey route in galactic X and Z coordinates\"><path class=\"route-grid\" d=\"M0 55H800M0 110H800M0 165H800M200 0V220M400 0V220M600 0V220\"/>"
            )
            .Append("<polyline class=\"route-path\" points=\"")
            .Append(points)
            .Append("\"/>");
        RouteMarker(html, Point(0), "departure");
        RouteMarker(html, Point(positions.Length - 1), "arrival");
        html.Append(
            "</svg><figcaption>Galactic X/Z projection. The route follows recorded visits; closely spaced stops may overlap.</figcaption></figure>"
        );
    }

    private static void RouteMarker(StringBuilder html, string point, string kind)
    {
        string[] coordinates = point.Split(',');
        html.Append("<circle class=\"")
            .Append(kind)
            .Append("\" cx=\"")
            .Append(coordinates[0])
            .Append("\" cy=\"")
            .Append(coordinates[1])
            .Append("\" r=\"5\"/>");
    }

    private static void Metric(StringBuilder html, string label, string value) =>
        html.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>");

    private static void Fact(StringBuilder html, string label, string value) => Metric(html, label, value);

    private static void Readout(StringBuilder html, string label, string value) =>
        html.Append("<span>").Append(E(label)).Append(" <strong>").Append(E(value)).Append("</strong></span>");

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string N(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset value) =>
        $"<time datetime=\"{value:O}\">{value.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture)}</time>";

    private static string Duration(DateTimeOffset start, DateTimeOffset end)
    {
        TimeSpan span = end > start ? end - start : TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m");
    }
}
