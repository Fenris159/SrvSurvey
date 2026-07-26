# SrvSurvey cross-platform porting plan

Last validated: 2026-07-25

## Goal and definition of done

Port SrvSurvey from its Windows-only WinForms/SharpDX implementation to a supported
desktop application for Windows and Linux without silently dropping user data,
journal events, overlays, input behavior, localization, or existing survey
workflows.

The port is complete only when:

1. The Windows and Linux packages start on clean supported systems.
2. The same journal fixtures produce equivalent domain state in the legacy and
   cross-platform applications.
3. Each feature in the parity matrix below has automated tests and recorded
   Windows and Linux runtime results.
4. Existing settings and commander data are either read in place or migrated
   once with a backup and a documented rollback path.
5. Platform limitations are visible to the user instead of being reported as
   successful behavior.

An application that merely opens an Avalonia window is a foundation milestone,
not a completed port.

## Audit of the initial branch work

The initial three port commits (`454881bf`, `0bd711ad`, and `ceac0fa2`) did not
contain the implementations described by their commit messages:

| Area | Claimed state | Validated state at `ceac0fa2` |
| --- | --- | --- |
| Porting plan | Complete sequential plan | Three-line pointer to unavailable “local artifacts” |
| Core library | Models, resource loading, colonization logic | One speculative record and two comment-only files |
| Avalonia UI | Integrated MVVM desktop application | Invalid project, invalid XAML, and comment-only C# files |
| Solution integration | Cross-platform projects included | Neither new project was in `SrvSurvey.sln` |
| CI | Windows, Linux, AppImage, and container builds | Comment-only workflow |
| Container | Multi-stage build | Comment-only Dockerfile |
| Documentation | Foundation complete | Original README replaced by one inaccurate sentence |

The legacy application is a substantial porting source: the audited tree has
231 C# files (about 93,500 lines), 70 WinForms designer files, 340 resource
files, and at least 51 C# files with direct WinForms, SharpDX, or native Win32
dependencies. Core behavior must be extracted deliberately; it cannot be
validated by recreating similarly named types.

## Target architecture

Keep the production application available while building a second executable in
vertical slices:

```text
SrvSurvey (legacy WinForms)
    Existing production behavior and comparison oracle

src/SrvSurvey.Core
    Journal and status ingestion
    Domain state and calculations
    Persistence contracts and migrations
    No Avalonia, WinForms, SharpDX, or platform P/Invoke

src/SrvSurvey.Desktop
    Avalonia views and view models
    Composition root
    Platform capability reporting

src/SrvSurvey.Platform.*
    Small Windows/Linux implementations for game-window tracking,
    overlay behavior, global input, notifications, and filesystem locations

tests/SrvSurvey.Core.Tests
    Journal fixtures, calculations, persistence, and migration tests
```

Use a separate cross-platform solution so Linux builds never evaluate the
Windows Application Packaging Project in `SrvSurvey.sln`.

## Porting rules

- Port observable behavior from the source and fixtures, not type names from
  memory.
- Move domain logic only after its inputs, outputs, units, edge cases, and
  persistence effects are understood.
- Keep platform services behind interfaces. Platform checks do not belong
  throughout the domain model.
- Preserve on-disk JSON compatibility where practical. Any schema change needs
  fixture coverage, a backup, a migration version, and rollback instructions.
- Treat malformed or partially written journal/status files as expected input.
- Keep network publication opt-in and preserve the legacy privacy controls.
- Do not mark a phase complete from compilation alone. Record runtime evidence
  per operating system and display server.

## Platform capability matrix

The Linux target must distinguish X11 and Wayland. Wayland compositors commonly
restrict global input, absolute window positioning, and click-through overlays;
these behaviors require runtime probes and may remain unavailable on some
desktops.

| Capability | Windows | Linux X11 | Linux Wayland |
| --- | --- | --- | --- |
| Read journals/status files | Required | Required | Required |
| Transparent topmost overlays | Required | Validate | Probe compositor support |
| Click-through overlay mode | Required | Validate | May be unavailable |
| Follow Elite game window | Required | Validate | May be unavailable |
| Global keyboard/controller input | Required | Validate replacement | Keyboard portal/compositor dependent; SDL controllers available |
| VR/Direct3D integration | Preserve as Windows adapter | Not initially planned | Not initially planned |

Unavailable capabilities must disable dependent features with an explanation;
they must not fail silently.

## Sequential delivery plan

### Phase 0 — Correct and make the foundation executable

- [x] Audit the existing branch claims against committed files.
- [x] Restore the project README and replace the placeholder plan.
- [x] Remove case-colliding repository paths.
- [x] Create buildable Core, Desktop, and test projects.
- [x] Add a cross-platform solution that excludes Windows packaging projects.
- [x] Add Windows and Linux CI restore, build, test, and publish smoke checks.
- [x] Make the Dockerfile a reproducible Linux build environment, or remove it.

Exit gate: a clean clone restores, builds, tests, and publishes on Windows and
Linux. Launching the desktop shell is manually smoke-tested on both platforms.

### Phase 1 — Journal bootstrap vertical slice

- [x] Resolve the journal folder from an explicit setting first.
- [x] Offer platform-specific candidate locations without assuming one exists.
- [x] Read the newest journal safely, including an incomplete final line.
- [x] Extract commander, game version, mode, system, body, and shutdown state
  present in the newest file, including the legacy current-planet semantics.
- [x] Rebuild bootstrap state across rotated/prior journals when the newest file
  does not contain all session-identifying events.
- [x] Watch journal rotation and `Status.json` with cancellation and retry logic.
- [x] Show the state and any path/parse errors in the desktop shell.

Exit gate: shared fixtures and a live journal session produce the expected state
on Windows and Linux.

### Phase 2 — Settings, commander data, and resources

- [x] Inventory every imported file recursively with size and SHA-256 evidence.
- [x] Define OS-appropriate config/data/cache locations while discovering the
  desktop and Microsoft Store legacy locations on Windows.
- [x] Implement a backup-first legacy data importer that verifies the backup and
  staged destination before activation and never mutates the source. The
  activated directory and its manifest are now hashed again in place before
  the rollback copy is released; a post-swap mismatch restores the prior
  cross-platform profile automatically.
- [x] Add the five Raven Colonial shell themes with native light/dark modes and
  an isolated persisted preference.
- [ ] Port theme, localization, and static JSON/image resource loading.
  The original seven non-English/pseudo catalogs now generate a deterministic
  UTF-8 Avalonia catalog with all 1,090 source strings per language. Startup
  applies the imported `lang` preference before constructing controls, literal
  control text is translated without replacing bindings, and Settings persists
  later changes only in the isolated cross-platform file with a controlled
  restart. A markup coverage audit currently matches 10 of 983 unique Avalonia
  literals to the legacy source catalog; the remaining 973 port-only literals
  and bound/dynamic presentation strings still require explicit translations,
  so localization parity remains open.
- [x] Test unknown fields, corrupt files, concurrent writes, and copied-profile
  upgrades.

The importer now upgrades the two retired organic-history formats that the
WinForms startup previously converted in place: commander `scannedOrganics`
plus three/four-part `scannedBioEntryIds`, and per-body files under
`organic/<FID>`. Conversion runs only against the checksum-verified activated
copy; the selected source and verified rollback backup remain byte-identical.
System/body merges are additive and atomic, retain unknown target and organism
fields, repair weak entry IDs and first-footfall rewards, and use per-profile
completion markers so startup retries are idempotent. Malformed values,
overflowing rewards, and symbolic links fail closed: their bytes are preserved,
the completion marker is not written, and Diagnostics identifies the file.

Imported published-reference caches are also active rather than merely copied.
The catalog resolver recognizes the original `codexRef.json`, `pub/bio-criteria`,
Guardian indexes/surveys/templates, human-settlement templates, and `pub/ggg.json`
layout. Every candidate must parse successfully and meet or exceed the shipped
catalog's coverage before it can replace that embedded catalog. Malformed ZIPs,
partial JSON, and syntactically valid but truncated catalogs remain byte-identical
on disk, fall back independently to the embedded baseline, and are reported in
Diagnostics/logs. The same resolved biology catalogs now feed the main survey,
predictions, status, Codex, and overlay paths so imported data cannot split the
application into inconsistent reference views.

The imported root `codexNotFound.json` cache is now validated and consumed as
well. Its region and entry identifiers remain read-only during migration,
malformed or oversized files fail closed without changing their bytes, and
biology/prediction surfaces keep global regional candidates distinct from both
commander-first and commander regional-first discoveries.

The imported `pub/Boxel.Names.txt` map is also loaded read-only with UTF-8,
file-size, line-length, entry-count, and complete-envelope validation. Its
scalar and multi-address legacy rows restore hand-authored system-name lookup
for Boxel search activation, and the resulting `name|ID64` state now round-trips
through the commander profile without losing the decoded generated geometry.
Malformed or truncated maps remain byte-identical and fail closed.

The port also preserves the legacy multi-client cargo safety rule. Because
Elite's shared `Cargo.json` cannot be attributed to a commander while multiple
game windows are running, cached cargo is cleared and new file snapshots are
suppressed for dock-to-dock logging, Guardian artifact guidance, Colonisation
planning/publishing, and quest auxiliary payloads. Quest events fall back to
their journal payload with an explicit warning, and the application waits for a
new `Cargo.json` write after returning to one game window instead of reusing
possibly contaminated state.

Exit gate: a copied real user profile opens without mutation, and an imported
profile matches the legacy application after restart.

### Phase 3 — Domain state and journal parity

- [ ] Port journal event models from observed payloads.
- [ ] Port system/body, commander, organic scan, guardian, human settlement,
  mission, cargo, colonization, and quest state in review-sized groups.
- [ ] Create golden journal fixtures for each group.
- [ ] Compare serialized state or a documented equivalent projection against
  the legacy implementation.

