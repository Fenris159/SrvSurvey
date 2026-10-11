# SrvSurvey-XP 2.1.3.0-rc.62

RC.62 adds profile backup and Google Drive sync, reorganizes Guides into
searchable subjects, and restores missing Firegroups choices with controls for
cockpit-view visibility. Colonization overlays gain clearer cargo counts,
customizable colors, compact layouts, and previews that follow your settings.
Live overlays have fewer status labels, the sidebar shows seven-day activity,
and shutdown backups finish after the window disappears.

## What's changed

- **Continue on another computer with Google Drive.** Link your Google account
  in Settings → Data & migration. Portable preferences and saved work can follow
  you between computers, including themes and colors, overlay visibility,
  shortcuts, workspace selections, saved searches, routes, navigation and mining
  bookmarks, Firegroups configurations, and surveys. Automatic sync checks for
  cloud changes on startup and backs up on orderly shutdown. Backup now and Sync
  now let you update the cloud while SrvSurvey is open.
- **Close the window while your backup finishes.** SrvSurvey disappears promptly
  when closed, then finishes saving and uploading in the background before the
  process exits. Repeated close requests do not bypass the shutdown guard.
  Failed or timed-out uploads retain a local recovery copy for a later retry.
- **Keep hardware settings with the right machine.** Monitor selection, overlay
  placement, capture calibration, platform options, and input-device choices are
  backed up separately and restored only on their originating machine and
  operating system. Credentials, journals, screenshots, and queued API reports
  are excluded from backups.
- **Recover saved work and choose conflicting changes.** Restore a cloud backup
  or export and import a local backup file without a Google account. Ten recent
  local and cloud backups are retained per machine. Restores requested while the
  app is open apply after restart, with a local recovery copy saved first. If
  computers change the same setting or collection, choose which version to keep;
  independent changes are combined. Failed uploads retain local recovery data so
  you can retry later. Close all SrvSurvey instances before applying a restore.
- **Manage your cloud backup history.** Refresh history to see the backup count
  and storage used across computers. Download a selected backup to keep a
  separate file, delete one backup, or clear all listed SrvSurvey cloud backups.
  Deletion requires confirmation and preserves local data and Google linking.
  Automatic backups on linked machines can create new cloud files afterward.
  Cloud backups use Drive's private app-data folder and count toward your Google
  storage; SrvSurvey's permission is limited to that folder.
- **Find and follow Guides more easily.** Expand a category and choose a subject
  to read focused instructions. Search stays at the top, searches complete
  subjects in the selected language, and opens matching instructions directly.
  Procedures use numbered steps, supporting notes, and relevant symbol examples.
  Updated help covers current mining searches, Rhino calibration, Wayland capture,
  keyboard input, overlays, delivery recovery, backup and sync, and other existing
  workflows. Firegroups and Fleet Carrier now have their own categories.
- **Assign the missing Firegroups actions.** Composition Scanner joins the
  built-in scanners in new and saved ship configurations. Equipped Frame Shift
  Drive Interdictors now have readable module names. Other equipment choices
  continue to follow the selected ship's recorded loadout.
- **Choose where the Firegroups overlay appears.** New Left, Main, and Right
  checkboxes in Theme → Overlay Settings control cockpit-view visibility. Main
  is enabled by default; the overlay hides when a side panel is focused unless
  you enable that view, and stays hidden in other interface screens.
- **Follow Fleet Carrier jump progress in the overlay.** Departure countdowns,
  jump phases, and cooldowns now update in real time, matching the application.
  Scheduling a jump also reveals the countdown in an already-open overlay.

- **Keep the colonization panel visible when you need it.** The Colonization
  commodities shortcut and panel checkbox now show the panel persistently until
  you hide it. The automatic docking option is renamed to Auto hide colonization
  overlay until docked. An indented Hide collapsed groups option can remove
  covered categories completely instead of leaving their heading and summary.
- **See combined cargo and accurate carrier readiness.** Combine Ship totals
  into FC counts adds ship cargo to the HAVE column instead of alternating
  between ship and carrier values. HAVE Δ uses that same combined total when
  Show delta vs sum is enabled. FC READY still requires enough cargo on the
  carriers themselves, so cargo waiting on your ship does not mark them ready.
- **Give colonization categories and counts their own colors.** In Theme →
  In-game overlay appearance → Colonization, the new Item Category picker sits above Item.
  Headings such as Metals, Technology, and Chemicals can use a different color
  from commodity names such as Steel. Default categories use gold (#FFC100),
  while the other presets use contrasting accents matched to their palettes.
  NEED, FC / HAVE, and SHIP gain separate numeric color pickers. Delta columns
  use surplus and deficit colors, including their dim variants; all seven
  presets start with consistent green/red delta colors and white NEED values.
  The Default preset uses a clearer grey commodity row fill at about 36%
  opacity (#6666665C). Carrier-loading highlights shade the whole row and share
  the row-fill opacity. Existing saved colors and row fills are preserved.
  Rapid theme saves keep separate recovery copies instead of failing when
  backups would otherwise share the same timestamp.
- **Read a more compact colonization table.** Completed commodities show the
  existing checkmark before their name, removing the separate checkmark column.
  Checkmarks keep row heights uniform, and delta symbols stay aligned with the
  other column titles. Column titles use the Primary accent color and Title font scale.
  Long project names wrap after the spaced hyphen, and the panel narrows when
  cargo columns are hidden instead of keeping unused space.
- **Preview the colonization layout you actually use.** The position editor now
  applies your current cargo columns, combined totals, delta mode, and category
  collapse/hide choices to sample data. Changing those settings refreshes an
  open preview while preserving its position.
- **Keep live overlays free of interaction status labels.** Passive and
  pass-through status text no longer occupies space inside the live panels.
  Passive panels instead show a small PSV badge beside their name in the
  position editor's panel tab.
- **See community activity over the last week.** Active Users 7d appears above
  the active commander card and refreshes once daily after a commander session
  loads. An anonymous check-in sends only a random installation ID, application
  version, and operating system; commander names and identifiers are excluded.
  Commander instances share one daily check-in on the same installation. The
  count uses installations active in the last seven days, retains its cached
  value when offline, and keeps its local identity out of Drive backups and sync.

## Update channel and packages

RC.62 is a development preview on the schema-2 `xp2-v` update channel. Existing
update-channel choices are preserved, and RC51 remains the compatibility bridge
for older installations. Windows and Linux packages remain self-contained.

- Version: `2.1.3.0-rc.62`
- Tag: `xp2-v2.1.3.0-rc.62`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.62-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.62-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.62-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.62-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a preview for testing. Please report unexpected behavior through
the project issue tracker.
