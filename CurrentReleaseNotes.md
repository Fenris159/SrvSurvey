# SrvSurvey-XP 2.1.3.0-rc.50

RC50 completes the per-panel overlay typography editor with independent icon
scaling, corrects text-role coverage across every overlay, and improves overlay
placement on Linux. It also includes the AppImage updater, profile migration,
window chrome, tooltip, pointer-input, and XWayland fixes from this release cycle.

## Per-panel overlay typography

- Right-click an overlay in the position editor and select the **Aa** button to
  adjust Header, Title, Value, Body, Detail, Caption, and Icons for that panel.
- Every overlay string is classified by meaning as Header, Title, Value, Body,
  Detail, or Caption. Each role scales from that string's original size, so the
  0% baseline preserves the existing layout, colours, font family, and weight.
- Icons independently scale badges, PIPs, direction markers, body symbols, and
  other panel glyphs without changing Caption text. Layout-aware scaling gives
  larger icons the space they need instead of drawing over nearby content.
- Fleet Carrier Route, Route Bodies, FSS, biology, surface navigation, and the
  remaining panel templates now classify their full content consistently, so
  Body, Detail, Caption, and Icons affect the content their names describe.
- Each panel stores its own seven-role typography profile and updates its
  preview and live overlay immediately. Profiles saved without an Icons value
  load with Icons at the unchanged 0% baseline.
- Biology sample circles, Codex-image indicators, pulse indicators, PIPs,
  badges, and the remaining status glyphs now follow the Icons slider. The
  per-panel text scale card includes a one-click reset that returns all seven
  roles to 0%.
- Overlay layouts now remeasure around scaled text. Compact content wraps,
  scrolls, or uses intentional ellipsis instead of clipping into nearby text.
- The overlay-category list and typography panel open upward, keeping both
  menus inside the editor near the bottom of the display.

Existing users keep the current appearance because every new typography scale
defaults to the 0% baseline.

## Overlay sizing and FSS layout

- Global overlay scale is now a slider from -100% through +200% in 5% steps.
  Zero uses the operating system's display scale as the baseline, so positive
  and negative values consistently grow or shrink overlays on every desktop.
- Each panel's optional scale override uses the same signed slider and range in
  the right-click appearance editor. On first startup, existing absolute-scale
  selections are backed up and converted once to the nearest equivalent value
  for the active display, preserving their previous physical size. Converted
  values are marked so later startups cannot migrate them again.
- Overlay previews have a lower-right resize grip. Custom panel dimensions are
  saved per overlay and replace that presentation's default width and height
  caps so flexible columns and wrapped text use the chosen shape. Fixed
  diagrams and semantic groups retain their familiar internal arrangement.
  The four-arrow button beside **Aa** restores the panel's measured default
  size.
- Prior Scans uses one fixed geometry for ACTIVE and ANALYZED pills.
- FSS Information now constrains and wraps its summary and filter explanation,
  reducing the oversized gap between body names and right-aligned values.
- The **Aa** typography controls are measured above the appearance card instead
  of using a constrained in-window popup, so they expand upward from the editor.
- The category selector now uses an upward-only popup from its first opening;
  it cannot flip below the editor when the lower edge is constrained.
- FSS Information, Prior Scans, and Route Bodies give additional custom panel
  height to their scrolling lists while keeping their headers, diagrams, and
  footers stable. Resetting the panel restores each compact list cap.
- Position and source-display reference files now save as one transaction. If
  either write fails, both original layout files are restored together.

## Surface mining map integration

- The Surface Mining overlay now lists the nearest four saved deposits from the
  active Mining Overview map, ordered by live distance from the player or Rhino.
- Adding, editing, or removing a map marker refreshes the compact tracker
  immediately, while the existing saved-bookmark list remains available as a
  fallback for older mining data.

## Workspace usability

- The global experimental Typography card has been removed from Theme. Per-panel
  text and icon scaling in the overlay editor is now the supported control; old
  theme typography values remain readable so saved themes keep their appearance.
- Colonization uses the available application width, wraps action groups, and
  proportionally sizes site, project, and depot columns without a horizontal
  workspace scrollbar. Clear primary and Make primary retain readable labels at
  narrow widths.
- Boxel Statistics uses accordion sections so Recently Recorded and the full
  Boxel browser share one viewport. Opening either section closes the other.
- Recently Recorded is capped at the latest eight matching boxels. Both sections
  use the same bordered, separated row format and keep large result sets inside
  a scrolling frame.
