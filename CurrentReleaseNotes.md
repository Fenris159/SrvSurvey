# SrvSurvey-XP 2.1.3.0-rc.48.9

RC48.9 focuses on Avalonia colonization: Raven Colonial depot sync,
create/link reliability, delivery remaining accuracy, and clearer
workspace layout. All of this ships in PR
[#139](https://github.com/Fenris159/SrvSurvey/pull/139).

## New in RC48.9

### Raven depot sync and create/link

- Fixes Raven HTTP 400 on depot create/update by including journal
  `timestamp` and `event` on the nested `colonisationConstructionDepot`
  payload (legacy forwarded the raw journal entry; Avalonia had omitted
  those required members).
- Stops progress-only depot ticks from wiping an in-progress create or
  review form; the editor still resets when system or site identity
  changes.
- Adds commander link/unlink for Raven projects and auto-links when a
  docked construction site resolves an existing project, so it survives
  the `/active` refresh instead of vanishing as “untracked.”
- Refreshes Avalonia localization for the new linked-project status
  string.

### Delivery remaining and EDMC-aligned API paths

- Publishes absolute remaining need immediately after
  `ColonisationContribution` so Raven delivery history cannot advance
  without updating what is still required; forces a follow-up depot sync
  when needed and normalizes contribution commodity keys.
- Aligns remaining updates with journal-aware clients: create/setup stays
  `PUT /api/project`, while remaining need and depot snapshots use
  `PATCH /api/project/{buildId}` (matching RavenColonial EDMC).
- Ports additional EDMC colonization hardening: clamp outbound need maps
  to non-negative values, clear phantom template commodity slots
  (negative/`-1` → `0`), skip duplicate depot PATCH payloads, and
  invalidate the short-lived project location cache on undock, create,
  link, and successful remaining sync.

### Colonization workspace UI

- Auto-sizes colonization workspace tables and dropdowns to content so
  site, project, commodity, and create-form controls no longer clip text.
- Adds fit-to-longest-item ComboBox behavior with headless coverage, and
  tightens local quality gates (CSharpier, localization verify, dynamic
  catalog counts) so those CI failures are caught before push.

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