The legacy dispatch audit now accounts for all 68 concrete event handlers in
`Game`: 66 already had an exact modern consumer, and the two uncovered events,
`MaterialTrade` and `TechnologyBroker`, now update the bounded in-memory
material inventory used by pickup-total notifications. Trade events validate
both sides before changing either count, unknown paid materials fail closed,
broker entries are isolated, and later `MaterialCollected` totals have fixtures
covering both event shapes and malformed-trade refusal. The broader golden
fixture/projection inventory remains open above.

The death/resurrection and main-menu lifecycle now matches the legacy split
between durable commander data and disposable live context. `Died`, `Resurrect`,
`Shutdown`, and `Music/MainMenu` clear the current body, SRV/fighter, carrier
dock/depot, Guardian site, human settlement/vehicle, and ground-combat session
without deleting system survey history or active massacre missions. This also
prevents a stale Fleet Carrier dock from authorizing Raven cargo deltas after a
rebuy. Focused reducer and Avalonia presentation fixtures cover each exit path.

Exit gate: the supported event inventory has a fixture and parity result; unknown
events remain non-fatal and observable.

### Phase 4 — Overlay and input infrastructure

- [x] Implement Avalonia overlay primitives, scaling, theme, and multi-monitor
  coordinates.
- [x] Add platform adapters for topmost/click-through behavior and game-window
  tracking.
- [x] Replace SharpDX input with maintained APIs and preserve configurable
  keyboard and controller bindings.
- [x] Define behavior for unsupported Wayland capabilities.
- [ ] Measure overlay update cost while the game is active.

The global overlay scale now preserves all 26 legacy combo-box indices,
including the non-monotonic 90% through 50% tail. Native mode leaves Avalonia's
per-monitor scaling intact; forced percentages compensate for the active
monitor render scale so 100% retains the WinForms 96-DPI meaning. Every
detached overlay receives the shared layout transform, window constraints and
placement size, including the dynamically sized human-settlement map. The
imported preference and later settings changes preserve unknown JSON fields;
mapping, DPI math, persistence, migration, and settings bindings are automated,
while rendered scale QA remains in the final visual pass.

The original `FormMultiFloatie` multi-process cue is also restored as a thin,
non-activating Commander label. It refreshes the Elite window inventory every
five seconds, appears while either Elite or an SrvSurvey window has focus,
follows the active game client, participates in temporary/suit suppression,
and closes for a single, minimized, or unavailable game window. Windows and
X11 use the shared native click-through and tracking safety gates; Wayland
fails closed. `Ctrl+Alt+W` continues to cycle the available Elite windows, and
the legacy `hideMultiFloatie` preference migrates into a live Settings toggle
without replacing other current or imported settings. Inventory, gating,
persistence, migration-upgrade, and XAML compilation coverage pass; visual and
live multi-client verification remain in the requested final pass.

The first consumer is the detached Guardian live map/current-obelisk surface.
Windows uses native extended window styles plus client-area tracking; X11 uses
XShape input regions plus EWMH/Xlib window discovery. Both follow the active
Elite client area, account for monitor scaling, hide when Elite is minimized or
not foreground, and fail closed if click-through cannot be enabled. Wayland
keeps detached overlays disabled because absolute placement and input regions
are compositor-controlled. SharpHook now provides opt-in global keyboard input
on Windows and X11, while SDL3 provides standard gamepad, joystick, and HOTAS
discovery/polling on Windows and Linux with reconnecting stable device IDs and
legacy chord-release behavior. Native Elite input checks with physical hardware
and Windows/X11 overlay runtime checks are still required.

The detached Guardian coordinator now also supplies the original
`PlotGuardianSystem` and `PlotRamTah` roles. The system summary uses the legacy
left/top anchor, game-panel and supercruise gates, current-system site list,
survey progress, and navigation-destination marker. The right-middle Ram Tah
surface uses the active mission/site pairing, surface-position and vehicle/panel
gates, incomplete-log grouping, obelisk names, and live artifact counts. Their
legacy-default preferences and Raven Colonial build-project suppression persist
in Settings. Both windows compile and their state/placement are automated; per
request, visual/theme QA is deferred to the final whole-application UI pass.

The detached live-site surface now also restores the Guardian alignment and
glide approach roles. Heading mode supplies the ruins measurement guide; origin
mode selects the ruins Alpha/Beta/Gamma or structure-specific reticle, applies
the migrated target altitudes and visibility preferences, and fades against
the original altitude band. Glide mode retains the site/type/blueprint prompt.
The window follows the original surface, vehicle, panel, FSD, and active-build-
project gates. These states and lossless settings updates are automated; visual
and in-game verification remains deferred to the final pass.

`PlotJumpInfo` is now a second complete automatic-lifecycle consumer. It selects
the latest journal `FSDTarget` ahead of the status destination, falls back from a
short `NavRoute.json` route to the active followed route, preserves `Alt+D`, and
shows proportional hop distance, scoopable stars, boosted legs, discovery,
traffic, body/biology/station counts, brokers, traders, engineers, Guardian
sites, followed-route notes, and galactic-region transitions. EDSM and Spansh
requests are independent and failure-tolerant so one unavailable provider does
not interrupt journal monitoring. Persisted automatic, compact, and selected
next-hop preferences are exposed in Settings. Windows dark/light presentation
has been checked; attachment to a live Elite client and Linux remain open.

`PlotFSSInfo` and `PlotSysStatus` now share a journal-derived current-system
model rather than WinForms-owned caches. It tracks body type and detail, scan
and DSS valuation, discovery/mapping, signals and analyzed progress, rings,
materials, destination context, FSS completion, and non-body signals. The
Raven-themed top-left body feed preserves value/signal filtering, `Alt+F`, and
FSS/system-map/navigation-panel modes; the bottom-left status surface preserves
FSS percentage, filtered DSS candidates, destination grouping, biology, and
optional non-body counts. Guardian-system priority is retained, with a forced
FSS view taking precedence. Windows dark/light presentation has been checked;
live Elite attachment and Linux remain open.

The journal-backed portion of `PlotFSS` is also implemented as a passive
top-center last-scan card. It retains the latest detailed standalone planet,
ignores stars, belt clusters, and ring children, and shows discovery,
terraformable/landable state, distance, scan and mapped values, and biological
signal count. The legacy experimental `watchFssSettings_TEST` screen-pixel
tuning detector is now implemented with the original scan/skip state machine,
color tolerances, watch-area geometry, white/yellow threshold, and status cues.
Its nested legacy settings migrate losslessly, including explicit disablement
and optional diagnostic captures. Native bounded capture adapters cover Windows
and X11; Wayland reports the platform limitation without attempting unsafe
capture. Blue dark/light presentation has been checked at the active Windows
scaling, but the new detector indicator intentionally awaits final visual QA;
live Elite attachment and Linux remain open.

`PlotBodyInfo` now has a Raven-themed passive replacement with the original
map/orrery, DSS, orbit/glide, optional surface-analysis, Sol-bubble, and `Alt+B`
visibility rules. It selects current versus status-destination bodies by mode,
supports unscanned targets, and renders discovery/mapping, scan and mapped
values, temperature, high-gravity warnings, pressure, biological/geological
signals, volcanism, atmosphere composition, materials, and rings. Forced body
information retains its legacy priority over the Guardian summary. A dense
synthetic body was checked in Blue dark/light at the active Windows scaling.
The exact biology evaluator supplies the predicted reward range on this
separate `PlotBodyInfo` surface; live Elite attachment and Linux remain open.

`PlotBioSystem` now has a Raven-themed passive replacement with its legacy
whole-system, FSS-last-body, and near/current/target-body selection rules. The
shared reducer retains genus/species/variant identities, localized names,
Codex IDs, rewards, analyzed state, regional-first state, and geological names.
The overlay renders active sampling, analyzed dimming, partial and confirmed
reward totals, first-footfall value, DSS guidance, geology, and unknown slots;
its persisted settings and Guardian suppression are wired into the shared
coordinator. Selecting a biological body in the System Map, Orrery, or
navigation panel now retains its detail card for two seconds per organism with
the original countdown behavior. The embedded v4 criteria catalog now supplies exact species and
variant predictions plus body/system reward ranges when scan context is
complete. It covers all 21 shipped criteria resources, galactic regions,
parent/barycentre ancestry and relative brightness, legacy star aliases, the
offline nebula catalog, Guardian bubbles, common-child inheritance, and known
genus/species suppression. Missing inputs stay explicit instead of producing
false exact matches, and disabling predictions refreshes the overlay
immediately. Dense body views, a three-body system overview, and an exact
prediction card were checked in Blue dark/light. Global and current-region
Commander Codex ledgers now infer commander and regional firsts for confirmed
and predicted organisms. A bounded shared Canonn request cache also restores
the known-signal hint for a selected nonlocal body without duplicating the
prior-scan radar request. Live Elite attachment and Linux remain open; the
restored auxiliary cues intentionally await the final visual pass.

`FormPredictions` now has a single-instance Raven-themed workspace backed by
the same exact evaluator. It shows system totals, confirmed and estimated
rewards, first-footfall estimates, expandable per-body organism rows, sample
separation distances, incomplete-scan guidance, current/target state, and
Canonn, Spansh, and EDSM actions. Current-body focus and compact, comfortable,
or large row sizing persist across launches. The 1040 by 760 logical-pixel
window was exercised in Blue dark/light, including focus, row-size, expand,
and collapse interactions. Commander-Codex first-discovery flags use the same
live ledger context as the passive biology card; live Elite attachment and
Linux remain open.

