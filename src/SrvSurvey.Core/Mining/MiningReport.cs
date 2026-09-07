using System.Globalization;
using System.Net;
using System.Text;

namespace SrvSurvey.Core.Mining;

/// <summary>Portable, offline session reports. HTML also supplies a print layout for browser Save as PDF.</summary>
public static class MiningReport
{
    public static string Text(IEnumerable<MiningSession> sessions)
    {
        var output = new StringBuilder("SrvSurvey mining report\n");
        foreach (var s in sessions)
        {
            output.AppendLine($"\n{s.System} / {s.Ring} · {s.Started:O} · {s.Ship}");
            output.AppendLine($"{s.RefinedTons:0.##} t · {s.TonsPerHour:0.0} t/h · {s.ActiveDuration} · {s.Asteroids} asteroids · {s.CoreHits} cores");
            output.AppendLine(s.Notes);
            foreach (var prospect in s.Prospects) output.AppendLine($"{prospect.Time:O} · {prospect.MineralSummary} · Core: {prospect.Core}");
            foreach (var group in s.Collections.GroupBy(c => (c.Name, c.Engineering))) output.AppendLine($"{group.Key.Name}: {group.Sum(c => c.Count)} {(group.Key.Engineering ? "materials" : "t")}");
            if (s.Imported is { } imported) foreach (var field in imported.Fields) output.AppendLine($"{field.Key}: {field.Value}");
        }
        return output.ToString();
    }
    public static string Csv(IEnumerable<MiningSession> sessions)
    {
        var text = new StringBuilder("Started,System,Ring,Ship,ActiveMinutes,Tons,TonsPerHour,Asteroids,CoreHits,Prospectors,Collectors,EngineeringMaterials,Notes\r\n");
        foreach (var s in sessions)
            text.AppendLine(string.Join(',', new[] { s.Started.ToString("O"), s.System, s.Ring, s.Ship,
                s.ActiveDuration.TotalMinutes.ToString("0.00", CultureInfo.InvariantCulture), s.RefinedTons.ToString(CultureInfo.InvariantCulture),
                s.TonsPerHour.ToString("0.00", CultureInfo.InvariantCulture), s.Asteroids.ToString(CultureInfo.InvariantCulture), s.CoreHits.ToString(CultureInfo.InvariantCulture),
                s.ProspectorLimpets.ToString(CultureInfo.InvariantCulture), s.CollectorLimpets.ToString(CultureInfo.InvariantCulture), s.EngineeringCount.ToString(CultureInfo.InvariantCulture), s.Notes }.Select(Cell)));
        return text.ToString();
    }
    public static string Html(IReadOnlyList<MiningSession> sessions)
    {
        var text = new StringBuilder("""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>SrvSurvey mining report</title><style>
            :root{color-scheme:light dark;--accent:#508b9a}body{font:16px system-ui;margin:40px auto;padding:0 24px;max-width:1080px;line-height:1.5}
            h1,h2{line-height:1.2}h2{margin-top:2rem}.summary{display:flex;flex-wrap:wrap;gap:24px;padding:20px;border-block:1px solid #888}
            .metric{font-size:24px;font-weight:600}table{border-collapse:collapse;width:100%;margin:20px 0}th,td{text-align:left;padding:10px;border-bottom:1px solid #8886}
            th{font-size:12px;text-transform:uppercase}pre{white-space:pre-wrap;font:inherit}section{break-inside:avoid}.bar{display:flex;align-items:center;gap:12px;margin:8px 0}.bar label{min-width:150px}
            progress{accent-color:var(--accent);width:40%;height:22px}button{padding:10px 16px;margin-right:12px}img{max-width:100%;max-height:480px}small{opacity:.7}
            @media print{body{color:#111;background:white;margin:0;max-width:none}.tools{display:none}section{break-inside:auto}}
            </style><body><div class="tools"><button onclick="window.print()">Print / Save as PDF</button><button onclick="document.documentElement.style.colorScheme=document.documentElement.style.colorScheme==='dark'?'light':'dark'">Light / dark</button></div>
            <h1>Mining operations</h1><p>SrvSurvey · session history and prospecting analysis</p>
            """);
        text.Append(CultureInfo.InvariantCulture, $"<div class='summary'><div><div class='metric'>{sessions.Count}</div>sessions</div><div><div class='metric'>{sessions.Sum(s => s.RefinedTons):N0} t</div>refined</div><div><div class='metric'>{sessions.Sum(s => s.ActiveDuration.TotalHours):0.0} h</div>active time</div></div>");
        if (sessions.Count > 1)
        {
            text.Append("<h2>Session comparison</h2><p>Tons refined per active hour; paused time excluded.</p>");
            var maximum = Math.Max(1, sessions.Max(s => s.TonsPerHour));
            foreach (var session in sessions) Bar(text, session.System + " · " + session.Started.ToString("g"), session.TonsPerHour, maximum, "t/h");
        }
        foreach (var s in sessions)
        {
            text.Append($"<section><h2>{H(s.System)} / {H(s.Ring)}</h2><p>{H(s.Ship)} · {H(s.Started.ToLocalTime().ToString("f"))}</p>");
            text.Append(CultureInfo.InvariantCulture, $"<p class='metric'>{s.RefinedTons} t · {s.TonsPerHour:0.0} t/h · {s.ActiveDuration:hh\\:mm\\:ss}</p><p>{s.Asteroids} asteroids · {s.CoreHits} cores · {s.TonsPerAsteroid:0.00} t/asteroid · {s.ProspectorLimpets} prospectors · {s.CollectorLimpets} collectors</p>");
            if (s.Notes.Length > 0) text.Append($"<pre>{H(s.Notes)}</pre>");
            if (s.Imported is { } imported)
            {
                text.Append("<details><summary>Imported report details</summary><p>Historical summary; per-event observations were not included in this CSV.</p><table>");
                foreach (var field in imported.Fields) text.Append($"<tr><th>{H(field.Key)}</th><td>{H(field.Value)}</td></tr>");
                text.Append("</table></details>");
            }
            if (s.RefineryEstimates.Count > 0)
            {
                text.Append("<h3>Pending refinery contents (manual estimates)</h3><ul>");
                foreach (var item in s.RefineryEstimates) text.Append(CultureInfo.InvariantCulture, $"<li>{H(item.Key)}: {item.Value:0.##} t</li>");
                text.Append("</ul><p>Not included in refined tonnage or efficiency.</p>");
            }
            text.Append("<h3>Refined minerals</h3>");
            foreach (var group in s.Collections.Where(c => !c.Engineering).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) Bar(text, group.Key, group.Sum(c => c.Count), Math.Max(1, s.RefinedTons), "t");
            text.Append("<h3>Prospecting yields</h3><table><thead><tr><th>Mineral</th><th>Finds</th><th>Quality hits</th><th title='Mean proportion over asteroids containing the mineral'>Average %</th><th>Best %</th></tr></thead><tbody>");
            foreach (var material in s.Summarize(s.Thresholds)) text.Append(CultureInfo.InvariantCulture, $"<tr><td>{H(material.Name)}</td><td>{material.Finds}</td><td>{material.QualityHits}</td><td>{material.Average:0.0}</td><td>{material.Best:0.0}</td></tr>");
            text.Append("</tbody></table><details><summary>Prospecting timeline</summary><table><tr><th>Time</th><th>Minerals</th><th>Core</th></tr>");
            foreach (var prospect in s.Prospects) text.Append($"<tr><td>{H(prospect.Time.ToLocalTime().ToString("T"))}</td><td>{H(string.Join(", ", prospect.Materials.Select(m => $"{m.Name} {m.Percentage:0.0}%")))}</td><td>{H(prospect.Core)}</td></tr>");
            text.Append("</table></details><h3>Engineering materials collected</h3><ul>");
            foreach (var group in s.Collections.Where(c => c.Engineering).GroupBy(c => c.Name)) text.Append($"<li>{H(group.Key)} ×{group.Sum(c => c.Count)}</li>");
            text.Append("</ul>");
            foreach (var screenshot in s.Screenshots) AppendScreenshot(text, screenshot);
            text.Append("</section>");
        }
        return text.Append("<footer><small>Journal observations describe events recorded during the session. Refining is measured separately from cargo transfers and purchases.</small></footer></body></html>").ToString();
    }
    private static void Bar(StringBuilder text, string label, double value, double maximum, string unit) =>
        text.Append(CultureInfo.InvariantCulture, $"<div class='bar'><label>{H(label)}</label><progress max='{maximum:0.00}' value='{value:0.00}'></progress><span>{value:0.0} {unit}</span></div>");
    private static void AppendScreenshot(StringBuilder text, string path)
    {
        if (!File.Exists(path)) return;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var mime = extension switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => null };
        if (mime is null || new FileInfo(path).Length > 20 * 1024 * 1024) return;
        text.Append($"<img alt='Session screenshot' src='data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}'>");
    }
    private static string H(string text) => WebUtility.HtmlEncode(text);
    private static string Cell(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
