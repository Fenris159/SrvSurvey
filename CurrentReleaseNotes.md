# SrvSurvey-XP 2.1.3.0-rc.48.5

- Replaces the Mining workspace placeholder with session accounting, prospecting
  yields, core and raw-material tracking, cargo, mining missions, historical
  reports, screenshots, manual refinery estimates and mining settings.
- Adds shared Navigation → Bookmarks with categories, filters, mining annotations
  and import/export. Mining uses the same catalog.
- Adds local/reference/Spansh hotspot searches, commodity-market and system/trader
  searches, existing Fleet Carrier data and distance shortcuts. Optional
  receive-only EDDN observations supplement prices and Powerplay information.
- Adds ship-only Mining notifications and a Firegroups reference overlay, with
  shared editor/live presentations, overlay controls and input toggles.
  Existing Rhino overlays are unchanged by this workspace expansion.
- Adds announcement filters/presets and optional Windows speech, CSV history
  import, offline HTML reports with print/PDF output, and ZIP backups containing
  shared bookmarks and screenshot attachments. Guides documents the new tools.

RC48.5 improves Surface Mining rig placement and makes application-instance and
main-window behavior safer and clearer. It retains the full RC48.4 feature set
summarized below.

## New in RC48.5

- Replaces the circular-biased splat fit with a deterministic hybrid candidate
  search and bounded exact packing pass. Irregular and oval traces can use their
  available area, small valid traces still suggest one rig, and the solver seeks
  the greatest valid arrangement up to the six-rig limit within an eight-second
  budget.
- Prevents accidental duplicate launches on Windows and Linux. A normal launch
  now offers to close existing SrvSurvey processes before continuing; deliberate
  additional instances remain available through the Multiple commanders panel.
- Brings an accepted replacement launch to the foreground after the old process
  exits, including restoration from hidden or minimized taskbar state.
- Disables the commander selector when no alternative profile remains and keeps
  its status synchronized when the journal identifies the current commander.
- Matches the Windows 11 native caption, border and title text to the selected
  application theme, with a quieter inactive-window palette. Earlier Windows
  releases and Linux retain their platform-native window decorations.

## New in RC48.4

- Routes automatic restarts through a helper that waits for the retiring process
  to exit before launching its replacement. AppImage restarts use the stable
  original image path instead of its temporary mount path.
- Reports each X11 overlay stacking policy once per process and aggregates
  repeated matching X11 and Avalonia render-loop failures while preserving the
  first failure and periodic counts for diagnosis.
- Extends `SRVSURVEY_SOFTWARE_RENDERING=1` to the Linux X11 renderer as a
  diagnostic recovery option when the default GLX renderer cannot initialize.

## Packaging

- Version: `2.1.3.0-rc.48.5`
- Tag: `xp-v2.1.3.0-rc.48.5`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.48.5-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.48.5-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.48.5-x86_64.AppImage`

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