`FormCodexBingo` now has a single-instance Raven-themed workspace backed by the
complete 1,070-entry Codex hierarchy. It reads the same global and 42-region
commander ledgers populated by live journals, can import the Canonn Challenge
and bounded historical journal files, and retains explicit confirmation for
manual discoveries. Commander and region selectors, recursive progress,
entry/location detail, Canonn/Bioforge/EDAstro/Spansh actions, and handoff to
the integrated nearest-signal or missing-variant search are implemented. An
isolated three-entry ledger was exercised in Blue dark/light, including tree
expansion, selection details, retained window state, disabled actions, and
cleanup. Live Elite updates, interactive remote imports, and Linux remain open.

`PlotBioStatus` now has a compact top-center passive replacement with its
separate default-on preference. It retains current-body gating, DSS-required
guidance, genus and geology summaries, analyzed progress, stale-body sample
warnings, the active one/two/three sample state, required and live separation
distance, reward and first-footfall value, jump/Guardian priority, the most
recent Composition Scanner Codex cue and image availability, and the original
live `.show` command handoff with entry selection. Active,
summary, and DSS states were checked at the legacy 480-pixel width in Blue
dark/light. The explicitly `_TEST` temperature diagnostic is now an opt-in,
migrated setting backed by the exact species criterion range, body baseline,
and live suit temperature; its visual check, live Elite attachment, and Linux
remain open.

Exit gate: overlay positioning, DPI scaling, focus, click-through behavior, and
input are recorded on the supported platform matrix.

### Phase 5 — Primary user features

Port and validate one complete feature at a time:

1. Organic scan tracking and distance guidance.
2. Latitude/longitude ground target guidance.
3. Guardian ruins, structures, beacons, maps, and survey persistence.
4. Core automatic overlay lifecycle driven by game state.

The legacy `guardianComponentMaterials_TEST` mode is now an opt-in migrated
Guardian setting. Its compact tower/panel strings are parsed into a typed model,
merged back into existing JSON without dropping malformed or future array
entries, and retained through live-state and editor copies. The survey editor
supports top/middle/bottom tower materials and single-material destructible
panels; enabled maps render the original lime/cyan/orange-red cues and include
panels in nearest-object tracking without adding them to normal completion
totals. A profile-import integration fixture proves the source, verified backup,
and activated Guardian survey remain byte-identical before the imported data is
read by the new model. Visual/theme QA remains deferred to the final pass.

Exit gate per feature: journal fixtures, calculation tests, persistence tests,
UI tests where practical, and live Windows/Linux evidence.

### Phase 6 — Remaining feature parity

- [ ] System/body/FSS information and route overlays.
  Followed-route Galaxy Map guidance, `PlotJumpInfo`, `PlotFSSInfo`, and
  `PlotSysStatus` are implemented. The journal-backed `PlotFSS` card is
  implemented together with its migrated cross-platform pixel tuning detector;
  `PlotBodyInfo` selection, visibility, presentation, and shared biological
  reward prediction range are implemented. `PlotFlightWarning` is implemented
  with the exact landable-body,
  gravity-threshold, flight-mode, and persisted auto-show rules. The
  journal-backed `PlotBioSystem` presentation, lifecycle, timed map selection,
  and exact prediction inputs plus Commander Codex first-discovery and Canonn
  known-signal cues are implemented. The rare legacy first-footfall fallback
  now samples the same configurable HUD colour region on Windows and X11 only
  for a live, unknown body in an unpopulated system; bootstrap replay, Guardian
  sites, unavailable capture, and a hidden/background Elite window fail closed.
  Inferred and manual corrections update organic rewards and legacy system
  history together, including an explicit atomic path that can safely clear a
  previously persisted positive correction. `FormPredictions` is implemented as a standalone system/body
  workspace. `PlotPriorScans` and its Canonn slice of the grounded radar are
  implemented with validated current-body coordinates, commander/analyzed and
  value filtering, continuously recalculated surface bearing/distance,
  approach angle, near/far state, and persisted radar-circle preferences. The
  journal-backed `PlotGrounded` and `PlotTrackers` replacement now preserves legacy
  touchdown, bookmark, and completed-scan records, active samples, sample
  removal rules, exact surface-mode gating, five radar sizes, heading-relative
  circles, ship/SRV markers, Composition Scanner auto-tracking, cross-system
  death marking, adjustable radar scale, all eight quick-location chords, and a
  compact tracker-only Raven state plus the dedicated `PlotMiniTrack`
  replacement. Human-site arbitration is wired into the same coordinator.
  `PlotSphericalSearch` now
  consolidates spherical-limit, Boxel-search, and followed-route Galaxy Map
  guidance at the original top-right anchor.
- [x] Human settlements and post-processing tools. The passive human-settlement
  map now covers all 28 templates, compatible-site and mode gates, docking and
  manual foot alignment, lossless saved geometry and calculation provenance,
  privacy-gated live Canonn station lookup that only fills missing local data,
  commander navigation,
  vehicle markers and dismissal guidance, automatic/manual/large-map zoom,
  conflict-zone and terminal state, optional material pickup tracking, and
  legacy-compatible material survey persistence. Template authoring is ported
  with live offset and shield-toggle capture, polygon/circle and POI editing,
  in-overlay draft preview, local undo/discard, and an explicit staged,
  parse-verified, checksum-verified atomic catalog export that backs up an
  existing file and refuses concurrent destination changes. Active quest target
  circles and widened waypoint routes are now projected into the aligned map;
  malformed imported coordinates fail closed without mutating quest data. Local
  post-processing is implemented with
  historical statistics, Codex merging, biology analysis, and transactional
  system reconstruction. The original historical Green Gas Giant matching and
  publication is restored without coupling network writes to analysis: only
  safely selected commander journals are inspected, exact raw `Scan` payloads
  and journal `StarPos` are retained, missing-position candidates are refused,
  and publication requires both the migrated privacy opt-in and a separate
  per-analysis confirmation. Successful candidates leave the retry set while
  individual failures remain isolated and retryable. Threat metadata is
  persisted and exposed through the `.threat` command. Visual/theme verification
  remains deferred to the final whole-application pass.
- [x] Cargo, missions, massacre/foot combat, and colonization projects.
  `PlotFootCombat` and `PlotMassacre` are implemented with legacy settlement,
  altitude, vehicle/panel, active-project suppression, mission-lifecycle,
  expiry, and one-credit-per-mission-giver rules. Massacre progress round-trips
  through the legacy-compatible commander profile. The original general-cargo
  presentation is the `PlotFloatie` cargo-depot remainder message; its passive
  replacement also restores material totals, Boxel completion/next-target,
  screenshot, Green Gas Giant upload, and banner-toggle messages with the
  original six-second lifetime and notification preferences. Bootstrap history
  hydrates material state without replaying messages. The Raven system-update
  workflow now loads the live
  system without writing, enforces architect/open permissions, imports missing
  body catalogs only after explicit confirmation, reconstructs legacy journal
  signal/status/approach/docking deductions, supports local manual site edits,
  and performs a fresh three-way comparison before a separately confirmed
  publish. Same-field races block publication, while remote-only sites, unknown
  response fields, and concurrently changed deletions are preserved. Its XAML
  and automated transaction coverage pass; visual QA remains deferred to the
  final whole-application pass. The original live project synchronization is
  restored as well: opted-in non-bootstrap docking resolves tracked or
  system/market-matched untracked projects and updates changed faction metadata;
  `ColonisationContribution` publishes normalized positive commodity deltas;
  depot events update the exact remaining requirements, total need, and legacy
  depot payload; completion is marked once. Each operation is isolated so a
  Raven failure cannot roll back local journal state, and the project list now
  supports the original set/clear-primary operation explicitly. Fleet Carrier
  onboarding is restored as an explicit commander-key-gated action for the
  currently docked carrier. It recovers the legacy display name from bounded
  journal chatter/FSS history, sends `cargo: null` so registration cannot erase
  existing Raven inventory, links the returned carrier locally, and reconciles
  only a matching Market snapshot written after docking. Registration and the
  optional cargo update report partial failure separately, and neither runs
  during bootstrap. New Raven credentials are now resolved through the
  original commander-by-key endpoint before they are stored. Rejected keys or
  keys owned by a different active commander leave the existing profile
  byte-for-byte unchanged, while service failures remain retryable instead of
  being mislabeled as invalid credentials. The linked-carrier live delta path
  is restored too: opted-in, non-bootstrap `MarketBuy`, `MarketSell`, and
  main-ship `CargoTransfer` events produce normalized, bounded Raven PATCH
  updates only for the currently docked known Fleet Carrier. SRV transfers are
  excluded, squadron-bank deposits retain the legacy exception, malformed
  events fail before any request, individual network failures remain isolated,
  and invalid server cargo cannot replace the local carrier inventory.
