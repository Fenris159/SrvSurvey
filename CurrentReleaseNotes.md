# SrvSurvey-XP 2.1.3.0-rc.70

RC70 adds a Raven Colonial Nexus workspace for managing shared colonization
routes, linked builds, and carrier cargo. Profile imports preserve existing
progress and recover usable data when individual files fail, with improved
startup checks and rollback when installing an update.

## What's changed

- **Manage Raven Colonial Nexuses in Colonization.** The new Nexus tab sits
  between System Editor and Settings. Select a hub card to see its active builds,
  progress, notes, commanders, and Fleet Carriers. System cards use up to two
  columns, and carrier assignments list their systems with active build tags.
- **Create and edit shared routes.** Set a Nexus name, notes, and public/private
  visibility; add, reorder, or remove systems with existing system autocomplete;
  and manage commander and carrier membership. Card lock buttons save Nexus
  privacy changes directly. System removal and Nexus deletion require
  confirmation, and at least one system must remain unless the Nexus is deleted.
- **Plan cargo across the whole Nexus or one system.** Cargo needs use every
  carrier linked to the Nexus for each selected system. All systems counts shared
  stock once. Progress bars distinguish delivered, ready-on-carrier, and remaining
  cargo, with small nonzero ready quantities kept visible.
- **Open builds, grouped reports, and Raven pages.** Build names open their
  existing report windows; crane icons open Raven build pages. Copy system names
  or open their Raven pages directly. Play buttons open combined remaining-needs
  reports for one system or the full route, including planned facilities without
  individual build projects. CSV exports retain the Nexus and selected systems.
- **Keep Nexus edits and cargo estimates reliable.** An editor checks for newer
  changes before saving, and partial-save messages explain when to refresh and
  reopen it. Failed report refreshes retain the last usable report and filters.
  Unreported commodity details preserve known remaining tonnage, and carriers
  with unknown cargo stay linked with their readiness shown as unknown.
- **Keep dropdown menus attached while scrolling.** Menus follow their controls
  near window edges, and opening the Nexus Cargo needs selector no longer jumps
  the surrounding page. Keyboard focus still reveals the selector, long menus
  scroll internally, and the Nexus selector uses a softer selection highlight.
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

This follow-up also clears nine informational analyzer findings in the Nexus UI
tests.
