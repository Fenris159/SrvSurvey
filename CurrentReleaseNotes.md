# SrvSurvey-XP 2.1.3.0-rc.69.9

RC69.9 improves importing profiles from the original SrvSurvey and older XP
clients, preserving current progress while recovering compatible data. Updates
also get a longer startup check that confirms the main window is responsive
without waiting for a Google Drive download.

## What's changed

- **Import original player and Codex data.** Profile selection now recognizes
  the original versioned and Microsoft Store backup folders, avoids selecting
  XP's active data directory, and prefers profiles containing saved data over
  log-only folders. Compatibility checks cover multiple commanders, Live and
  Legacy profiles, global and regional discoveries, and manual Codex entries.
- **Keep current progress when importing.** Imports add missing commanders,
  files, and Codex discoveries while retaining current player values and valid
  duplicate Codex entries, including Bingo checks. Importing an older profile
  does not reset completed entries or restore biological claims already sold
  in XP. Repeating an import preserves progress made since the previous import.
- **Recover usable data when some files fail.** Unreadable or incompatible
  source files produce specific warnings while other recovered data remains
  usable. Optional settings conversion failures no longer stop the whole
  import. Verified backups retain captured source files and the previous
  destination, and the import result identifies the recovery location.
- **Find Wine and Proton profiles on Linux.** Automatic detection checks common
  Wine, Steam/Proton, Flatpak Steam, Bottles, Heroic, and Lutris locations,
  including external Steam libraries and configured prefixes. Manual folder
  selection remains available for custom locations and can resolve a profile
  from its Wine prefix or Proton directory.
- **Keep XP overlay positions.** Original SrvSurvey desktop and VR overlay
  positions are backed up but skipped during import because their layouts do
  not match XP. Existing XP preferences and panel positions take precedence;
  importing another XP profile can still fill missing compatible settings.
- **Confirm a responsive update startup.** The replacement application has up
  to three minutes to confirm startup. Confirmation requires the main window
  to appear, render a frame, and process another UI turn. Update messages now
  distinguish an early process exit from a startup timeout.
- **Avoid waiting for cloud downloads during update verification.** Local
  profile recovery still runs before the desktop opens, while automatic Google
  Drive checks wait until the next normal restart or **Sync now**. Your
  automatic sync preference stays enabled if it was already enabled.
- **Restore the previous version after a canceled update.** Cancellation during
  installation health checks now follows the same rollback path as a failed
  startup for both AppImage and archive installations.

## Update channel and packages

RC69.9 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.69.9`
- Tag: `xp2-v2.1.3.0-rc.69.9`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.69.9-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.69.9-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.69.9-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.69.9-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Close the source SrvSurvey before importing
to obtain a consistent snapshot. Review any import warnings for skipped files;
captured originals remain available in the recovery backup.
