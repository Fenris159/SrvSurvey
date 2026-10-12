# EDAstro exploration POIs

SrvSurvey downloads the Galactic Exploration Catalog (GEC) from EDAstro when
external data is enabled and the current-system context is available. The
validated `edastro-gec.json` cache is shared across commanders and refreshed
after seven days. Refresh catalogue in Exploration requests an earlier refresh,
subject to a one-minute cooldown and the provider's quota headers. Failed
refreshes retain the previous snapshot; gameplay lookups use local data.

Exploration retains survey totals and adds a compact reference-system / nearby
POI list, category, rarity, distance and rating filters, descriptions, source
links, copy-system and **Bookmark POI** actions. System and attraction searches
are separate; entering one disables the other until it is cleared. The system
search and reference-system entry use the shared system-name autocomplete.
The reference defaults to the current system and follows arrivals until edited.
An edited reference stays fixed until **Use current system** is selected.
Distances and the nearby radius use that reference's coordinates. Unresolved
references never reuse the previous origin's distances.
The panel fills the same available width as the other workspace cards. Headers
track the result table's measured width, including its external scrollbar gutter.
Descriptions render text and linked photos in source order. Photos load only for
the selected POI when External Data is enabled, reuse the bounded image cache
under `poi-images`, and are reduced to at most 1,400 pixels wide for display.
Unavailable photos show a source-entry fallback without exposing image markup.
Saved POIs have separate external identities so multiple attractions within a
system remain distinct. Repeated saving preserves personal notes.

Existing Spansh URL imports remain the route source. Route hops gain named POI
annotations; next-jump information and Galaxy Map intelligence gain catalogue
context. Galaxy Map target selection follows journal/status evidence, with
destination / next-jump fallbacks; arbitrary hover is not exposed.

Enable **Auto-show known POIs in the current system** in **Exploration Overlay
Settings** to use the passive current-system panel. It hides during hyperspace,
while Galaxy Map/FSS is open, and in systems without known catalogue entries.
Its position, scale, theme, vehicle exclusions and global suppression use the
existing overlay settings. The **Exploration POIs** panel availability toggle
and optional visibility shortcut work like those of other panels, including
across system changes. Browser searches, origins and Legacy filtering do not
change the passive panel's current-system context.
Each visible attraction name uses the title font role. A matching system heading
serves as the POI title in Galaxy Map intelligence; the passive POI panel omits
that duplicate heading. Both editor previews include Great Annihilator and
Azura Initiative. The Galaxy Map's **Q** marker means **quest-tagged system**.

GEC descriptions are community reports. Scores and rare flags do not establish
accessibility, discovery status or biology rewards. Legacy-only POIs are hidden
by default. **New POI**, left of **Refresh catalogue**, opens the provider's
[attraction submission form](https://edastro.com/gec/new).

Content attribution: [Galactic Exploration Catalog](https://edastro.com/gec/),
[CC BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/).
Open a source entry for contributor credits and original context.

## Optional journal contribution

**Settings → Privacy & Sharing → Configure EDAstro Sharing** reviews the
contribution disclosure and saves a separate installation-wide opt-in. It is
false by default and independent of catalogue downloads. The Core
`EdAstroPublisher` consumes the existing shared journal projection; it neither
opens a second journal watcher nor changes route planning.

The JSON array follows EDDiscovery's envelope: an `appName` of `SrvSurvey-XP`,
`appVersion`, and session-captured `odyssey`, followed by journal objects.
`GET /api/accepting` is intersected with locally disclosed `CarrierStats`,
`CarrierJumpRequest`, `Docked`, `ScanOrganic`, and `CodexEntry`. Empty or failed
lookups do not permit arbitrary events. CarrierStats is limited to public
carrier identity, fuel, access and service fields. Commander/FID fields,
balances, carrier finances and `EDD*` additions are excluded. Startup/replay,
Legacy sessions and ambiguous shared data are not collected.

The bounded memory queue holds at most 4,096 events / 4 MiB; POSTs contain at
most 200 events / 1 MiB, with responses bounded to 128 KiB. Request spacing is
at least ten seconds, and the **shared process-wide** catalogue/upload budget
allows at most 100 attempts in fifteen minutes. Provider `Retry-After` and
quota headers extend that cooldown. Transient delivery failures retain data
with exponential backoff; rejection of the application identity waits fifteen
minutes. HTTP success alone is insufficient: the JSON result is checked.

Opt-out cancels in-flight requests and discards pending data. Multiple Elite
windows pause collection/delivery while keeping already captured reports.
Queued data expires after one day and is discarded on process shutdown; it is
not persisted or backed up. Repeated delivery problems are consolidated and
successes are summarized per fifteen-minute activity window. Enable this
contribution in only one application to avoid duplicates.

Application identity acceptance is not confirmed by public documentation.
There is no API key in EDDiscovery's protocol; EDAstro can reject a new sender.
Tests exercise HTTP via controlled fixtures and never submit synthetic events
to the live service. [Protocol research](research/edastro-exploration-integration.md),
[provider submission information](https://edastro.com/api-info.html).
