# SrvSurvey-XP 2.1.3.0-rc.62

RC.62 adds profile backup and Google Drive sync, reorganizes Guides into
searchable subjects, and restores missing Firegroups choices with controls for
cockpit-view visibility.

## What's changed

- **Continue on another computer with Google Drive.** Link your Google account
  in Settings → Data & migration. Portable preferences and saved work can follow
  you between computers, including themes and colors, overlay visibility,
  shortcuts, workspace selections, saved searches, routes, navigation and mining
  bookmarks, Firegroups configurations, and surveys. Automatic sync checks for
  cloud changes on startup and backs up on orderly shutdown. Backup now and Sync
  now let you update the cloud while SrvSurvey is open.
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
