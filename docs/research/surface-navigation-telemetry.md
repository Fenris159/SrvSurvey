# Surface navigation telemetry and upstream SrvSurvey

Checked: 2026-10-18. Scope: Frontier's published surface telemetry contract,
the original Windows SrvSurvey implementation, this fork's calculation and
delivery paths, and a five-minute live flight capture. Live application timings
end at early ingestion; overlay presentation latency was not measured.

## Frontier's documented contract

Frontier's Journal Manual v38, current through May 2026, describes `Status.json`
as a whole-file snapshot, updated when data changes and nominally every few
seconds. It includes latitude, longitude, altitude, heading and planetary radius,
plus flags distinguishing main ship, SRV and on-foot states. The example has
six decimal places for coordinates and an integer heading; these examples do
not establish accuracy guarantees. [Manual §§2.1, 14, printed pp6, 95–97](https://hosting.zaonce.net/community/journal/v38/Journal_Manual_v38.pdf#page=95).

The specified position-trigger thresholds are **0.02° in flight** and
**0.0005° in the SRV**, in either latitude or longitude. These trigger writes;
the manual does not say coordinates are rounded to those increments or that
other status changes cannot publish a newer position. It specifies neither
fixed Hz nor heading-trigger timing, heading precision, on-foot position
threshold, ground-velocity vector, or the exact relationship between heading
and camera direction. In particular it does not promise a heading update on
every turn in place. [Manual §14, printed pp95–97](https://hosting.zaonce.net/community/journal/v38/Journal_Manual_v38.pdf#page=97).

## What the original SrvSurvey actually calculates

Source references below are pinned to upstream
[`1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc`](https://github.com/njthomson/SrvSurvey/commit/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc),
the `main` head observed during this investigation, dated 2026-09-05.

**It uses the reported heading, rather than estimating facing from successive
positions.** The status model reads `Heading` as an integer, stores the latest
coordinates, normalizes negative headings, and dispatches a status event onto
the UI thread. Its input is a `FileSystemWatcher` for writes to `Status.json`.
This is event notification of game-produced snapshots, rather than a movement
history or a configurable coordinate-polling estimator.
[`Status.cs`, lines 28–30, 56–66, 89–95, 184–201](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/Status.cs#L184-L201).

`TrackingDelta` calculates the vector between a **current coordinate and a
target coordinate**. Its angle is the bearing to that target, not the course
the player has recently travelled. It obtains great-circle distance and
initial bearing, then resolves the distance into east/north components using
`sin(bearing)` and `cos(bearing)`. `Util.getDistance` uses the spherical cosine
formula with a clamped `acos`; `getBearingRad` uses the usual spherical
`atan2` expression. There is no displacement baseline or movement threshold in
these routines. [`TrackingDelta.cs`, lines 46–74](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/units/TrackingDelta.cs#L46-L74),
[`Util.cs`, lines 229–309](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/Util.cs#L229-L309).

| Display | How the upstream source uses heading |
| --- | --- |
| Ground target | Relative arrow is current-to-target bearing minus `status.Heading`; descent angle separately uses altitude and surface distance. |
| Surface survey | Shared map transform rotates by negative `status.Heading`; the target/scan arrow likewise subtracts reported heading. |
| Guardian map | Map rotates by negative reported heading, translates by commander offset, then rotates by the site's stored heading. |
| Human settlement map | Shared site transform uses the same reported-heading rotation and site-relative translation/rotation; symbols compensate to remain upright. |

Sources: [`GroundTarget.cs`, lines 42–54](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/widgets/GroundTarget.cs#L42-L54),
[`PlotGrounded.cs`, lines 167–172 and 395–396](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/plotters/PlotGrounded.cs#L395-L396),
[`PlotBase2.cs`, lines 603–609 and 738–757](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/plotters/PlotBase2.cs#L738-L757),
[`PlotGuardians.cs`, lines 1275–1309](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/plotters/PlotGuardians.cs#L1275-L1309),
[`PlotHumanSite.cs`, lines 650–660](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/plotters/PlotHumanSite.cs#L650-L660).

## Implications and what still needs measurement

The following are deductions, not additional claims about Frontier's runtime:

- On a body with radius 1,000 km, the documented latitude triggers correspond to
  approximately 349.1 m in flight versus 8.73 m in an SRV (`R × angleRadians`). A
  flight moving north at 300 m/s would traverse 349.1 m in 1.16 s. This illustrates
  why position steps could be much more noticeable in flight; it is not a
  measured interval or an upper/lower bound on status writes. Longitude's local
  distance additionally scales by `cos(latitude)`.
- Heading, target bearing and ground track answer different questions. A
  current-to-target arrow must rotate with facing even if the coordinate stays
  fixed. Estimating track from successive positions could help predict drift
  or strafing, but should not replace reported facing when rotating the map.
- Faster file detection and a lighter update path can reduce application-added
  delay after a snapshot exists. Neither can recover a turn or position that
  the game has not yet written. Interpolation changes presentation;
  extrapolation predicts motion and needs bounded age/error handling.
- A live timing trace should compare monotonic file-change/read times, actual
  `Heading`/coordinate value changes, and overlay presentation times during
  ship turns, straight flight and lateral motion. Repeated timestamps alone
  should not be treated as proof that snapshots are identical. A stationary
  turn isolates heading delivery from position-triggered writes.

The flight capture below measures observed snapshot cadence and early ingestion
for one session. Heading quantization, stationary-turn behavior and final
presentation latency remain unmeasured; neither the manual nor this flight
establishes a guaranteed update rate.

## This fork: calculation and delivery audit

Audited source: `d870b6dd9370172a9517023f2a10824df1d9293c`. The reported
symptom is most noticeable while flying; SRV and on-foot behavior is acceptable.

The four surfaces also consume reported heading directly in this fork:

| Surface | Current calculation |
| --- | --- |
| Ground target | Great-circle distance and initial bearing from current coordinates to the target; relative arrow is bearing minus reported heading. |
| Surface survey radar | Each stored marker gets distance and bearing from the latest position; its relative bearing subtracts reported heading. |
| Guardian site map | Commander position is projected relative to the site origin and stored site heading; orientation uses reported heading minus site heading. Map rotation remains subject to the map's rotation setting. |
| Human settlement map | Site-relative east/north offsets come from distance and bearing; commander orientation uses reported heading minus the stored settlement heading. |

Sources: [surface geometry](../../src/SrvSurvey.Core/Navigation/SurfaceNavigation.cs),
[ground target](../../src/SrvSurvey.Core/Navigation/GroundTargetState.cs),
[surface radar markers](../../src/SrvSurvey.Desktop/ViewModels/SurfaceSurveyViewModel.cs),
[Guardian proximity](../../src/SrvSurvey.Core/Guardian/GuardianSiteProximityEvaluator.cs),
[Guardian live map](../../src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs),
[human navigation](../../src/SrvSurvey.Core/Settlements/HumanSiteNavigation.cs),
[human live map](../../src/SrvSurvey.Desktop/ViewModels/HumanSiteViewModel.cs).

There is no movement-history heading estimator, minimum travel baseline, or
heading smoothing filter in these paths. The map controls invalidate rendering
when their bound heading or position changes. Hosted overlay polling concerns
window lifecycle and placement; it is not an additional requirement to wait for
movement before redrawing map content.
[Guardian control](../../src/SrvSurvey.Desktop/Controls/GuardianSiteMapControl.cs),
[human control](../../src/SrvSurvey.Desktop/Controls/HumanSiteMapControl.cs),
[surface control](../../src/SrvSurvey.Desktop/Controls/SurfaceSurveyRadarControl.cs).

The live input loop awaits the complete poll and journal projection, **then**
waits 250 ms. Its period is therefore processing time plus 250 ms, not a
guaranteed four updates per second. Even a status-only change traverses the
full 84-operation projection plan, although many operations can return quickly
without doing work. Ground-target status runs earlier than Guardian and human
site status; surface tracking runs later still. Awaited file, provider, or
workspace work can consequently delay pose delivery and the next status read.
This is an architectural opportunity, not a measurement of a particular slow
provider on the user's machine.
[monitor loop and serialization](../../src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs),
[projection ordering](../../src/SrvSurvey.Desktop/ViewModels/JournalProjection.cs),
[projection adapter](../../src/SrvSurvey.Desktop/ViewModels/DesktopJournalProjectionAdapter.cs).

### Read-only experiments

- **No-movement turn:** replaying headings 0, 90, 180, 270, 359 and 0 at the
  same coordinate immediately produced relative target bearings 0, 270, 180,
  90, 1 and 0. Distance stayed constant. Rewriting only `Heading` also produced
  all four expected monitor updates with unchanged coordinates and unchanged
  game timestamp. Thus neither coordinate displacement nor a new timestamp is
  required by the monitor and target calculation within the same context to
  accept a changed heading.
- **Calculation cost:** after 10,000 warm-up calls, 200,000 actual
  `GroundTargetState.UpdateStatus` calls took 38.84 ms on this machine, about
  0.194 microseconds per solution. This is a microbenchmark of the navigation
  calculation, not a benchmark of full UI rendering or all map construction.
- **Queue delay:** the real `JournalProjection` with a recording adapter
  visited 84 operations for a status-only update. Injecting one synthetic
  300 ms await at `CommanderChange`, before pose consumers, delayed the four
  observed delivery points to approximately 300.7 ms. No actual provider was
  timed or contacted in this experiment.
- **Retained history:** 3,215 stored status snapshots included 1,866 near-surface
  main-ship snapshots excluding docked, landed, supercruise and on-foot modes.
  Among 1,843 positive source-timestamp intervals between adjacent eligible
  samples on the same body, limited to gaps up to 10 seconds, the median was
  1 second. There were 1,035 one-second, 344 two-second and 196 three-second
  intervals. These are deduplicated received snapshots with game timestamps,
  not file-write times, a live flight capture, or presentation-latency data.
  The original snapshots were only read; no raw commander data was copied
  into this repository.
- **Existing checks:** 50 focused Core navigation/planner/tracker tests and 92
  Desktop ground-target, surface-survey and settlement-map tests passed.

### Live flight capture

Captured 2026-10-18 from 20:23:48 to 20:28:48 UTC (15:23:48–15:28:48 CDT),
reading the game's status file at 10 ms intervals and observing new application
diagnostic-history entries. The collector wrote only separate diagnostic files;
it did not modify game files, send game inputs, or change production code. It
stopped after 300 seconds. Only selected navigation fields were retained locally;
raw commander data is not included in this repository.

| Observation | Result |
| --- | --- |
| Distinct observed game snapshots | 250; all 250 matched application history |
| Interval between observed game snapshots | Median 1.007 s; 95th percentile 2.013 s; maximum 10.074 s |
| Source observation to application-history observation | Median 124 ms; 95th percentile 228 ms; maximum 248 ms |
| Source file write time to application-history write time | Median 122 ms; 95th percentile 228 ms; maximum 241 ms |
| Distance between successive captured positions | Median 264 m; maximum 421 m |
| Ground speed derived from successive coordinates and elapsed time | Median 260 m/s; maximum 391 m/s |

There were 251 raw source observations, including one consecutive repeated pose
caused by observing a whole-file rewrite. Deduplicating that repetition gives
the 250 snapshots above; it is not a missed application update. The collector
retried empty or partial JSON encountered during writes. Its sampling interval
limits timing precision and cannot prove that no shorter-lived source state
ever existed.

The diagnostic-history write occurs near the start of application projection.
These timings therefore measure early ingestion, **not final overlay painting**
or the completion of later map updates. They show application intake consistent
with the 250 ms polling interval, with substantially larger gaps between game
snapshots. They do not demonstrate a slow provider or a rendering bottleneck.

An offline constant-velocity forecast used the last two positions and monotonic
elapsed time, comparing its result with the next authoritative position. Across
238 eligible flight intervals, holding the last position gave a median endpoint
error of 270 m. Predicting at most 0.5 s reduced that to 138 m; at most 1.0 s
reduced it to 22 m. However, the 1.0 s forecast still had a worst-case error of
411 m. These are next-sample comparisons, not measurements of intermediate true
positions or a tested visual implementation. They support cautious prediction
during steady flight, with strict age limits and correction when new data arrives.

Ground track also differed materially from reported heading: among moving
samples, the median angular difference was 8.2°, the 95th percentile 65.6°, and
the maximum 173°. A position-derived course must not replace reported facing.

### Live window-pixel timing

A separate 90-second capture at 20:42:05 UTC read the active ground-target
window's pointer pixels through X11 while observing status-file changes. The
pointer angle was measured from its outer line and endpoint, excluding the
fixed center glyph and grid. Thirteen changes of more than 1° matched the
coordinate-derived relative bearing within 0.75°. The initial state was excluded.

| Boundary | Median | 95th percentile | Maximum |
| --- | --- | --- | --- |
| Observed source change to matching window pixels | 242 ms | 341 ms | 350 ms |
| Source file write timestamp to matching window pixels | 276 ms | 379 ms | 379 ms |

This reaches the actual rendered window surface, rather than stopping at early
ingestion. It does not measure compositor presentation or monitor scanout. Each
read includes an X server round trip; the nominal 10 ms sleep is not a guarantee
of 10 ms measurement precision. Observer overhead and source-detection delay
contribute to the difference between the two rows. These samples do not establish
an isolated render-duration measurement or justify subtracting a median from the
earlier flight capture to infer a UI-only delay. A subsequent 60-second interval
had only one pointer image and could not provide a useful ingestion-to-pixel
comparison.

### Improvement order

1. Extend timing from the measured early ingestion point through pose dispatch
   and actual presentation before claiming a rendering bottleneck. Keep the
   measured game snapshot gaps separate from application-added delay.
2. Give transient position/heading updates a lightweight delivery path, using
   file-change notification with polling fallback and latest-value coalescing.
   Keep the ordered journal projection and commander/body attribution intact.
   Shortening the existing delay alone would also run the full workspace graph
   more often and cannot force Elite to write fresh telemetry.
3. Consider bounded **visual position extrapolation** during flight using
   recent coordinate displacement and elapsed time. Apply newly reported
   heading immediately and keep facing distinct from ground track. Avoid
   delayed interpolation buffers or long smoothing windows when low latency is
   the objective. Prediction cannot discover an unreported turn or acceleration.
4. Limit prediction age and correct against each authoritative sample. Reset
   on docking, landing, body/context changes or discontinuities. Saved survey
   points, proximity decisions and recorded coordinates must continue using
   authoritative data rather than predicted display positions.

The baseline evidence points to source telemetry cadence and update delivery
before bearing mathematics. The initial audit and captures changed no production
code.

## Implementation following the audit

- Journal/companion file notifications now wake the existing ordered monitor,
  coalescing repeated notifications. The 250 ms fallback remains for missed
  events, partial writes and unavailable watches. This removes the intentional
  polling sleep when a notification arrives; it does not bypass ordered journal
  projection or claim that slow workspace work cannot delay later polls.
- A shared presentation-only model predicts displacement from recent coordinates
  for at most one second. It limits sample age and speed, disables prediction
  after abrupt velocity changes, and resets for incompatible body, travel mode,
  invalid position or old samples. Once the horizon expires, the estimated
  endpoint holds until fresh authoritative data arrives; it does not drift
  indefinitely.
- Reported heading remains the source of facing. Corrections take the shortest
  angular path over 80 ms; changes larger than 90° snap to the new report. Facing
  is not inferred from travel direction and unreported turns are not predicted.
- The ground-target pointer, surface radar, Guardian map and human settlement map
  request animation frames only while their drawings are visible and moving.
  Their view-model coordinates, distance labels, proximity decisions and saved
  survey data remain authoritative. SRV and on-foot motion retain direct updates.

Validation results and any further live comparison must be recorded separately
from the baseline measurements above.

### Implementation replay and focused checks

The compiled implementation replayed the captured flight at 60 simulated frames
per second, producing 17,985 frames. The median per-frame position step was
4.21 m (95th percentile 6.32 m), versus 264 m between raw reports. The largest
frame step remained 166 m after a reset/correction; this is not a claim that all
discontinuities disappear. This replay measures the model's drawing inputs,
not native frame rate, final painting latency, or between-sample position truth.

Twenty-two focused Core tests and 64 Desktop tests passed. The Desktop checks
include rendering all four controls at multiple points between authoritative
updates and checking that distinct intermediate frames appear without mutating
the input status. Full quality-gate and package results are recorded in the
local diagnostic/build artifacts; these focused checks alone do not establish
the complete coverage gate.

The complete `tools/Test-ChangedCodeQuality.ps1` gate subsequently passed on
rerun, including formatting, localization freshness and tests, changed-line
Sonar checks, all Core/Desktop tests, and the 66-case journal sender matrix.
Changed production line/branch coverage was **95.7% (1,131/1,182 points)**,
above the 80% minimum. The first full run had one unrelated Linux speech
timeout assertion; that test passed both in isolation and in the complete rerun
without a speech-code change.

### Installed Linux test build

The normal Linux AppImage was replaced and restarted with
`2.1.3.0-rc.68+surface-motion.local.20261018`. The test package retains the
previously installed backup/project-filter/market-theme changes from
`b2b8eb77`, as well as the four bundled application integration settings.
Published localization resources were checked against the regenerated catalogs.
Package dependency closure, ordinary XWayland startup and Gamescope combined-host
startup checks passed. The running mounted assembly matches the validated
published assembly, and the installed AppImage checksum matches the test package.

At the post-install check, the game was in supercruise with no surface
coordinates. The ground target setting was also inactive; its settings file was
last modified at 21:03:03 UTC, before the installation/restart. Consequently no
post-change live pointer timing is claimed. The earlier live timing, simulation
and rendered-control tests remain distinct measurements. A further live comparison
requires surface flight with an active target.