- [x] Quest communications and controller navigation. Legacy per-commander
  development-quest state and definitions now load read-only with all known
  objective, chapter, message, variable, route, tag, and journal-event fields;
  malformed data is isolated, reads leave the imported source byte-identical,
  and development progress updates merge unknown data atomically only after a
  byte-identical verified backup.
  Raven catalog, definition, publish, commander progress/status, activation,
  deletion, state-transition, and chapter endpoints are ported with the
  original authentication and unavailable-read behavior. The LuaCSharp runtime
  now restores chapter/quest variables, runs local or remotely fetched scripts,
  dispatches journal events and emotes, exposes the original quest/objective/
  chapter/commander libraries, applies chapter and terminal transitions, and
  supports message read/reply callbacks with cancellable execution. The live
  compatibility layer also reconstructs last-docked/FSD-jump, faction,
  commander-status, and surface-navigation context and reads the original nine
  auxiliary journal payload files without modifying them; unavailable,
  malformed, or multi-client-ambiguous Cargo data falls back to the triggering
  journal event. A lifecycle
  coordinator now hydrates active Raven definitions, composes remote and local
  runtimes with local development-quest precedence, isolates per-quest errors,
  suppresses bootstrap replay, dispatches only live events, and routes every
  mutation through the appropriate verified local or Raven save/transition
  contract. The desktop journal monitor now supplies the migrated opt-in,
  commander identity/API key, retained status, bootstrap/live boundary, and
  shutdown disposal to that coordinator; quest warnings and unread totals are
  exposed for presentation. Catalog/status reads plus activation, pause,
  resume, removal, and explicit refresh now execute through the same serialized
  coordinator, including verified local development-quest removal. A modern
  Raven-themed workspace now provides message reading/replies, resolved
  definition-backed text, active objectives and tracked locations, catalog
  activation, commander history/resume, pause, two-step removal confirmation,
  opt-in controls, and the migrated `questShow` global action. The compact
  `PlotQuestMini` replacement now shows visible objectives, unread messages,
  and tracked surface targets with distance, relative bearing, and target-radius
  completion; it follows legacy placement/opacity and the passive overlay safety
  rules. The `FormPlayDev` workflow is also ported: top-level legacy development
  folders are hashed and loaded without source mutation, all Lua chapters are
  validated before activation, matching progress is preserved, different quest
  identities replace only quest-local state after a verified backup, and the
  Avalonia Developer tab exposes watched reloads, objectives/messages/existing
  chapter-variable editing, chapter start/stop, Lua debugging, saved-state
  reload, guarded removal, and confirmation-gated Raven publishing. The portable
  definition is embedded in the atomic commander-state update so a missing or
  stale legacy sidecar cannot corrupt activation. Visual verification remains
  intentionally deferred to the final parity pass.
- [ ] Network integrations, update behavior, diagnostics, and remaining tools.
  The non-store release check now targets only cross-platform packages from this
  fork instead of offering an incompatible upstream WinForms asset. It ignores
  drafts, prereleases, and legacy releases without `release-index.json`, bounds
  both GitHub responses, requires the tag/index versions and both exact RID
  contracts to agree, and cross-checks archive names and sizes against unique
  GitHub assets before offering a release. It checks asynchronously after the
  window opens. HTTP, rate-limit, timeout, unsupported-platform, and malformed
  metadata failures are contained and explicitly leave both the installation
  and profile untouched. Imported reference catalogs are now
  validated and consumed with per-catalog embedded fallback. The legacy static
  catalog refresh is also restored for Codex, biology criteria, Guardian
  indexes/surveys/templates, human settlements, and Green Gas Giant criteria.
  Downloads are size-bounded, archives reject traversal and unexpected files,
  candidates must parse at full embedded coverage, existing references receive
  checksum-verified backups, and activation rolls back as a unit on injected
  pre/post-swap failures. Refresh runs without blocking journal startup and a
  controlled restart applies the immutable catalogs. Raven system nicknames now
  refresh in the same transaction: the service array is normalized into the
  legacy map only after every row and the non-empty result validate. Tagged
  cross-platform builds now establish the installable artifact contract:
  version-validated Windows ZIP and Linux tarball assets contain a per-file
  SHA-256 manifest, while the release index records the exact RID, archive type,
  byte length, and SHA-256 for both packages. Package downloads are streamed to
  a unique app-data partial file with exact response-length bounds and
  incremental SHA-256 validation; only a fully verified stream atomically
  replaces the cache, an already-valid cache is reused, and a failed replacement
  leaves the prior bytes untouched. ZIP and tar-gzip candidates are then staged
  with strict manifest/version/RID/file-set validation, portable path and link
  rejection, bounded expansion, per-file size/SHA-256 checks, preserved Linux
  executable mode, and a verified ready-directory swap that preserves the prior
  candidate on failure. Both full self-contained CI packages passed this staging
  path. The installation transaction now prepares and revalidates a same-volume
  sibling candidate while the current app remains untouched, fingerprints every
  current install file and mode, refuses any pre-swap drift, renames the old
  directory to a recoverable backup, and restores it byte-for-byte while
  preserving the failed candidate when replacement health confirmation or an
  injected post-swap checkpoint fails. The process handoff now has a bounded,
  atomically written app-data plan with strict request-directory and two-hour
  expiry checks, preserved startup arguments, a random 256-bit health token, and
  atomic health/outcome markers. The staged self-contained executable now runs
  the helper mode before any profile/UI initialization, waits for the exact
  parent instance, applies the transaction, starts the replacement with internal
  arguments removed from normal option parsing, and requires token-bound health
  confirmation after Avalonia initialization. A crash or 60-second timeout stops
  the replacement before rollback and relaunch of the verified old build; a
  still-running parent aborts without starting a duplicate. Diagnostics now
  exposes this pipeline only after an explicit close/verify/rollback consent,
  reports byte progress and every guarded phase, disables replacement for local
  or unpackaged folders without `release-package.json`, and leaves the releases
  link available for manual installation. The confirmed replacement immediately
  reports its retained rollback backup, while rollback/abort relaunches strip a
  separate internal result argument and strictly reload the matching atomic
  outcome so the restored UI explains what happened even if its asynchronous
  release check completes later. Final packaged process testing and the remaining
  network audit keep this phase open.
  The legacy regional-Codex Google Sheet now refreshes on the original
  weekly cadence or with a Codex-reference update. Its live 26-column CSV
  contract is parsed with bounded RFC-style quoting, numeric region IDs remain
  authoritative over display-name drift, unresolved unpublished variants are
  skipped as before, and the generated `codexNotFound.json` joins the same
  verified backup/stage/activation/rollback transaction. The versionless
  `Boxel.Names.txt` source is likewise provisioned for fresh or incompatible
  installs only after its complete legacy envelope and entries validate. Any
  replaced copy remains byte-identical in the checksum-verified backup, and a
  malformed download cannot touch the active reference tree. Read-only Canonn
  settlement lookup is restored against the live legacy endpoint with bounded
  nested-JSON parsing, exact system/market matching, first-submission duplicate
  behavior, migrated external-data privacy gating, and bootstrap suppression.
  Compatible remote geometry fills only missing local fields, and its original
  calculation provenance survives the lossless local save. The matching Canonn
  publication contract is restored behind a new default-off consent: only
  newly inferred live geometry is validated and submitted, with exact legacy
  field names; imported, downloaded, repeated, and bootstrap state is never
  republished, and failures remain isolated from journal processing.
  EDDN publication is now functional rather than merely retaining the legacy
  preference: the migrated opt-in and dev/beta/live environment feed the 16
  supported legacy journal contracts, while disabled and bootstrap processing
  only hydrate session, system, expansion, and body identity context. Payloads
  follow the current EDDN live schemas, always use HTTP/1.1, route dev/beta to
  test schema references, remove localized and commander-specific fields
  recursively, require every schema field, cross-check system identity before
  augmentation, and apply the Status.json/journal body agreement rule before a
  Codex BodyID is sent. Requests and response details are bounded, a failed or
  schema-invalid message is never retried automatically, later events remain
  independent, and all failures are isolated from journal processing.
  The legacy Raven launch actions are restored as cross-platform desktop links:
  API-key acquisition, Green Gas Giant documentation, the active system page,
  and the system visualizer now open through Avalonia's platform launcher with
  contained failure reporting. A tagged-control audit accounts for all 133
  original Settings options: 126 translate directly, while the seven remaining
  WinForms implementation switches are either superseded by native separate,
  click-through Avalonia overlays or already consumed losslessly from imported
  overlay/Journey settings.
- [ ] Localization review for every migrated surface.

Features may be explicitly deferred, but the release notes and UI must identify
the gap.

### Phase 7 — Packaging and release

- [x] Produce self-contained Windows and Linux publish directories. Branch and
  pull-request builds retain review packages for 14 days; version tags publish
  the same tested packages and checksum index as GitHub release assets.
- [ ] Decide whether Windows single-file publishing is compatible with native
  Avalonia dependencies, diagnostics, and update requirements.
- [ ] Build the Linux AppImage from the tested publish output.
- [ ] Add icons, desktop entry, MIME/URL handling if required, and licenses.
- [ ] Generate checksums and a software bill of materials. Package and archive
  SHA-256 manifests are generated and exercised; SBOM generation remains open.
- [ ] Test install, upgrade, downgrade/rollback, and uninstall on clean systems.

A Docker image is useful for reproducible Linux builds; the GUI application is
not expected to run as a headless container service.

## Current parity status

