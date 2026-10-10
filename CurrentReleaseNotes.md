# SrvSurvey-XP 2.1.3.0-rc.60

RC.60 adds live Raven build previews, a combined cargo report, and spreadsheet
exports. Opted-in ship-cargo uploads now continue when builds are hidden or none
are loaded. It includes the RC.59.2 overlay-dragging patch, the RC.59.1 keyboard
shortcut patch, and the earlier RC.59 improvements below.

This preview improves window sizing, overlays, keyboard shortcuts, mining and
Boxel searches, and background uploads. It focuses on keeping your chosen
settings and work intact when searches overlap, connections fail, or the
application restarts.

## What's changed in RC.60 and the RC.59 previews

- **Inspect a Raven build inside SrvSurvey.** Click a project name in
  Colonization → My build projects to open its own read-only window. See cargo
  requirements, delivered progress, carrier stock and deficits, trip estimates,
  linked commanders and carriers, system effects, and delivery history.
- **Plan cargo across all your builds.** Combined Build Report, beside Open
  Raven builds, opens an aggregate report for every workspace project, including
  hidden builds. It combines requirements, delivery progress, ship and carrier
  cargo planning, and linked participants, with system effects grouped by system.
  Shared carrier stock counts once so shortages are not understated.
- **Reports stay up to date while open.** Individual previews and combined
  reports refresh automatically 30 seconds after each read cycle, with a Refresh
  button for an immediate update. Failed refreshes keep the last complete report;
  closing a window stops its requests.
- **Export a build report to a spreadsheet.** Export CSV saves the displayed
  individual or combined report in compact labelled tables with cargo quantities
  in tonnes. Empty sections are omitted, and Excel-compatible exports include
  build details or report membership, carrier totals, system effects, and history.
- **Hidden builds no longer stop ship-cargo uploads.** When automatic publishing
  is enabled, the ship name, type, capacity, and current Cargo.json counts continue
  to publish to Raven even with all builds hidden or no projects loaded. Show
  controls the shopping plan and overlay; the publishing help text now makes this
  clear. Raven access, credentials, ship readiness, and the multiple-game-window
  safeguard remain required.
- **RC.59.2 quick patch: overlay dragging.** Live overlays and position-editor
  previews use the current mouse position on X11/XWayland, fixing stale drag
  positions that could leave panels trailing behind the pointer. Fast direction
  changes no longer skip a move while the desktop is reporting an earlier
  position, and releasing the mouse ends the drag cleanly. The selected-monitor
  lock continues to keep panels within the chosen display.
- **RC.59.1 quick patch: Linux keyboard shortcuts.** SrvSurvey registers with
  the desktop shortcut service when launched directly from an AppImage, fixing
  the reported "App info not found" error. Unsupported or rejected shortcut
  setup stops retrying and filling the log. Temporary connection failures still
  recover automatically; after repairing desktop setup, restart SrvSurvey or
  turn key chords off and back on to retry.
- **Your window keeps its shape.** SrvSurvey remembers the main window's size,
  position, and maximized state between sessions. Switching applications,
  receiving an unchanged display notification, or restoring from the tray no
  longer resets a resized window. A changed monitor, resolution, or scaling
  setting uses a suitable default when the saved geometry cannot be restored.
- **Overlays stay visible when you ask them to.** “Keep overlays visible when
  Elite loses focus” also applies to the multi-game Commander panel, keeping it
  on top while you use another application.
- **Overlays recover more cleanly.** Hiding and showing a panel retries a
  temporary opening or preparation failure. Stream overlays retry preparation
  automatically, and repeated failures produce a clear status without filling
  the log with the same message.
- **Keyboard shortcuts recover after temporary failures.** Game-display input
  continues checking after a discovery, focus, or input-reading error instead
  of stopping for the rest of the session. Recovery clears stale held keys and
  automatic input-source selection while keeping your configured bindings.
- **Overlay positioning saves your final move.** Saving a layout or opening
  the position editor includes a drag that the desktop has not finished
  reporting yet. If saving fails, your live edits remain available to retry;
  canceling still restores the original positions.
- **Mining searches keep the right results.** Starting a replacement search
  prevents older work from changing the current results or progress. Ring and
  planetary searches retain their own diagnostic output, and restored searches
  keep their reference system until you edit it.
- **Powerplay searches continue past stale results.** If refreshing a cached
  batch removes all its candidates, the search continues to later pages instead
  of stopping early. Reference systems with surrounding spaces are handled
  consistently without changing a restored reference.
- **Raven delivery recovery preserves construction progress.** Pending updates
  recover the remaining materials and completion state after a restart, without
  resending delivery credit that Raven already acknowledged. Deliveries with an
  uncertain credit response still use the existing verification workflow.
- **Fleet-carrier updates stay with the right commander.** Switching profiles
  or overlapping a journal refresh with a manual refresh no longer lets stale
  work update the replacement profile or prematurely clear its pending cargo.
  Docking context and queued cargo changes remain recoverable for their owner.
- **Journal shutdown preserves recorded progress.** Exploration and system
  history from an already-read journal batch are saved before monitoring stops.
  With cargo publishing enabled and one game window open, startup publishes the
  current cargo snapshot without replaying historical cargo uploads.
- **Invalid coordinates no longer interrupt Guardian tracking.** Missing or
  out-of-range position data preserves the existing site information instead of
  triggering a false nearby-site match or aborting the journal update.
- **Boxel searches suggest the last known system again.** Named-sector systems
  are retained when resolving available Spansh, local, and route data. The
  suggested number is the highest known system, and you can still override it
  when in-game investigation finds a larger range.
- **Boxel completion settings affect an active search.** Previously visited
  systems and systems whose Spansh data predates the search start date are
  evaluated using your chosen rules. Completed systems stay visible and marked
  complete, and next-target selection skips them. Requiring a full FSS scan no
  longer lets an old scan complete a later unscanned visit. Manual empty markers,
  deferred systems, and a forced last-system value remain intact.
- **Starting a fresh Boxel search gives you a fresh set of results.** Completion,
  empty-system markers, and deferred systems from a previous search no longer
  carry into a new one. The new search uses the completion rules you choose.
  Continuing the active search or resuming one from the library keeps its recorded
  progress, with saved completion marks visible before system data finishes loading.
- **Boxel auto-copy controls stay in sync.** Changing “Auto-copy next system in
  Galaxy Map” in overlay settings also updates the Boxel workspace control, and
  vice versa. Boxel search, Route Manager, and FC Routes share one destination
  source: enabling auto-copy for one turns it off for the others, so each Galaxy
  Map entry copies a single destination.
- **New Boxel searches start with today's date.** The default refreshes when
  entering the workspace or preparing a new search, including after midnight.
  Dates you deliberately choose are preserved, and explicitly resumed progress
  retains its original start date. Configuration drafts also survive refreshes
  and rapid checkbox changes.
- **VoxStellar logs are quieter and uploads are grouped.** Events collect in
  short timed batches, reuse connections, and produce one summary of accepted,
  rejected, and failed uploads per batch. Uploads retain their existing order
  and request format; turning off uploads invalidates unsent queued events.

## Update channel and packages

RC.60 is a development preview on the schema-2 `xp2-v` update channel. Existing
update-channel choices are preserved, and RC51 remains the compatibility bridge
for older installations. Windows and Linux packages remain self-contained.

- Version: `2.1.3.0-rc.60`
- Tag: `xp2-v2.1.3.0-rc.60`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.60-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.60-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.60-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.60-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a preview for testing. Please report unexpected behavior through
the project issue tracker.
