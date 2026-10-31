using System.Globalization;
using System.Text;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journeys;

public static partial class JourneyReport
{
    private const double MapLeft = 80;
    private const double MapRight = 926;
    private const double MapTop = 48;
    private const double MapBottom = 548;
    private const double MapCenterX = (MapLeft + MapRight) / 2;
    private const double MapCenterY = (MapTop + MapBottom) / 2;
    private const string GalacticMap = "galactic";
    private static readonly GalacticCoordinate SolPosition = new(0, 0, 0);
    private static readonly GalacticCoordinate GalacticCenter = new(25.21875, 0, 25899.96875);

    private static void AppendRoute(StringBuilder html, JourneyDocument journey)
    {
        if (journey.VisitedSystems.Count == 0)
        {
            return;
        }
        html.Append("<figure class=\"route\"><div class=\"route-endpoints\"><div><span>Departure</span><strong>")
            .Append(E(journey.VisitedSystems[0].StarSystem.Name))
            .Append("</strong></div><div><span>Final recorded system</span><strong>")
            .Append(E(journey.VisitedSystems[^1].StarSystem.Name))
            .Append("</strong></div></div>")
            .Append(
                "<div class=\"map-heading\"><h2>Across the galactic plane</h2><div class=\"map-views\" role=\"group\" aria-label=\"Route map view\">"
            )
            .Append(
                "<button type=\"button\" data-map-view=\"galactic\" aria-pressed=\"true\" aria-controls=\"map-galactic\">Galactic context</button>"
            )
            .Append(
                "<button type=\"button\" data-map-view=\"detail\" aria-pressed=\"false\" aria-controls=\"map-detail\">Route detail</button></div></div>"
            );
        AppendRouteMap(html, journey, GalacticMap);
        AppendRouteMap(html, journey, "detail");
        html.Append("<div class=\"map-references\"><span><strong>Sol</strong> X 0, Z 0 ly</span>")
            .Append("<span><strong>Sagittarius A*</strong> X 25.21875, Z 25899.96875 ly</span></div>")
            .Append(
                "<figcaption>Galactic X/Z projection, with equal distance scale on both axes. Galactic context centers on Sagittarius A* and includes Sol. Route detail fits the recorded visits. Select a point to open its history entry. Coincident points offer a visit chooser, including return visits and systems at different heights.</figcaption></figure>"
            )
            .Append(
                """
                <dialog id="map-visit-picker" aria-labelledby="map-visit-picker-title">
                  <div class="map-picker-heading"><h2 id="map-visit-picker-title">Visits at this position</h2><button type="button" data-close-map-picker>Close</button></div>
                  <p>Choose the recorded visit to open in the history.</p><div id="map-visit-options"></div>
                </dialog>
                """
            );
    }

    private static void AppendRouteMap(StringBuilder html, JourneyDocument journey, string mode)
    {
        bool galactic = mode == GalacticMap;
        GalacticCoordinate[] positions = journey.VisitedSystems.Select(visit => visit.StarSystem.Position).ToArray();
        var projection = RouteMapProjection.Create(positions, galactic);
        Svg(
            html,
            $"""<div class="route-map-scroll" id="map-{mode}" data-map-panel="{mode}"{(galactic ? "" : " hidden")}><svg class="route-map" data-map="{mode}" viewBox="0 0 960 620" role="group" aria-labelledby="map-title-{mode}"><title id="map-title-{mode}">"""
        );
        html.Append(
                galactic
                    ? "Journey in the galactic plane, centered on Sagittarius A*"
                    : "Recorded journey route in galactic X and Z coordinates"
            )
            .Append("</title><desc>Positive Z is upward. Each destination point links to its recorded visit.</desc>");
        Svg(
            html,
            $"""<rect class="map-frame" x="{MapLeft}" y="{MapTop}" width="{MapRight - MapLeft}" height="{MapBottom - MapTop}"/>"""
        );
        AppendMapGrid(html, projection);
        html.Append("<polyline class=\"route-path\" points=\"");
        for (int index = 0; index < positions.Length; index++)
        {
            if (index > 0)
            {
                html.Append(' ');
            }
            MapPoint point = projection.Project(positions[index]);
            Svg(html, $"{point.X:0.###},{point.Y:0.###}");
        }
        html.Append("\"/>");
        AppendMapReference(html, projection, "Sol", SolPosition, "Coordinate origin");
        AppendMapReference(html, projection, "Sagittarius A*", GalacticCenter, "Galactic center");
        for (int index = 0; index < journey.VisitedSystems.Count; index++)
        {
            AppendMapStop(html, journey.VisitedSystems[index], index, journey.VisitedSystems.Count, projection);
        }
        Svg(
            html,
            $"""<text class="map-axis-title" x="{MapCenterX}" y="602" text-anchor="middle">X (light-years)</text><text class="map-axis-title" x="17" y="{MapCenterY}" text-anchor="middle" transform="rotate(-90 17 {MapCenterY})">Z (light-years)</text>"""
        );
        html.Append("</svg></div>");
    }

