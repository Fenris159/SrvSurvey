# SrvSurvey-XP 2.1.3.0-rc.48.9

RC48.9 improves Avalonia colonization sync with Raven Colonial, aligning
depot remaining updates with the proven EDMC plugin API paths and fixing
delivery history drift after contributions.

## New in RC48.9

- Aligns Raven project updates with journal-aware clients: create/setup
  stays `PUT /api/project`, while remaining need and depot snapshots use
  `PATCH /api/project/{buildId}` (matching RavenColonial EDMC). PR
  [#139](https://github.com/Fenris159/SrvSurvey/pull/139).
- Publishes absolute remaining need immediately after
  `ColonisationContribution` so Raven delivery history cannot advance
  without updating what is still required.
- Ports additional EDMC colonization hardening: clamp outbound need maps
  to non-negative values, clear phantom template commodity slots
  (negative/`-1` → `0`), skip duplicate depot PATCH payloads, and
  invalidate the short-lived project location cache on undock, create,
  link, and successful remaining sync.
- Auto-sizes colonization workspace tables and dropdowns to content so
  market, commodity, and project columns stay readable without manual
  resizing.
- Continues the colonization create/link UX and depot sync repairs from
  earlier work on this branch, including site MarketID repair when docking
  at player colony markets.

## Packaging

- Version: `2.1.3.0-rc.48.9`
- Tag: `xp-v2.1.3.0-rc.48.9`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.48.9-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.48.9-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.48.9-x86_64.AppImage`

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