| Feature | Core | Desktop UI | Windows runtime | Linux runtime |
| --- | --- | --- | --- | --- |
| Application shell | Not applicable | Modern navigation plus Overview, Exploration, Exobiology, Travel, Search, Guardian, Colonisation, Diagnostics, Settings, and explicit pending states | Blue dark/light shell plus current Overview, Exobiology, Travel, Search, Guardian, Colonisation, and Settings visual smoke passed; Exploration page needs current visual recheck | Not tested |
| Journal folder discovery | Implemented; 3 tests | Paths and errors shown | Missing and default paths smoke-tested | Not tested |
| Journal ingestion/state | Retrying status/cargo readers plus polling journal append/partial-line/rotation and `Status.json`/`Cargo.json`/`NavRoute.json` change monitor; shared bootstrap/live reducer | Overview and Diagnostics projections update live; cargo changes feed Guardian artifact state | Earlier bootstrap state and cargo-backed inactive Guardian state inspected; current live monitor not exercised with Elite | Not tested |
| Raven shell themes | Five definitions; 11 desktop tests cover themes, persistence, and shell navigation | Five-theme gallery and runtime switching | Blue dark/light switched and inspected | Not tested |
| Settings/data migration | OS paths, legacy discovery, manifests, verified backup/staging/import/final activation with automatic rollback, pre/post-swap source and destination drift detection, immediate non-destructive legacy preference translation including all 26 overlay-scale indices, the multi-game label preference, and the Raven development-service override, retired commander/body organic-history conversion, lossless commander profile updates, lossless Guardian component-material hydration, lossless legacy local quest hydration plus verified atomic development-progress updates, the original quest opt-in, all legacy screenshot/notification preferences, first-footfall screen-inference colour/tolerance/threshold, and all 30 legacy input binding names/defaults implemented | Explicit backup-and-import workflow stops and awaits the live journal monitor before activation, translates preferences and retired organic history immediately, reports preserved malformed legacy settings/history without replacing or corrupting current Avalonia data, plus opt-in global keyboard/controller settings, validation, default restore, SDL device picker, refresh, reconnect status, global overlay-scale selector, multi-game Commander-label toggle, portable screenshot folders/banner/deletion controls, and all original in-game notification toggles; a valid imported Raven service URI consistently redirects Colonisation, Quest, and Green Gas Giant clients while malformed/non-HTTP values fail closed to production | Automated importer coverage proves recursive byte retention, malformed-file preservation, current-only merge retention, collision records, backup and activated-manifest identity, repeat-import refusal, path isolation, pre-swap abort, during-swap rollback, monitor shutdown ordering, immediate preference availability, byte-identical source/backup/retired organic files, idempotent additive system conversion, weak-ID and first-footfall repair, malformed-shape and reward-overflow refusal, and byte-identical Guardian component surveys across source, backup, and activation before typed loading; translated overlay scale, multi-game label, Raven service, screenshot, notification, Guardian, and first-footfall inference settings preserve unknown imported section fields during later saves; quest migration proves complete known-field loading, byte-identical read sources, verified pre-write backups, unknown-field retention, and malformed-file refusal; real profile restart comparison remains for the final runtime pass | Not tested |
| Exploration totals | Legacy valuation and six counters plus compatible, atomic profile persistence implemented | Live Overview/Exploration projections and two-step reset | Automated only | Not tested |
| Organic scans | Complete 1,070-entry Codex reference, global and 42-region commander discovery ledgers, live and historical journal ingestion, Canonn Challenge import, confirmed manual overrides, discovery-location resolution, three-sample state, surface separation, journal and conservative Windows/X11 screen-notification first-footfall detection, synchronized organic/system-history correction, sale, death, reset, compatible profile fields, current-system organism/geology history, sample-distance catalog, exact Canonn surface-POI ingestion/planning, legacy surface-history/bookmark persistence, Composition Scanner tracking, cross-system death marking, live three-sample mutations, and the complete embedded v4 prediction evaluator with region/star/nebula/Guardian context implemented | Live Overview/Exobiology projections, two-step unclaimed reset, passive `PlotBioSystem` with timed map selection, global/regional first inference, and shared-cache Canonn hints, compact `PlotBioStatus` including the optional temperature diagnostic, single-instance `FormPredictions` with the same first-discovery context, single-instance `FormShowCodex` browser with bounded image cache, single-instance `FormCodexBingo` workspace, passive `PlotPriorScans` guidance, and consolidated `PlotGrounded`/`PlotTrackers` heading-relative radar, tracker list, zoom, and quick-location workflow are implemented | Empty/live-profile page, dense body/system predictions, the standalone predictions workspace, compact active/summary/DSS overlay states, the Codex browser with a real cached reference image, and a three-entry Codex Bingo hierarchy checked in Blue dark/light; automated inference coverage proves bootstrap suppression, foreground/capture failure safety, legacy pixel bounds, and dual-store persistence, while active Elite sampling remains intentionally deferred to the final runtime pass; the new temperature diagnostic and auxiliary cues, prior-scan, and grounded-radar visual QA are intentionally deferred to the final UI pass, and interactive remote imports and Linux were not run | Not tested |
| Ground target tracking | Legacy coordinate parsing plus cardinal formats, validated settings, great-circle distance/bearing, relative heading, approach bands, and exact legacy mode gating implemented | Travel target editor supports typed, current, clipboard, clear, and live guidance; passive bottom-center `PlotTrackTarget` replacement adds Raven-themed bearing/descent instrumentation and global visibility control | Inactive/live-profile page visually and accessibility checked; overlay XAML and lifecycle are automated, while its visual/theme pass and active surface guidance are intentionally deferred to the final UI pass | Not tested |
| System notes | Lossless legacy per-system JSON lookup/update/creation plus legacy topmost and screenshot-folder settings implemented; saves against the active Journey system update that visit's note counter | Single-instance resizable notes window, current-system context, save/cancel, always-on-top, Canonn/Spansh/EDSM, screenshots, Travel launcher, and `Ctrl+Shift+N` implemented | Travel card and notes window visually checked in Blue dark/light without changing live notes | Not tested |
| Commander journeys | Lossless legacy journey JSON and active-pointer persistence, historic journal replay, all legacy counters/rewards/flags, current and prior-system starts, live updates, note coupling, conclude, and bounded reprocess implemented | Single-instance responsive workspace unifies begin, list, edit, viewer, and per-system details; supports history, preferences, notes, screenshots, dirty-state handling, and guarded destructive actions | Isolated QA journey exercised in Blue dark/light, including both start modes, begin, overview, visited systems, note edit/discard, refresh, and conclude/reprocess confirmations; no live commander Journey data changed | Not tested |
| Followed routes | Lossless legacy route JSON, activation/progression/completion, out-of-order protection, name resolution, and current Spansh result polling/shapes implemented | Travel card plus single-instance route workspace support imports, per-hop progress, route preferences, manual copy, live FSDJump advancement, route-priority Galaxy Map auto-copy, and the followed-route slice of the combined `PlotSphericalSearch` overlay | Isolated QA route/editor and earlier route-only overlay preview exercised in Blue dark/light; progress/discard, distance, notes, neutron/refuel, destination, and copy states checked; the corrected combined overlay intentionally awaits the final UI pass; no live commander data retained and no live Elite attachment run | Not tested |
| Next-jump information | `FSDTarget`/status target precedence, nav/followed/direct route planning, ship-range boost detection, EDSM bodies/traffic, Spansh dump aggregation, migrated EDSM-or-Spansh last-updated source selection, Guardian counts, and galactic-region lookup implemented with partial-provider failure handling | Passive top-center `PlotJumpInfo` replacement, proportional route renderer, automatic/FSD/selected-hop lifecycle, `Alt+D`, compact mode, persisted preferences, and Guardian-overlay arbitration implemented; the shared timestamp choice also restores the matching Galaxy Map line | Live Sol provider responses and a two-hop 64.9 ly preview checked in Blue dark/light; provider parsing/selection, migration, persistence, and both overlay projections are automated; the revised timestamp line awaits final visual QA and no live Elite attachment run occurred | Not tested |
| System/FSS survey | Current-system reducer tracks body details, galactic position, FSS/DSS values and completion, discovery/mapping, rings, materials, bio/geo analysis, destination context, latest detailed standalone planet, high-gravity warning state, and non-body signals with stale-system rejection | Passive top-left `PlotFSSInfo`/`PlotBodyInfo`, top-center journal-backed `PlotFSS` and `PlotFlightWarning`, and bottom-left `PlotSysStatus` replacements, `Alt+F`/`Alt+B`, legacy visibility/filter/Sol-bubble/gravity settings, bounded body feed, detailed body composition, last-scan values/signals, DSS targets, shared biological reward ranges, Guardian arbitration, and the original FSS pixel-tuning cues are implemented; detector settings migrate and Windows/X11 capture failures degrade explicitly | Synthetic six-body, last-scan, dense body, exact biological-reward prediction, FSS scan-state, pixel-threshold, diagnostic-encoding, and native-buffer coverage passed; the earlier overlays were checked in Blue dark/light at actual Windows DPI, while the detector indicator and high-gravity warning intentionally await the final UI pass; no live Elite attachment run | X11 capture is implemented and decoder-tested; runtime not tested; Wayland reports capture unavailable |
| Spherical search limit | Legacy-compatible center/radius state, strict boundary rule, lossless commander persistence, journal galactic position, final-route/status destination precedence, and current Spansh response contract implemented | Search supports center lookup and selection, 1–1000 ly radius, enable/disable, current-system distance/result, and the spherical slice of the combined top-right `PlotSphericalSearch` overlay | Live profile page and Sol lookup visually/accessibility checked; save action enabled but profile was not changed; destination overlay state/XAML are automated and intentionally await the final UI pass | Not tested |
| Boxel search | Generated-name hierarchy, ID64 decoding for generated and hand-authored names, completion/skip rules, lossless commander fields, legacy local-system and empty-boxel files, paged Spansh search, live `NavRoute.json`/journal updates, destination validation, and cancellable full-area completion audit with partial results implemented | Search supports activation, mass-code/date options, current/parent/sibling/child navigation, expected counts, manual completion, empty marking, current-system highlighting, refresh, manual/Galaxy Map clipboard copy, audit progress/cancellation, a large-audit confirmation guard, and the Boxel slice of the combined `PlotSphericalSearch` overlay | Inactive live-profile page and audit controls visually and accessibility checked at 1182 by 790; automated ID64, source-merge, live-completion, destination, audit, cancellation, and confirmation integration passed; combined overlay visual QA awaits the final pass; no search/profile/audit action was invoked | Not tested |
| Nearby biological systems | Canonn nearest-codex and POI enrichment plus Spansh missing-variant request/response contracts implemented; malformed rows are excluded and Spansh results are limited to five unique systems | Search supports current journal reference coordinates, both legacy modes, validation, result selection, copy name/coordinates, Canonn/Spansh links, and the original Spansh result page | Both modes and conditional inputs visually checked in Blue dark at 1182 by 790 without issuing a live query; automated request, enrichment, selection, clipboard, address-resolution, and link coverage passed | Not tested |
| Guardian surveys | All 759 shipped sites, 13 map templates, 729 published surveys, exact completion scoring including implicit-present legacy raw POIs, duplicate-ID/full-body matching, legacy/current commander files, compact/old POI and obelisk formats, compact component-material tower/panel data with lossless future-entry merging, visits, notes, beacon locations, live site transitions/writes, native map projection, lossless survey editing, duplicate-guarded live raw-POI measurement/add/remove, isolated master-template metadata/POI/destructible-panel/group-label authoring, checksum-verified concurrency-safe catalog export, cargo artifact aliases, 25 m obelisk/panel proximity, scan persistence, both Ram Tah missions, current-system summary state, incomplete-log/artifact guidance, custom-origin distance lookup, and optional all/needed-only catalog logs implemented | Browser, clipboard and Canonn/Spansh/EDSM actions, direct survey/share navigation, native maps with live template-draft preview, opt-in component-material colors/legend and tower/panel editor, survey/raw-point and collapsed developer template editors, live-site/scanner cards, artifact requirements, scan toggle, both complete Ram Tah workspaces, detached commander-centered and heading-up live map/current-obelisk, `PlotGuardianSystem`, and `PlotRamTah` replacements implemented with the five legacy sizes, exact automatic/manual zoom rules, global zoom input, site-type/heading/map-mode prompts, ruins/structure alignment reticles, migrated aerial altitudes and guide preferences, altitude fade, glide site/blueprint guidance, obelisk targeting, and compatible `.site`, `.heading`, `.alphaflip`, `.map`, `.aerial`, `.note`, `.tower`, `.empty`, `.os`, `.to`, `.add`, `.remove`, and `z` commands | Live-profile browser, maps/editor, Ram Tah tabs, and inactive scanner card visually checked at 1182 by 790; scanner checked in Blue dark/light without changing commander state; the component-material mode plus custom-origin/log/action, raw-point, master-template, alignment/glide, and revised live-map additions compile and are automated but intentionally await the requested final UI pass; all three detached overlays compile and are automated, while the two new auxiliary windows await that final UI pass and active in-game proximity was not run | Not tested |
| Human settlements | All 28 shipped templates, compatible-site filtering, pad-layout subtype inference, dock/manual-foot heading recovery, ship cockpit offsets, commander navigation, vehicle lifecycle, processed terminals, pickup locations, threat metadata, lossless system geometry with provenance, privacy-gated/live-only Canonn station lookup and default-off new-inference publication, legacy `footMatStats` persistence, portable template authoring/export, and quest route/location projection implemented | Passive Raven vector map with approach/docking state, map content preferences, automatic/manual/large zoom, ship/SRV/former-ship markers, 500 m and 2 km boundaries, material dots, active quest target circles and routes, station/survey arbitration, global map actions, `.threat` handling, and a live developer authoring workspace implemented | Reducer, geometry, storage, Canonn live-contract/parser/gating/merge/publication behavior, settings, view-model, quest projection, transactional export, transform, placement, XAML, and full-suite checks passed; no live external POST was issued and, per request, authoring visual/theme QA is deferred to the final pass | Not tested |
| Overlays/input | Passive overlay contract, monitor-aware placement, all legacy global scale modes, Windows native click-through/client tracking, X11 XShape click-through/EWMH tracking, Wayland overlay gating, multi-Elite window inventory/cycling, SharpHook keyboard input, and SDL3 controller input implemented | Guardian live map/current-obelisk, Guardian system summary, Ram Tah site guidance, human settlements, followed-route Galaxy Map, next-jump intelligence, FSS/body/system survey, high-gravity warning, biology survey, compact genetic-sampler, Canonn prior-scan/radar, surface-history/tracker radar, compact trackers, station information, ground-target, ground-combat, massacre-mission, transient notification, and the passive multi-game Commander label follow foreground Elite and close on unsafe states; every detached plotter window shares native-or-forced DPI-aware scaling; the persisted `SrvSurveyWindowOne` stream surface clones live overlays into one game-sized capture target; all 30 legacy chords are editable and keyboard/controller events share focus-aware routing | Placement, scale mapping/math/persistence/migration, multi-game inventory/label gating, capability, identity, stream projection/settings/migration, route/jump/system-survey/biology/prior-scan/surface-radar/human-site/station-info/ground-target/combat/Guardian auxiliary/notification state, hook, controller lifecycle, and chord tests passed; route, jump, FSS, body, system-status, biology survey, and compact sampler overlays were visually previewed in Blue dark/light, while scale rendering and all newly ported overlay visual QA are deferred to the final UI pass; no live multi-client/Elite overlay, stream capture, or physical controller session run | Native libraries present in Linux publish; runtime not tested |
| Combat missions | Journal state preserves ground-CZ session kills/bonds and active massacre mission acceptance, completion/failure/abandonment, mission-list reconciliation, expiry, and legacy one-credit-per-mission-giver bounty behavior; massacre state uses the compatible commander profile | Persisted opt-in settings plus modern Raven-themed top-left `PlotFootCombat` and top-right `PlotMassacre` replacements implement the exact legacy altitude, settlement-war, vehicle, panel, and build-project suppression gates | All state, persistence, settings, passive preparation, binding, and XAML compilation checks passed; per request, visual/theme QA is held until the final whole-application pass and no live combat session was run | Not tested |
| Colonisation projects | Shipped build costs, construction depot and music state, commander Raven profile, project creation validation, aggregate commodity planning, Cargo/Market readers, carrier inventories, corruption-safe Fleet Carrier registration plus fresh-market and live buy/sell/transfer Raven cargo reconciliation/endpoints, commander-resolving and lossless API-key persistence, system-site journal inference, concurrency-safe three-way site reconciliation, multi-client-safe Cargo suppression, and bootstrap-safe live contribution/depot/completion/docking project synchronization implemented | Opt-in project workspace and creation review, depot progress, set/clear primary project, explicit current-carrier publish/link with display-name recovery and existing-cargo preservation, active-commander key validation, market/ship/FC shopping overlay with legacy Market-after-docking, construction-site, right-panel, manual, and Squadron-bank music auto-show rules, overlay preferences, credential-gated linked-carrier sync, and the guarded Raven system-site editor with explicit body-import and publish confirmations implemented | Main project workspace and shopping overlay were previously checked in Blue dark; credential match/refusal/source-integrity, Fleet Carrier registration/preserve-cargo/reconciliation, event-delta/SRV/squadron/bootstrap/malformed-response rules, project-mutation contracts, startup suppression, untracked-site resolution, cargo clear/publish refusal, and updated lifecycle/system-editor transaction/XAML coverage pass automatically, with their visual pass intentionally deferred; no external publish was run | Not tested |
| Quest communications | Legacy state/definition loading and verified atomic updates preserve known and unknown data; the opt-in migrates; Raven catalog, lifecycle, progress, and chapter contracts are implemented; the LuaCSharp runtime restores variables, loads local/remote chapters, dispatches journal/emote/message callbacks, exposes quest/objective/chapter/commander helpers, applies lifecycle mutations, and supports cancellable execution with structured errors; live compatibility reconstructs prior-event, faction, status, and surface context and losslessly reads all nine auxiliary payload files while rejecting multi-client-ambiguous Cargo files; the lifecycle coordinator hydrates and composes remote/local runtimes with development precedence, error isolation, bootstrap suppression, safe persistence, catalog/status access, and activation/pause/resume/removal/refresh actions; immutable snapshots expose tracked locations and cloned route geometry; the desktop monitor supplies live commander/profile/status data and disposes the coordinator on shutdown | Modern Raven-themed messages, active-quest/objective/location, catalog, and history workspace implemented with read/reply, activation, pause, resume, guarded removal, enable/disable, refresh, and `questShow` navigation; passive `PlotQuestMini` replacement shows visible objectives, unread counts, and tracked-target navigation; active target circles and routes render on the human-settlement map | Automated migration, lossless-save, mapping, preference, endpoint-contract, script-runtime, context, hydration, multi-client Cargo fallback, source-integrity, lifecycle, precedence, bootstrap, commander-action, desktop live-append, navigation, indicator navigation, route projection, XAML compilation, and full-suite fixtures; workspace/indicator visual testing intentionally deferred | Not tested |
| Secondary features | Nearest systems, colonisation project forms, system notes, Commander Journeys, followed routes, commander profile discovery, per-process commander journal selection, multi-game Commander identification, multi-client-safe shared Cargo handling, and transactional visited-stars cache replacement are implemented; remaining rows are tracked in `docs/UI_PORTING_MATRIX.md` | Implemented slices are integrated into Search, Travel, Colonisation, Overview, and Diagnostics; multi-commander launch/focus/passive labeling, immediate Cargo-state clearing, and guarded EDGalaxy cache swap/restore include portable manual paths | Automated catalog/launcher/journal fixtures cover multi-commander identity, inventory state, label gating, cycling, Cargo suppression propagation, dock-log clearing, Guardian inventory clearing, Colonisation publish refusal, and Quest fallback; visited-stars fixtures cover response validation, game-running refusal, checksum-backed backup, staged replacement, corrupt-backup refusal, two-step UI confirmation, Windows path resolution, and verified reusable restore; see remaining per-feature rows and UI matrix | Not tested |
| Packaging/AppImage | Self-contained publish configured; no AppImage | Not applicable | `win-x64` publish passed | Cross-publish passed; runtime not tested |