    private static void AppendMapGrid(StringBuilder html, RouteMapProjection projection)
    {
        double step = MapTickStep(70 / projection.Scale);
        double minX = projection.CenterX + (MapLeft - MapCenterX) / projection.Scale;
        double maxX = projection.CenterX + (MapRight - MapCenterX) / projection.Scale;
        double minZ = projection.CenterZ - (MapBottom - MapCenterY) / projection.Scale;
        double maxZ = projection.CenterZ - (MapTop - MapCenterY) / projection.Scale;
        if (
            !double.IsFinite(step)
            || !double.IsFinite(minX)
            || !double.IsFinite(maxX)
            || !double.IsFinite(minZ)
            || !double.IsFinite(maxZ)
        )
        {
            return;
        }
        AppendMapTicks(html, projection, minX, maxX, step, horizontal: true);
        AppendMapTicks(html, projection, minZ, maxZ, step, horizontal: false);
    }

    private static void AppendMapTicks(
        StringBuilder html,
        RouteMapProjection projection,
        double minimum,
        double maximum,
        double step,
        bool horizontal
    )
    {
        double first = Math.Ceiling(minimum / step) * step;
        for (int index = 0; index < 32; index++)
        {
            double value = first + index * step;
            if (value > maximum)
            {
                break;
            }
            string css = value == 0 ? "route-grid map-origin" : "route-grid";
            string label = N(value == 0 ? 0 : value, step >= 1 ? "N0" : "0.##");
            MapPoint point = projection.Project(
                horizontal ? new(value, 0, projection.CenterZ) : new(projection.CenterX, 0, value)
            );
            if (horizontal)
            {
                Svg(
                    html,
                    $"""<path class="{css}" d="M{point.X:0.###} {MapTop}V{MapBottom}"/><text class="map-tick" x="{point.X:0.###}" y="572" text-anchor="middle">{label}</text>"""
                );
            }
            else
            {
                Svg(
                    html,
                    $"""<path class="{css}" d="M{MapLeft} {point.Y:0.###}H{MapRight}"/><text class="map-tick" x="70" y="{point.Y + 4:0.###}" text-anchor="end">{label}</text>"""
                );
            }
        }
    }

