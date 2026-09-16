# SrvSurvey-XP 2.1.3.0-rc.48.7

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

RC48.7 restores reliable main-window startup after the RC48.5 instance gate and
hardens Linux duplicate-instance detection. It retains the full RC48.6 feature
set summarized below.

## New in RC48.7

- Shows the main window after async instance-gate startup. Avalonia only shows
  `MainWindow` once at lifetime start; deferred assignment now calls `Show()`
  so ordinary launches are no longer invisible while the process keeps running.
- Marshals desktop runtime start and shutdown onto the UI thread after instance
  scanning, avoiding off-thread Avalonia window construction.
- Makes the startup multi-instance confirmation visible in the taskbar and
  centered on screen when no owner window exists yet.
- Stops treating shared `dotnet` host executables as duplicate SrvSurvey
  instances unless application identity matches, ignores weak Linux name-only
  unresolved `/proc` hits, prompts only on confirmed peers, and prefers the
  stable AppImage path for identity matching.

## New in RC48.6

- Shows journal-, Canonn-, and Spansh-backed biology reward PIPs without
  prediction hatching. Biology supplied only by the built-in prediction data
  remains hatched.
- Uses exact Canonn organism records as confirmed organism and reward evidence,
  while confirmed genus-only records retain a solid minimum-to-maximum reward
  band until the species is known.
- Keeps the base reward as the minimum and the possible five-times First Logged
  value as the maximum when bonus eligibility is not known.
- Updates the in-app PIP glossary to explain confirmed evidence, prediction-only
  hatching, and reward uncertainty.

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

## Packaging

- Version: `2.1.3.0-rc.48.7`
- Tag: `xp-v2.1.3.0-rc.48.7`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.48.7-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.48.7-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.48.7-x86_64.AppImage`

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