Update this table only with evidence from the relevant exit gate.

## Latest validation evidence

Validation performed on 2026-07-25 using Windows build `10.0.26200` and .NET SDK
`10.0.103`:

- `dotnet build SrvSurvey.CrossPlatform.slnx --configuration Release`
  completed with zero warnings and zero errors.
- `dotnet test SrvSurvey.CrossPlatform.slnx --configuration Release --no-build`
  passed all 1,052 tests: 619 Core tests and 433 Desktop tests.
- `dotnet format SrvSurvey.CrossPlatform.slnx --verify-no-changes` passed.
- Automated quest migration coverage loads known legacy commander,
  development-definition, objective, chapter, message, variable, retained
  journal-event, route, tag, and access fields; it rejects path traversal,
  isolates malformed data, and proves both valid and malformed sources remain
  byte-identical.
- Nine Raven quest transport tests cover catalog, definition, publishing,
  commander progress/status, activation, deletion, state transitions, chapter
  scripts, escaping, authentication, unavailable reads, error reporting,
  future fields, and the definition-free legacy save payload.
- The legacy quest mapper preserves invariant objective progress, coordinates,
  access metadata, messages/actions, chapter and quest variables, retained
  journal entries, tags, and routes in the shared runtime transport model.
- Development-quest saves merge known progress into the existing legacy JSON,
  retain unknown root and nested fields, preserve definition files, verify a
  byte-identical SHA-256 backup before atomic replacement, reopen the staged
  JSON, and refuse malformed input without writing.
