# SrvSurvey-XP 2.1.3.0-rc.48.4

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

RC48.4 prevents Linux renderer lockups during automatic restart and further
reduces repeated X11 and Avalonia log noise. It retains the full RC48.3 feature
set summarized below.

## New in RC48.4

- Routes automatic restarts through a helper that waits for the retiring process
  to exit before launching its replacement. AppImage restarts use the stable
  original image path instead of its temporary mount path.
- Reports each X11 overlay stacking policy once per process and aggregates
  repeated matching X11 and Avalonia render-loop failures while preserving the
  first failure and periodic counts for diagnosis.
- Extends `SRVSURVEY_SOFTWARE_RENDERING=1` to the Linux X11 renderer as a
  diagnostic recovery option when the default GLX renderer cannot initialize.

RC48.3 adds an explicit Wayland capture-source reset and actionable diagnostics
for Linux capture troubleshooting.

## New in RC48.3

- Adds **Settings → Application → Wayland screen capture** with a clearly scoped
  **Choose capture source again** action. It clears the saved portal source and
  restarts SrvSurvey so the desktop picker opens if X11 capture fails and the
  Wayland fallback is needed again.
- Preserves the original capture failure in the retry-countdown status instead
  of replacing it with only a timer.
- Adds low-noise diagnostics for X11-to-portal fallback, portal capabilities and
  selected-source geometry, the first PipeWire frame and crop, repeated failures,
  and recovery. Matching failures are throttled instead of flooding the log.

## Packaging

- Version: `2.1.3.0-rc.48.4`
- Tag: `xp-v2.1.3.0-rc.48.4`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.48.4-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.48.4-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.48.4-x86_64.AppImage`

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
