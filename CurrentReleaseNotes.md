# SrvSurvey-XP 2.1.3.0-rc.45

RC45 adds experimental Rhino HUD rig tracking and corrects overlay sizing and
next-jump error handling. It retains the combined RC43, RC44, and RC44.5 features
summarized below.

## New in RC45

- Rig radar circles now have a 78-meter radius, matching restricted placement
  spacing. Resource circles retain their 70-meter radius.
- Adds a Mining rig range warning using the Flight Warning high-risk style.
  Aboard the Rhino, it appears beyond 4 km from the farthest saved rig and clears
  when all rigs return within range. It warns that moving beyond 4.5 km destroys
  rigs, copies the saved Flight Warning placement initially, and has independent
  Mining visibility, shortcut, and position controls.
- Hides flight warnings on foot and in ground vehicles, while retaining them
  aboard the Nomad and in normal ship/fighter flight.

- Adds opt-in, experimental detection of the Rhino's six deployment bars on
  Windows. A movable Mining calibration frame in the overlay editor provides
  independent circle centres, diameter, oval height, rotation, bar gap, and
  movement-search controls. The color picker supports modified HUD palettes;
  bright green is the default, and gray, white, and black do not count as bars.
- Automatically saves a missing rig tracker when its bar is detected, preserving
  the original location on repeated readings. Three continuous seconds of empty
  readings remove only that rig. Uncertain readings leave trackers unchanged and
  restart the removal delay. Calibration Test previews detection without changing
  trackers; normal detection requires the active game, aboard the Rhino and
  looking forward with no cockpit panel open.
- Pauses automatic tracker changes while surface coordinates or heading change,
  resuming after one second of stillness. HUD movement or reacquisition also
  requires a steady second. Rig 1 establishes the initial anchor; relative bar
  spacing and circle alignment protect slot identity. Incomplete bars, ambiguous
  layouts, night-vision backgrounds, and bright ice are handled conservatively.
  Reacquiring the empty circle grid allows the last retrieved rig to clear.
  This feature remains experimental and requires calibration for the player's HUD.
- Keeps the Surface mining panel's width and placed top-left position consistent
  between the editor and game, regardless of body-name length or empty trackers.
  Resource rows can still expand or contract it vertically. Rig detection status
  sits directly below the rig cards; cargo capacity sits above its progress bar.
- Next-jump lookup failures show a short provider notice and log the full error
  with the destination name and address. The panel retains its preview width at
  the selected scale instead of expanding to fit long error messages.
- Updates the in-app Surface mining guide and setup documentation for calibration,
  automatic tracker changes, movement safeguards, and uncertain readings.

## Surface mining and resource tracking

- Adds a theme-aware Surface mining overlay with a radar, six saved rig circles
  and direction indicators, and an SRV cargo-capacity row.
- Splits vehicle guidance into Ship and Rhino columns. On foot, the Rhino
  chevron points back to the parked vehicle; aboard, an X marks it untracked.
- Accounts for Rhino cockpit and deployment offsets when marking rigs, with
  collection, deployment-distance, and near/far guidance.
- Shows named ground-resource locations below the rigs in two columns, filling
  left to right. Each location has its own name, chevron, and live distance;
  longer lists scroll. These are manually saved surface bookmarks.
- Keeps mining guidance available while operating or walking back to a parked
  Rhino, suppressing Surface Survey and its mini tracker during that activity.
- Adds Mining under Activities with the standard overlay settings shortcut.
  The workspace remains reserved for future tools; guidance appears in the overlay.

## Tracker shortcuts and clearing

- Input settings now has **Tracker/Mining Rig (1)** through
  **Tracker/Mining Rig (6)**, followed by regular **Tracker (7)** and
  **Tracker (8)**. Defaults are **Ctrl+Alt+F1** through **Ctrl+Alt+F8**.
- Slots 1–6 toggle rigs while aboard the Rhino and surface trackers outside
  Rhino mining. Slots 7 and 8 remain regular surface trackers.
- Mining overlay settings retains six **Mining rig** entries linked to the
  first six Input bindings, so edits in either location stay synchronized.
- Preserves custom tracker chords. A customized RC43 rig chord carries over
  when the corresponding tracker still uses its default; an explicit tracker
  customization takes precedence. The old Alt+1–6 rig defaults are retired.
- Adds **Clear rigs automatically when boarding your ship**, enabled by default
  and saved between sessions. Turn it off to retain rig markers after boarding
  your own ship on foot or docking the Rhino.
- Re-entering the Rhino, taking a taxi, or boarding another Commander's ship
  preserves the rig markers. Automatic cleanup, when enabled, affects rigs only.