- Eight Lua runtime fixtures execute local and remotely supplied chapters,
  restore imported variables, cover the quest/objective/chapter/commander APIs,
  journal and humanoid-emote dispatch, message reads/replies, prior journal
  state, chapter and terminal transitions, structured failures, persistence
  callbacks, and cooperative cancellation of an infinite script.
- Automated human-settlement coverage validates all 28 embedded templates,
  compatibility filtering, docking transitions, unique pad inference, manual
  foot and automatic dock alignment, cockpit offsets, coordinate projection,
  lossless geometry and material-survey persistence, corrupt and concurrent
  file handling, terminal processing, pickup placement, vehicle markers,
  dismissal guidance, quest target/route projection with malformed-coordinate
  isolation, zoom/settings/input integration, passive placement, and XAML
  compilation. Per request, no human-site window was opened; its full
  visual/theme pass remains held until the final whole-application UI review.
- Automated diagnostics coverage validates timestamped application-log files,
  write-failure fallback, newest-ten retention, live trace updates, copy/clear/
  folder actions, crash-template URLs, exception and recent-log capture,
  journal actions, and unavailable-platform states. Both Raven XAML surfaces
  compile; per request, neither was opened for visual testing.
- Automated Guardian-sharing coverage validates the legacy discovery-delta
  rules, published-survey comparisons, commander-path confinement,
  content-sensitive archive names, ZIP contents, workspace preparation, and
  clipboard handoff. The Share data tab compiles and awaits the final visual
  pass.
- Automated prior-scan coverage validates the live Canonn string-valued POI
  contract, malformed and mismatched responses, normalized body matching,
  catalog/value/ownership/analyzed/sample filtering, physical duplicate
  removal, distance/bearing/approach and near/far state, system response
  caching, retry backoff, persisted settings, and radar presentation state.
  Per request, no prior-scan window was opened; its visual/theme pass is held
  until the final whole-application UI review.
- Automated ground-target coverage validates all accepted legacy vehicle and
  panel modes, taxi/station/coordinate/body-radius rejection, persisted target
  lifecycle, bearing/descent presentation state, passive preparation, and
  bottom-center monitor placement. Its XAML compiled as part of the full test
  run; per request, the overlay was not opened and visual/theme QA remains held
  until the final whole-application UI review.
- Automated grounded-radar coverage validates lossless legacy surface history,
  bookmark and completed-scan updates under shared per-file locking, sample and
  touchdown journal mutations, tracker-removal preferences, exact legacy
  status/panel/landing-gear gating, Composition Scanner filters, complete
  cross-system death marking, all eight quick tracker actions, marker
  navigation, tracker-only presentation, five legacy window sizes, bounded
  zoom, passive preparation, Guardian arbitration, and XAML compilation. Per
  request, the overlay was not opened; its visual/theme pass remains held until
  the final whole-application UI review.
- Automated high-gravity warning coverage validates the persisted opt-in,
  configurable threshold, landable-body requirement, and exact
  supercruise/glide/flight/landed/SRV/fighter modes. Automated combat coverage
  validates war/civil-war settlement and 100 m gates, foot/SRV modes, mission
  acceptance/removal/reconciliation, expiry, bounty credit grouping,
  compatible profile round-tripping, active-project suppression, legacy
  top-corner placement, passive preparation, and XAML compilation. Per request,
  none of these windows was opened; their visual/theme pass remains held until
  the final whole-application UI review.
- Automated `PlotSphericalSearch` coverage validates final-route precedence,
  resolver fallback, strict inside/outside sphere evaluation, Boxel destination
  validity, Galaxy Map lifecycle, route-priority clipboard state, shared
  passive preparation, original top-right placement, and XAML compilation. The
  prior route-only top-left window was replaced. Per request, the combined
  window was not opened and its visual/theme QA remains held until the final
  whole-application UI review.
- The direct and transitive NuGet vulnerability audit reported no known
  vulnerable packages.
- Self-contained `win-x64` and `linux-x64` publish commands completed; the
  Linux publish was repeated after adding the X11 native adapters.
- The Windows development shell remained healthy during startup smoke tests with
  a deliberately missing journal directory and with the default live journal
  folder (455 journal files present), then was stopped after each check.
- The rebuilt 1180 by 760 Windows shell was visually inspected in Blue (dark)
  and Blue (light). Overview, Diagnostics, Settings, pending-feature disclosure,
  live theme switching, and the exposed accessibility tree were checked. All
  five Raven theme previews rendered; the runtime switch was exercised for the
  two blue variants.
- The current Windows build was launched against the default live journal
  folder after the Exobiology slice. Overview and Exobiology rendered at 1180
  by 760, and UI Automation exposed the navigation, Refresh, and Clear
  unclaimed actions. No active genetic-sampler event occurred during this smoke
  test.
- The Travel surface-navigation page was rendered in the current Windows build.
  Its editor and inactive guidance layout were visually checked, and UI
  Automation exposed both coordinate fields and all four target actions with
  the expected enabled/disabled state. No active surface target was changed.
- The Travel system-notes card and its resizable 720 by 600 secondary window
  were rendered against the live Facece journal context in Blue (dark) and
  Blue (light). Current system/address, empty-note state, topmost control,
  Canonn/Spansh/EDSM actions, cancel/save layout, and light/dark native controls
  were checked. The window was cancelled without changing live system data and
  the original Blue (dark) preference was restored.
- The Travel Commander Journeys card and resizable 1160 by 780 Journey workspace
  were exercised with an isolated QA journal/profile in Blue (dark) and Blue
  (light). Current-system and prior-system start modes, historical start lookup,
  begin, overview statistics, the visited-system detail/notes view, dirty and
  discard states, refresh status, and conclude/reprocess confirmations were
  checked. The exact QA commander records were removed afterward and the
  original Blue (dark) preference was restored; no live commander Journey data
  was changed. Active in-game updates and Linux behavior remain untested.
- The Travel followed-route card and resizable 1100 by 760 route workspace were
  exercised with an isolated QA journal/profile in Blue (dark) and Blue
  (light). Per-hop progress changed the next-system and dirty states, discard
  restored the persisted route, and distance, notes, neutron, and refuel
  guidance rendered correctly. The compact Galaxy Map overlay was visually
  previewed in both modes with destination and copied-system states. The
  temporary hook and exact QA files were removed, and Blue (dark) was restored;
  live Elite window attachment and Linux behavior remain untested.
- The Search spherical-limit page was rendered at 1180 by 760. A live Spansh
  lookup for Sol returned five matches, selected the exact system, and showed
  the expected 131.09 ly distance from the journal position for Facece. UI
  Automation exposed the center, matches, radius, enable, and disable controls;
  the live commander profile was not changed.
- The rebuilt Search page was rendered at 1182 by 790 after the Boxel slice. Its
  status card, generated-system input, mass-code and date options, skip/FSS
  rules, auto-copy option, expected-count editor, current-boxel actions, and
  full-area audit description, progress state, audit/cancel controls, and
  disabled inactive states were visually checked and exposed through UI
  Automation. No Boxel or audit action was invoked against the live commander
  profile.
- Automated Boxel coverage exercises legacy profile round-tripping, local
  discovery files, grouped empty-boxel files, paged Spansh responses,
  real generated-name/ID64 pairs, hand-authored system names, `NavRoute.json`
  hashing/updates, bootstrap replay suppression, live jump/FSS completion,
  Galaxy Map auto-copy, tree navigation, source merging, full-area completion,
  partial-result cancellation, and the large-audit confirmation guard.
- Automated nearby-system coverage exercises the exact Canonn nearest/POI and
  Spansh body-search shapes, biological-note summaries, malformed-response
  handling, five-unique-system limit, current-system/commander context,
  validation, row selection, clipboard actions, Canonn/Spansh links, and address
  resolution for Canonn results. Both conditional mode layouts were visually
  checked at 1182 by 790 in Blue (dark), with no live query or browser action.
- Automated Guardian coverage loads all 759 reference sites, all 13 map
  templates, and all 729 published surveys; reproduces every shipped survey
  percentage; preserves the duplicate `GS 80` identities and case-sensitive
  `P35` anomaly; reads current/legacy commander folders plus old/compact POI and
  obelisk formats; isolates malformed files; merges visits and completion; and
  exercises browser filters, distance ordering, profile loading, and clipboard
  behavior. Component-material coverage parses and rewrites legacy tower/panel
  strings, preserves malformed future entries, edits all material slots,
  projects the three legacy colors, selects destructible panels by proximity,
  migrates the opt-in preference, and verifies byte-identical profile import and
  backup before typed loading.
- The Guardian browser was rendered at 1182 by 790 against the live profile.
  Nearest-site ordering, all 759 rows, live filtering to the unique `GR 1`
  system address, selected-site details, scrolling, and full galactic/surface
  coordinate layout were visually checked. No refresh, clipboard, survey, or
  profile-changing action was invoked.
- Automated Ram Tah coverage validates the exact 101 Ancient Ruins and 28
  Guardian Logs identifiers and categories, both legacy mission-name aliases,
  journal Accepted/Completed/Failed/Abandoned and active-mission transitions,
  string/numeric status loading, lossless legacy-field persistence, manual
  checklist changes, and independent two-step resets.
