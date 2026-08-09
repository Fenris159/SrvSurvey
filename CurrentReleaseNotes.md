# SrvSurvey-XP 2.1.3.0-rc.7

This release candidate restores exobiology behavior that was lost or
misinterpreted during the Avalonia port. The changes below are the delta from
`2.1.3.0-rc.6`.

## What's new since 2.1.3.0-rc.6

- Preserves exact organism identity across journals, predictions, snapshot
  merges, legacy migration, and restart recovery, including bodies containing
  multiple species or variants from the same genus.
- Restores canonical Horizons genus handling for Brain Trees, anemones, bark
  mounds, Amphora Plants, crystalline shards, and sinuous tubers, including
  their sampling-distance behavior.
- Aligns sampling and composition tracking with legacy behavior: completed
  analysis clears partial sample state, active organisms resolve by EntryID or
  species, and analyzed-species suppression remains local to the correct body.
- Restores durable prior-scan ownership and regional Codex fallback when journal
  coordinates are unavailable, while unresolved Codex EntryIDs are never
  classified as commander first discoveries.
- Adds the distinct white potential galactic or regional first-discovery PIP,
  theme controls, and glossary guidance, while filtering fixed-life Codex events
  and invalid surface coordinates from organism results.

## Packaging

- Version: `2.1.3.0-rc.7`
- Tag: `xp-v2.1.3.0-rc.7`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.7-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.7-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.7-x86_64.AppImage`

The Windows and Linux packages are self-contained. AppImages must be updated
manually; the application links directly to the selected XP release.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