- Mass-code buttons are immediate multi-select filters with a theme-safe accent
  border. Selecting a letter keeps it highlighted and clicking it again clears
  that filter without hiding the label. Returning to the unscoped top-level
  view clears previous mass-code filters from recent and browser results.
- Boxel row radial actions align their center hole with the launcher on Linux
  after popup layout and display scaling are known. Window edges no longer push
  Complete, Reopen, Defer, or Start Here away from the selected row.

## Inara Community Goals

- Community Goal reads use the active commander's saved Inara API key when one
  is configured, allowing refreshes to continue when the bundled read key is
  rejected.
- Inara's explanatory API error text is now shown with the status code instead
  of reducing failures such as an invalid key to an unexplained status 400.

## AppImage updates

- A writable Linux AppImage can now install releases from SrvSurvey's update
  card instead of requiring a manual download and replacement.
- The updater verifies the indexed AppImage checksum, keeps the previous image
  for rollback, and restores it if the replacement cannot confirm a healthy
  startup, including when the operating system cannot launch the replacement.
  Activation uses one same-filesystem atomic replacement so interruption cannot
  leave the normal AppImage launch path missing.
- AppImage update mode now requires a matching `APPIMAGE` and `APPDIR`
  environment, verifies that SrvSurvey is running inside that mounted AppDir,
  and confirms the canonical launch file is a real x64 AppImage before offering
  self-update installation.
- Stable file names and symbolic-link launch paths are supported. The update
  helper uses extract-and-run mode so the update transaction does not depend on
  FUSE being installed.
- Release AppImages now embed update-channel metadata and publish a matching
  `.zsync` asset for standard AppImage tooling.
- Stable AppImages, tarballs, checksum manifests, `.zsync` metadata, and both
  in-application update feeds now publish to and consume from the same stable
  release repository.

The manual download instructions remain available when the AppImage or its
containing folder is read-only. The Linux guide also documents the writable
installation-directory and launch-path requirements for automatic AppImage and
tarball updates.

## Linux window theme

- The main window and normal tool windows now share title-bar, border, text, and
  caption-button colours from the selected application theme on X11 and XWayland.
- The themed title bar reserves its own space above the application content, so
  the navigation and page controls no longer cover the caption area.
- The themed frame keeps normal Linux window dragging, resizing, minimizing,
  maximizing, and closing behavior. Transparent overlay windows remain
  borderless.

## Linux integration fixes

- The Frontier connection warning now names the `secret-tool` executable and
  gives the correct package and install command for Debian/Ubuntu and
  Arch/Manjaro/CachyOS.
- Expected X11 session-manager, disposed IBus context, window-lifecycle races,
  and unrelated Avalonia rendering events no longer fill the application log;
  SrvSurvey's own X11 failures and incomplete unexpected IME diagnostics remain
  visible through shutdown.
- Tooltips, drop-downs, and flyouts remain available while using Avalonia's
  in-window popup layer on Linux. This avoids the transient X11 windows that
  caused hover flicker, missed button input, and unreliable focus activation.
- Clicking the main application content explicitly restores focus when the
  window is in the background.
- Standard X11 and XWayland overlays now use the notification window hint with
  a normal-window fallback, allowing positioning in the top screen area that
  GNOME reserves for its desktop toolbar when the game is not covering it.
- Choosing a Windows SrvSurvey application-data folder now prefers its populated
  `cross-platform` profile and imports the adjacent `cross-platform-ui.json`.
  Legacy version folders remain supported when no current-format profile exists.
- Profile data and UI settings are verified and backed up before replacement.
  SrvSurvey pauses its own log-file writes during the transaction so an active
  local log cannot invalidate the staged profile. Network imports report each
  phase plus live file and byte progress and warn that large shares can take time.
- Current-profile UI imports record completion separately from their verified
  backup. An interrupted migration resumes safely, reuses only a hash-matching
  backup, and cannot mistake a backup file alone for a completed import.
- Imported overlay offsets retain their source game-display dimensions and are
  scaled proportionally against the current Elite Dangerous window. Left,
  centre, right, top, middle, and bottom anchors keep their original orientation
  when moving a profile between resolutions.
- Profile-import restart helpers escape the original systemd application unit,
  so closing the importing process does not also terminate the replacement.
- Automatic Frontier commander selection now refreshes from the active journal
  even when secure-storage or linked-account discovery is unavailable.

## Packaging

- Version: `2.1.3.0-rc.50`
- Tag: `xp-v2.1.3.0-rc.50`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.50-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.50-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.50-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.50-x86_64.AppImage.zsync`

Windows and Linux packages remain self-contained. Numeric Windows FileVersion
remains `2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.