- The Guardian Ram Tah workspace was rendered at 1182 by 790 against the live
  profile. Both mission cards, all ten legacy checklist categories, all 129
  controls, progress/status presentation, scrolling, guide actions, disabled
  empty reset states, and the status footer were visually checked without
  changing the live commander profile. UI Automation data was unavailable.
- Automated Guardian live coverage now includes multi-digit settlement indexes,
  travel clearing, lossless approach writes, all 13 finite map projections,
  survey-editor validation/round-tripping, retry-safe `Cargo.json` monitoring,
  all six Guardian artifact aliases and duplicate quantities, site-heading
  proximity, the legacy 25 m current-obelisk rule, scan persistence, and
  artifact-gated Ram Tah updates. It also verifies the five original plotter
  sizes, migrated near-obelisk and SRV-turret preferences, exact ruins zoom
  thresholds, manual/automatic zoom transitions, and commander-centered,
  heading-up transforms. Live command coverage verifies atomic site heading,
  type, notes, tower, empty-point, scan, target, and raw-POI changes, corrects
  the previous 180-degree raw-point projection error, and proves historical
  `SendText` bootstrap events cannot replay mutations. It also verifies ruins
  and structure alignment modes, migrated grid/altitude preferences, altitude
  fade, glide blueprint guidance, and surface/panel/FSD/build-project lifecycle
  gates. Unknown imported screenshot settings survive subsequent altitude
  saves. The revised live plotter awaits final visual QA.
- Automated Guardian auxiliary-overlay coverage validates legacy-compatible
  defaults and shared-settings preservation, current-system filtering,
  destination marking, supercruise/panel visibility, forced FSS/body and active
  Raven Colonial project suppression, mission-to-site pairing, incomplete-log
  grouping, artifact readiness, and right-middle placement. Both new XAML
  windows compile, but were not opened because visual testing is held for the
  final whole-application pass.
- The Guardian live scanner card was rendered at 1182 by 790 in Blue (dark) and
  Blue (light). Its inactive proximity, artifact, mission, and disabled scan
  states were visually checked and exposed through UI Automation. Blue (dark)
  was restored, and no commander survey or Ram Tah state was changed.
- Automated overlay coverage now includes Windows/X11/Wayland capability
  matrices, Windows and Wine/X11 Elite window identities, negative-coordinate
  multi-monitor placement, the Guardian overlay preparation state, and
  commander map projection. The Windows and X11 adapters are compiled into
  self-contained publish outputs, but have not yet been exercised over a live
  Elite window; Wayland remains intentionally disabled.
- Automated system-survey coverage now includes system transitions and stale
  events, FSS completion exclusions, scan/DSS valuation, detailed body fields,
  biological/geological progress, non-body filtering, legacy visibility modes,
  forced-toggle semantics, DSS candidate rules, settings round-tripping, and
  bottom-left placement. Both new overlays were visually checked together in
  Blue (dark) and Blue (light); the temporary preview hook and settings were
  removed.
- Automated biology-overlay coverage now includes persisted legacy defaults,
  system/map versus body/FSS selection, near-body targeting, known, partial,
  and exact predicted reward totals, first-footfall value, active samples,
  analyzed dimming, regional-first semantics, geological details, incomplete
  prediction context, and immediate prediction disablement. A journal-to-exact-
  species integration test exercises parent-star context and criteria matching.
  Dense body, three-body whole-system, and exact-prediction presentation were
  checked in Blue dark/light; the temporary preview hook and QA settings were
  removed.
- Automated prediction-workspace coverage now includes current-body filtering,
  expansion state, persisted compact/comfortable/large row sizing, exact and
  genus-only organism rows, system and first-footfall totals, sample separation,
  incomplete scan context, external-link routing, and single-instance opening.
  A three-body workspace was exercised at 1040 by 760 logical pixels (1042 by
  790 including the Windows frame) in Blue dark/light. Large rows, current-body
  focus, expand all, and collapse all rendered and behaved correctly; the
  temporary preview hook and isolated settings were removed.
- Automated compact-biology-status coverage now includes its separate persisted
  auto-show preference, supported flight/focus modes, DSS guidance, genus and
  geology rows, analyzed and active progress, stale samples from another body,
  two-sample separation distance/remaining distance, and first-footfall reward.
  Active-sampler, no-active-sample summary, and DSS-required states rendered
  cleanly at 480 pixels wide in Blue dark/light. Its top-center lifecycle uses
  the shared passive window and yields to jump and Guardian overlays; the
  temporary preview hook and isolated settings were removed.
- Automated biology-Codex coverage now includes all 1,070 reference entries,
  biological filtering, commander discovery states, exact predictions, body and
  entry navigation, temperature criteria, rewards, sample separation, external
  actions, image validation, atomic caching, size limits, and download timeout.
  The single-instance `FormShowCodex` replacement was exercised with an isolated
  exact Aleoida prediction and a real cached Canonn reference image in Blue
  dark/light. Fit, wheel zoom, drag pan, credit, and disabled navigation states
  rendered and behaved correctly; the isolated profile/cache entry was removed
  and Blue dark restored. Live Elite updates, remote image-failure presentation,
  and Linux remain untested.
- Automated Codex-Bingo coverage validates the 1,070-entry hierarchy, all 42
  galactic regions, commander/region ledgers, safe manual overrides, live and
  bounded historical journal ingestion, Canonn Challenge response mapping,
  idempotent imports, location resolution, progress/selection actions, and
  nearest-search inputs. The single-instance `FormCodexBingo` replacement was
  exercised with an isolated three-entry Sol ledger in Blue (dark) and Blue
  (light). Tree expansion, selected-node details, state retention, progress,
  and disabled actions rendered correctly; the exact preview ledgers were
  removed and Blue (dark) restored. Interactive remote imports, live Elite
  updates, and Linux remain untested.
- Automated global-input coverage now validates all 30 legacy action names and
  default chords, keyboard formatting/routing and text-entry suppression,
  controller button/trigger/eight-way POV chords, first-release dispatch,
  disconnect clearing, focus/Elite gating, device changes, and settings
  persistence. Galaxy Map copy/paste now restores route, in-boxel, and clipboard
  precedence through bounded SharpHook text entry on Windows/X11, and the
  first-footfall action rewrites compatible organic entries, persisted rewards,
  and legacy system history in one synchronized correction. The unknown-body
  screen fallback carries forward the imported HUD colour/tolerance/threshold,
  refuses bootstrap or background-window capture, and has Windows/X11 capture
  implementations. The joined stream action now persists and composes registered live
  overlays into the legacy-named `SrvSurveyWindowOne` capture surface. The two
  `adjustVR` now opens and activates the Avalonia Settings calibration workflow,
  while `resetVR` calls the active OpenVR coordinator and fails closed with an
  explicit status when projection is disabled. Catalog and coordinator tests
  cover both legacy action names, the `Alt+V` default, runtime dispatch endpoints,
  and disabled-state safety. The real Windows SharpHook
  service reached its active state.
  SDL3 initialized and enumerated successfully with no controller connected;
  physical button/HOTAS input has not been exercised on this machine.
- Automated screenshot coverage uses generated BMP fixtures to verify portable
  decode, optional data banners, UTC filenames, cross-platform sanitization,
  collision-safe output, atomic PNG writes, decode/dimension verification, and
  source deletion only after successful verification. Guardian shots within
  50 m of the surveyed origin and 500-2000 m altitude also receive the legacy
  aerial-site copy, including optional Alpha crop/rotation. Legacy folder, banner,
  colour, aerial, and deletion preferences migrate without changing the
  imported `settings.json`, and later Avalonia saves retain unknown imported
  screenshot fields; runtime UI visual QA remains deferred.
- Automated notification coverage hydrates legacy material inventory without
  bootstrap popups; validates live material totals, cargo-depot remainder,
  Boxel completion/next-target, screenshot, Green Gas Giant upload, and banner-
  toggle messages; and verifies de-duplication, six-second expiry, all migrated
  preferences, unknown-setting retention, the 22nd adjustable overlay default,
  and XAML compilation. Visual/theme QA remains deferred.
- Framework-dependent `win-x64` and `linux-x64` publishes include the expected
  `SDL3.dll`/`libSDL3.so` and `uiohook.dll`/`libuiohook.so` native libraries.
  The Settings input cards, all 30 binding editors, and the SDL device picker
  were visually/accessibility checked in the saved Blue (light) theme.
- Automated Colonisation coverage now includes the shipped build catalog,
  construction journal state, project validation/creation, commander Fleet
  Carriers, ship cargo capacity, aggregate commodity allocation, local Market
  guidance and freshness, Raven project/cargo endpoints, credential persistence,
  exact market reconciliation, consent gating, and pending sync state. The main
  workspace and detached shopping overlay were visually checked without
  publishing commander or carrier data.
- The workflow YAML and `global.json` parsed successfully.

Not validated in this environment:

- A hosted GitHub Actions run of the new workflow.
- The Dockerfile (`docker` is not installed on the validation machine).
- Linux X11 or Wayland startup, live journals, overlays, input, or AppImage.
- A live Elite Dangerous session or parity comparison with real commander data.
- Physical gamepad, joystick, or HOTAS input and reconnect behavior.
- Minimum-width, OS high-contrast, and non-blue runtime theme rendering.

## Review and commit strategy

Each commit should be independently buildable where possible and cover one
reviewable concern: documentation/status, repository portability, core behavior
plus tests, a UI vertical slice, or build/release automation. Avoid commits that
mix generated formatting, binary assets, domain behavior, and CI changes.

## Open decisions

- Minimum supported Windows version and Linux distributions.
- Whether Wayland with reduced overlay/input capabilities is a supported mode.
- Whether VR remains a Windows-only optional component.
- Supported in-place data locations versus explicit import.
- Upgrade path from the legacy `.NET 9` application to the supported .NET target.
- Release signing and ownership of Windows Store/AppImage publication.
