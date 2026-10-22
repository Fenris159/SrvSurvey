# SrvSurvey-XP 2.1.3.0-rc.70

RC70 makes profile imports preserve existing progress and recover usable data
when individual files fail. It also improves startup checks and rollback when
installing an update.

## What's changed

- **Import original and older XP profiles without resetting progress.** Add
  missing commanders and global or regional Codex discoveries while retaining
  current player values, Bingo checks, and discovery metadata. Sold exploration
  and biological claims stay sold, including when an import is repeated.
- **Recover compatible data and explain what was skipped.** Locked, damaged,
  or incompatible source files produce specific warnings while readable data
  is kept. Optional settings failures no longer cancel a recovered import.
  Verified backups retain captured originals and the previous profile, and
  warnings and their recovery-report location remain visible after restart.
- **Find profiles on Windows and Linux.** Selection recognizes original
  versioned and Microsoft Store folders, common Wine/Proton and launcher
  prefixes, and external Steam libraries. Manual folder selection remains
  available. Duplicate Wine aliases and unrelated game folders no longer hide
  or duplicate detected profiles. Prefix scanning runs when import needs it,
  avoiding repeated discovery during normal path resolution.
- **Keep your current overlay layouts.** Layouts from original SrvSurvey and
  older XP profiles are backed up and skipped, including shared and commander
  panels, position references, size overrides, and VR calibrations. Existing
  panel positions stay unchanged; new profiles use XP defaults. Compatible UI
  preferences still merge while retaining current values.
- **Confirm healthy update startup and improve rollback.** Update verification
  allows up to three minutes and requires the main window to appear, render a
  frame, and process another UI turn. Local profile recovery still runs, while
  automatic Google Drive checks wait until the next normal restart or
  **Sync now**. Failure messages distinguish early exit from timeout, and canceled
  health checks restore the previous AppImage or archive installation.

## Update channel and packages

RC70 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.70`
- Tag: `xp2-v2.1.3.0-rc.70`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.70-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.70-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.70-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.70-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Close the source SrvSurvey before importing
to obtain a consistent snapshot. Review any import warnings for skipped files;
captured originals remain available in the recovery backup.
