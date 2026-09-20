# SrvSurvey-XP 2.1.3.0-rc.50

RC50 completes the overlay editor with independent per-panel typography, icon,
scale, and shape controls. It also improves overlay layouts, the Surface Mining
tracker, Colonization workspace, and Boxel Statistics.

## Per-panel overlay typography

- Right-click an overlay in the position editor and select the **Aa** button to
  adjust Header, Title, Value, Body, Detail, Caption, and Icons for that panel.
- Every overlay string is classified by meaning. Each role scales from that
  string's original size, preserving the existing layout, colours, font family,
  and weight at the 0% baseline.
- Icons independently scale badges, PIPs, biology sample circles, direction
  markers, body symbols, Codex-image indicators, pulse indicators, and other
  panel glyphs without changing Caption text.
- Fleet Carrier Route, Route Bodies, FSS, biology, surface navigation, and the
  remaining panel templates classify their full content consistently. Larger
  text and icons receive layout space instead of drawing over nearby content.
- Each panel stores its own seven-role typography profile. Profiles saved before
  icon scaling load with Icons at the unchanged 0% baseline.
- The text-scale card has a one-click reset for all seven roles. The card and the
  overlay-category selector expand upward inside the editor from their first
  opening so Linux popup bounds cannot send them below the window.

## Overlay scale and panel size

- Global overlay scale is a slider from -100% through +200% in 5% steps. Zero
  uses the operating system display scale as its baseline.
- Per-panel scale overrides use the same signed slider. Existing absolute scale
  values are backed up and converted once to preserve their physical size; the
  migration marker prevents later startups from converting them again.
- Overlay previews have a lower-right resize grip. Custom width and height
  replace the presentation's default caps while flexible columns, wrapped text,
  and scroll regions use the available space. Fixed diagrams and semantic
  groups retain their familiar internal arrangement.
- The four-arrow button beside **Aa** restores the panel's measured default
  shape. FSS Information, Prior Scans, and Route Bodies give added panel height
  to their scrolling lists and restore their compact list caps on reset.
- FSS Information constrains and wraps its summary and filter explanation,
  reducing excess width between body names and right-aligned values.
- Prior Scans uses matching geometry for ACTIVE and ANALYZED pills.
- Position and source-display reference files now save as one transaction. If
  either write fails, both original layout files are restored together.

Existing users keep their familiar overlay appearance because new typography
and icon roles default to 0%, and saved panel shapes remain unchanged until the
user resizes them.

## Surface Mining tracker

- Surface Mining lists the nearest four saved deposits from the active Mining
  Overview map, ordered by live distance from the player or Rhino.
- Adding, editing, or removing a map marker refreshes the compact tracker
  immediately. Existing named mining bookmarks remain as a fallback for older
  data.

## Workspace usability

- The experimental global Typography card is removed from Theme. Per-panel text
  and icon scaling is the supported control, while saved theme baseline values
  remain readable for compatibility.
- Colonization fits the application viewport without a horizontal workspace
  scrollbar. Site, project, and depot sections share available width, wrap their
  controls, and keep Clear primary and Make primary labels visible.
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

## Packaging

- Version: `2.1.3.0-rc.50`
- Tag: `xp-v2.1.3.0-rc.50`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.50-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.50-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.50-x86_64.AppImage`

Windows and Linux packages remain self-contained. Numeric Windows FileVersion
remains `2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.