    private static double MapTickStep(double distance)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(distance)));
        double normalized = distance / magnitude;
        double multiple = normalized switch
        {
            <= 1.5 => 1,
            <= 3 => 2,
            <= 7 => 5,
            _ => 10,
        };
        return multiple * magnitude;
    }

    private static void AppendMapReference(
        StringBuilder html,
        RouteMapProjection projection,
        string name,
        GalacticCoordinate position,
        string description
    )
    {
        MapPoint point = projection.Project(position);
        if (point.X < MapLeft || point.X > MapRight || point.Y < MapTop || point.Y > MapBottom)
        {
            return;
        }
        bool labelOnLeft = point.X > MapRight - 180;
        double labelX = point.X + (labelOnLeft ? -15 : 15);
        string anchor = labelOnLeft ? "end" : "start";
        Svg(
            html,
            $"""<g class="map-reference" data-reference="{E(name)}" data-x="{position.X:R}" data-z="{position.Z:R}"><title>{E(name)}: X {position.X:R}, Z {position.Z:R} light-years</title><circle cx="{point.X:0.###}" cy="{point.Y:0.###}" r="9"/><text class="map-reference-name" x="{labelX:0.###}" y="{point.Y - 7:0.###}" text-anchor="{anchor}">{E(name)}</text><text class="map-reference-detail" x="{labelX:0.###}" y="{point.Y + 11:0.###}" text-anchor="{anchor}">{E(description)}</text></g>"""
        );
    }

    private static void AppendMapStop(
        StringBuilder html,
        JourneySystemVisit visit,
        int index,
        int count,
        RouteMapProjection projection
    )
    {
        MapPoint point = projection.Project(visit.StarSystem.Position);
        string kind = (index == 0 ? " departure" : "") + (index == count - 1 ? " arrival" : "");
        int radius = index == 0 || index == count - 1 ? 5 : 3;
        string label = $"Visit {index + 1}: {visit.StarSystem.Name}";
        string arrived = visit.Arrived.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
        Svg(
            html,
            $"""<a class="route-stop-link" href="#visit-{index + 1}" data-visit="visit-{index + 1}" data-arrived="{E(arrived)}" aria-label="{E(label)}" tabindex="0"><title>{E(label)}; X {visit.StarSystem.Position.X:R}, Z {visit.StarSystem.Position.Z:R} ly</title><circle class="route-stop-hit" cx="{point.X:0.###}" cy="{point.Y:0.###}" r="12"/><circle class="route-stop{kind}" cx="{point.X:0.###}" cy="{point.Y:0.###}" r="{radius}"/></a>"""
        );
    }

    private static void Svg(StringBuilder html, FormattableString markup) =>
        html.Append(markup.ToString(CultureInfo.InvariantCulture));

    private readonly record struct MapPoint(double X, double Y);

    private readonly record struct RouteMapProjection(double CenterX, double CenterZ, double Scale)
    {
        public MapPoint Project(GalacticCoordinate position) =>
            new(MapCenterX + (position.X - CenterX) * Scale, MapCenterY - (position.Z - CenterZ) * Scale);

        public static RouteMapProjection Create(GalacticCoordinate[] positions, bool galactic)
        {
            double x = galactic
                ? GalacticCenter.X
                : positions.Min(point => point.X) / 2 + positions.Max(point => point.X) / 2;
            double z = galactic
                ? GalacticCenter.Z
                : positions.Min(point => point.Z) / 2 + positions.Max(point => point.Z) / 2;
            IEnumerable<GalacticCoordinate> extent = galactic
                ? positions.Append(SolPosition).Append(GalacticCenter)
                : positions;
            double radius = Math.Max(1, extent.Max(point => Math.Max(Math.Abs(point.X - x), Math.Abs(point.Z - z))));
            double scale = Math.Min((MapRight - MapLeft) / 2, (MapBottom - MapTop) / 2) / radius * 0.85;
            return new(x, z, scale);
        }
    }

    private const string RouteMapStyles = """
        .map-heading{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:14px;margin:28px 0 8px}.map-heading h2{font-size:22px;margin:0}.map-views{display:flex;flex-wrap:wrap;gap:8px}.map-views button{font-size:13px;padding:7px 11px}.map-views button[aria-pressed=true]{color:var(--space);background:var(--accent);border-color:var(--accent)}
        .route-map-scroll{overflow-x:auto}.route-map{display:block;width:100%;min-width:640px;height:auto;max-height:none;margin:0}.map-frame{fill:none;stroke:var(--line);stroke-width:1}.route-grid{stroke:var(--line);stroke-width:.6;opacity:.45;fill:none}.map-origin{opacity:.9;stroke-width:1.1}.map-tick,.map-axis-title{fill:var(--muted);font:12px "Segoe UI",Arial,sans-serif}.map-axis-title{font-size:14px}.route-path{stroke-width:1.6}
        .route-stop-link{cursor:pointer}.route-stop-hit{fill:transparent;stroke:none}.route-stop{fill:var(--accent);stroke:var(--surface);stroke-width:1.2}.route-stop.departure{fill:var(--surface);stroke:var(--text);stroke-width:2}.route-stop.arrival{fill:var(--accent);stroke:var(--text);stroke-width:2}.route-stop-link:hover .route-stop,.route-stop-link:focus .route-stop{r:6;fill:var(--text);stroke:var(--accent);stroke-width:2}
        .map-reference circle{fill:var(--surface);stroke:var(--muted);stroke-width:1.2}.map-reference-name,.map-reference-detail{fill:var(--text);font:600 15px Bahnschrift,"Trebuchet MS",sans-serif;paint-order:stroke fill;stroke:var(--surface);stroke-width:4px;stroke-linejoin:round}.map-reference-detail{fill:var(--muted);font:12px "Segoe UI",Arial,sans-serif}.map-references{display:flex;flex-wrap:wrap;gap:8px 28px;font-size:13px;color:var(--muted);margin:0 0 12px}.map-references strong{color:var(--text);font-weight:500;margin-right:8px}.visit:target{border-left-color:var(--accent);background:var(--surface)}
        #map-visit-picker{box-sizing:border-box;color:var(--text);background:var(--surface);border:1px solid var(--accent);border-radius:8px;width:min(480px,calc(100vw - 40px));max-height:75vh;overflow:auto;padding:20px}#map-visit-picker::backdrop{background:rgba(0,0,12,.8)}.map-picker-heading{display:flex;align-items:center;justify-content:space-between;gap:16px}.map-picker-heading h2{margin:0;font-size:22px}#map-visit-picker p{color:var(--muted);font-size:14px}.map-visit-option{display:block;color:var(--text);padding:14px 4px;border-top:1px solid var(--line);text-decoration:none}.map-visit-option:hover,.map-visit-option:focus{color:var(--accent)}.map-visit-date{display:block;color:var(--muted);font-size:13px;margin-top:4px}
        @media(max-width:700px){.map-heading{align-items:start}.map-heading h2{font-size:20px}.map-references{flex-direction:column;gap:5px}}
        @media print{.route{--surface:#f1f6fa;--text:#152331;--muted:#425568;--line:#a6bdcf;--accent:#164c7d;--instrument:#e4ecf3}.route-map{min-width:0;max-height:460px}.route-map-scroll{overflow:visible}.map-views{display:none}#map-visit-picker{display:none!important}}
        """;

    private const string RouteMapScript = """
        document.querySelectorAll('[data-map-view]').forEach(button=>button.addEventListener('click',()=>{
          const view=button.dataset.mapView;
          document.querySelectorAll('[data-map-panel]').forEach(panel=>panel.hidden=panel.dataset.mapPanel!==view);
          document.querySelectorAll('[data-map-view]').forEach(control=>control.setAttribute('aria-pressed',String(control===button)));
        }));
        const mapVisitPicker=document.getElementById('map-visit-picker');
        mapVisitPicker?.querySelector('[data-close-map-picker]').addEventListener('click',()=>mapVisitPicker.close());
        function openMapVisit(link){
          const row=document.getElementById(link.dataset.visit);
          if(!row)return;
          search.value='';search.dispatchEvent(new Event('input'));
          row.querySelector('details').open=true;row.tabIndex=-1;row.focus({preventScroll:true});
        }
        document.querySelectorAll('.route-map').forEach(map=>{
          const positions=new Map();
          map.querySelectorAll('.route-stop-link').forEach(link=>{
            const dot=link.querySelector('.route-stop');
            const key=dot.getAttribute('cx')+','+dot.getAttribute('cy');
            if(!positions.has(key))positions.set(key,[]);
            positions.get(key).push(link);
          });
          positions.forEach(visits=>visits.forEach(link=>link.addEventListener('click',event=>{
            if(visits.length===1||event.detail===0){openMapVisit(link);return;}
            event.preventDefault();
            const options=mapVisitPicker.querySelector('#map-visit-options');
            options.replaceChildren();
            visits.forEach(visit=>{
              const option=document.createElement('a');
              option.className='map-visit-option';option.href=visit.getAttribute('href');
              option.textContent=visit.getAttribute('aria-label');
              const date=document.createElement('span');date.className='map-visit-date';date.textContent=visit.dataset.arrived;option.append(date);
              option.addEventListener('click',()=>{mapVisitPicker.close();openMapVisit(visit);});
              options.append(option);
            });
            mapVisitPicker.showModal();
          })));
        });
        """;
}
