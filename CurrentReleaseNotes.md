# SrvSurvey-XP 2.1.3.0-rc.37

This release candidate brings together the desktop redesign, Guardian survey
editor, community-sharing integrations, Boxel workflow, overlay lifecycle, and
diagnostic improvements completed since `2.1.3.0-rc.36`.

## Desktop experience

- Redesigns the main window around an Active Commander card, Overview, and
  collapsible Survey, Navigation, and Activities groups, with Diagnostics,
  Settings, Theme, and Guides kept in a consistent utility area.
- Reorganizes Settings into focused categories with keyboard-friendly search
  and direct links to matching controls.
- Adds the dedicated Theme workspace for application palettes, overlay colors,
  typography, opacity, saved overlay states, and individual overlay settings.
- Adds a low-glare Monochrome dark application theme while keeping application
  and in-game overlay appearance independent.
- Refreshes cards, buttons, accordions, scrollbars, guide presentation, Overview
  labels, application branding, and detached-window titles.
- Gives Guardian Sites and Surveys fixed, column-aligned sortable headers with
  visible ascending and descending state, matching Route Manager behavior.
- Improves Boxel statistics presentation and preserves system suggestions,
  click-to-copy fields, detached tools, update navigation, and category overlay
  shortcuts through the redesign.

## Guardian surveys and maps

- Updates the bundled ruins, structures, templates, and Guardian survey archive
  to the latest verified upstream catalog, including the Gamma T9 correction.
- Uses one selectable marker inspector for reference maps and live Commander
  surveys, with segmented hover/selection rings and locked preview controls that
  become editable in the applicable survey or map-draft workflow.
- Adds precise marker editing for name, type, angle, distance, rotation, status,
  relic heading, component materials, and Commander-specific raw points.
- Adds site-type repair, paired surface latitude/longitude editing, and complete
  active-obelisk authoring for marker name, Ram Tah log, artifact requirements,
  and scanned state.
- Integrates the legacy map-authoring workflow into the Survey map: select and
  edit shared points, choose and align a local background image, add measured
  points, place obelisk group labels, export a verified catalog, or discard the
  recoverable draft.
- Mirrors the live Commander position onto the in-app Survey map when the active
  site and surface context match. The passive Commander marker never blocks
  selection of an overlapping survey point.
- Raises interactive Survey map zoom from 10x to 15x and expands the compact
  two-column legend with larger symbols and active-obelisk wedge meanings.
- Verifies the complete Guardian survey save, reload, completion, and share-ZIP
  pipeline against the established compact survey format without modifying the
  published catalogs or legacy staging data.
- Detects Elite high-resolution screenshots from the game client dimensions
  instead of the primary desktop working area.

## Search and exploration

- Moves Boxel state, persistence, saved-library linkage, audits, health, and
  auto-copy ownership into one application-scoped search session.
- Renames and clarifies the Boxel workflow around Save to Library, Open Library,
  Resume Selected, and explicit stop/resume state.
- Synchronizes linked library progress automatically, preserves drafts and
  selection during refreshes, retains partial cancelled-audit work, and prevents
  stale callbacks from overwriting newer search state.
- Coordinates Boxel, standard route, and Fleet Carrier route auto-copy without
  changing manual copy behavior.
- Reduces expected Spansh timeout noise to concise resolver diagnostics while
  retaining full details for unexpected failures.
- Defers EDSM and Spansh body lookups until Elite is running and a live visit
  confirms the current system. Unindexed data is limited to three retries after
  the initial lookup, with the visit budget and backoff retained across restarts
  and reset only after a later return visit.

## Privacy and community sharing

- Adds a dedicated EDDN consent dialog under Privacy & sharing. EDDN remains off
  by default and requires an explicit installation-wide opt-in.
- Isolates EDDN Commander and journal-series state while keeping a bounded,
  durable, application-scoped retry queue; disabling sharing removes pending
  messages and multi-client ambiguity pauses companion-file publication.
- Publishes supported live journal and companion-file data using internally
  fixed test schemas, with queue migration, fair scheduling, cancellation-safe
  ingestion, and rollback when a consent transition cannot be applied.
- Makes Inara publishing Commander-specific: saving the displayed Commander's
  personal API key opts that profile in, while confirmed removal opts it out.
- Prevents Inara events from crossing Commander or API-key boundaries and
  serializes apply, flush, profile-switch, and shutdown work for reliable final
  session reporting.
- Keeps Raven API-key validation and status updates on the UI thread so delayed
  validation no longer leaves the settings controls unresponsive.

## Overlays, updates, and application reliability

- Centralizes passive overlay hosting, visibility evaluation, requested state,
  suppression, priority rules, placement, theming, recovery, and teardown.
- Restores overlays from current settings and game state after manual, editor,
  suit, session, Galaxy Map, or higher-priority-panel suppression ends.
- Migrates Ground Target and Station Info to the shared hosted lifecycle and
  makes System Survey, Guardian, combined, and notification presentation use the
  same validated registry decisions.
- Uses hide-before-show reconciliation and an acyclic priority graph to prevent
  competing overlays from flashing, sticking, or restoring stale state.
- Hardens update installation with single-flight execution, multi-instance
  confirmation, checksum validation, staged candidates, rollback preparation,
  helper-handoff monitoring, and cleanup of cancelled, failed, timed-out, or
  stale installation plans.
- Centralizes desktop startup, ownership, rollback, restart, update handoff, and
  shutdown so failures release overlays and child resources in a deterministic,
  failure-isolated order.
- Replaces the broad main-window construction options bag with focused owned
  inputs and reverse-order rollback while preserving the existing workflows and
  bindings.

## Developer diagnostics and engineering

- Adds isolated diagnostic journal replay with a standalone Windows/Linux
  controller, managed sessions, timestamp-relative playback, stepping, speed
  controls, child-process supervision, retained logs, and an unmistakable
  diagnostic application border.
- Adds searchable Journal History and bounded raw or privacy-redacted replay
  exports with bootstrap context, checksums, immutable evidence verification,
  and deny-by-default network and external effects.
- Extends replay packages with synchronized rolling Status, Cargo, ShipLocker,
  NavRoute, and Market timelines so Commander, position, inventory, route, and
  market context are restored before playback.
- Uses compact UTC date/time export controls, defaults to the previous 24 hours,
  caps interactive ranges at 31 days, orders mixed journal/companion input
  deterministically, and locks incompatible controls during playback.
- Adds repository issue-tracker, triage-label, and domain-context guidance and
  broad regression coverage across the new lifecycle, UI, network, Guardian,
  search, update, and replay seams.

## Packaging

- Version: `2.1.3.0-rc.37`
- Tag: `xp-v2.1.3.0-rc.37`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.37-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.37-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.37-x86_64.AppImage`

The Windows and Linux packages are self-contained. AppImages must be updated
manually; the application links directly to the selected XP release.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