- Sending **`---` in game chat** clears rigs and all surface bookmarks on the
  current body, including resources, biology bookmarks, and numbered trackers.
  It works even with automatic cleanup disabled. Scan history and other bodies'
  bookmarks are preserved, and old chat commands are not reapplied on restart.

## Guides and chat-command reference

- Adds **Guides > Chat Commands**, organized by activity, with syntax, examples,
  requirements, and the data each command changes.
- Covers bookmark operators and organism abbreviations, mining cleanup,
  first-footfall and Codex commands, ground targets, Guardian alignment and
  surveys, settlement surveys and measurements, and application utilities.
- Updates **Guides > Surface mining** with shared rig chords, vehicle and cargo
  tracking, named resource bookmarks, and both cleanup options.
- See [Surface mining setup and controls](docs/SURFACE_MINING.md) for the same
  player-facing setup and workflow reference.

## Application layout and overlay presentation

- Adds a sidebar toggle at the top-right of the navigation column. Collapse to
  a narrow strip to expand the workspace, then restore navigation with the same
  button, without resizing the window or losing the current selection.
- Adds the **Monochrome Companion** overlay preset with champagne headings,
  soft gray text, and muted status colors to pair with Monochrome dark.
  Fixed flight warnings remain unchanged; pills, biology pips, commodity values,
  and segmented jump-progress cues stay distinguishable.
- Fixes the joined stream overlay remaining above other applications after
  Elite loses focus. It returns as topmost when Elite regains focus, respecting
  the existing keep-visible, editing, and live-interaction overrides.
- Restores Canonn prior-scan biological predictions, Surface Survey, and the
  mini tracker while supercruising above a planet. Normal ship flight retains
  its landing-gear gate and other overlay display conditions.
- Improves monochrome selected-row contrast and corrects Guardian card,
  expander, overlay-position, and snap-to-center regressions. Development builds
  retain F12 inspection support.

## Exploration rewards and Live Horizons

- Tracks newly estimated scan and mapping rewards by system, then removes
  matched systems after `SellExplorationData` or `MultiSellExplorationData`.
- Preserves older unattributed totals and makes replayed sales idempotent.
  Exploration reset also clears the per-system ledger.
- Keeps the ledger at the end of the Commander profile after settings saves.
  Duplicate system-name normalization is protected against numeric overflow.
- Separates Live/Legacy galaxy classification from expansion ownership so
  Live Horizons keeps the correct Commander profile, journey history, and
  exploration, system, and boxel reward estimates.
- EDDN uses the latest session's expansion flags, preserving explicit false
  values and omitting unknowns instead of carrying over stale flags.
- Recognizes demolished RavenColonial sites while excluding them from the
  planned-project picker.

## Earlier improvements included

The candidate also retains the preceding Guardian, controller, and sharing work:

- **Guardian survey workspace:** compact selected-map sidebar, collapsible
  legend, survey-point list, orientation help, and separate site editing and
  shared-map drafting. Site metadata and coordinates are editable; newly typed
  uncatalogued sites select automatically with local GR L01 / GS L01 names.
- **Guardian authoring and alignment:** live geometry/image/label previews,
  catalog export to the existing location, portable survey marker offsets,
  synchronized manual/nearest targeting, preserved unapplied edits, corrected
  commander orientation, docked-ship guidance, and visible firegroup selections.
- **Controller input:** SDL3 gamepad events on Windows and Linux, joystick/HOTAS
  polling fallback, direct chord capture, assignable D-pad diagonals, and safe
  disconnect handling. Shortcut editing accepts completed bindings on focus
  loss, restores the previous value with Escape, and supports clearing.
- **EDDN:** approved opt-in events use the production ingest gateway and
  production schema references, retaining attribution, multicrew, multi-window,
  retry, consent, and duplicate-uploader protections.
- **EDSM compatibility and account setup:** handles object-valued Multicrew
  statistics correctly; provides a dedicated opt-in card below Inara with
  per-Commander credentials, settings link, save/enable, and confirmed disable.
- **EDSM delivery:** sends supported new Live events in ordered, bounded batches,
  maintains system/station/market/ship context, and validates the current
  discarded-event policy before upload. Chat, screenshot paths, Status.json,
  unsupported companion events, startup history, Legacy, alpha/beta, diagnostic
  replay, multicrew, and ambiguous multi-window activity remain excluded.
  Delivery uses a bounded memory-only queue, rate-limit-aware retries, and a
  credential-failure pause. EDSM, EDDN, and Inara retain independent opt-ins.

## Packaging

- Version: `2.1.3.0-rc.45`
- Tag: `xp-v2.1.3.0-rc.45`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.45-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.45-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.45-x86_64.AppImage`

Windows and Linux packages are self-contained. Linux packaging tools and the
AppImage runtime use versioned, checksum-verified downloads. AppImages are updated
manually through the selected XP release. Numeric Windows FileVersion remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
