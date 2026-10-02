# SrvSurvey-XP 2.1.3.0-rc.58.19

This patch improves construction completion updates to Raven Colonial and fixes
docking warnings that could remain visible after recovery or moving to another
system.

## Raven Colonial fixes

- Construction completion is now reported to Raven even when an earlier depot
  update already reduced the remaining cargo requirements to zero. Completed
  projects no longer miss their completion update because no cargo changed.
- If Raven cannot accept a completion update, SrvSurvey keeps the project
  incomplete locally and can retry on a later live completion event.
- Docking site-repair warnings clear after a successful lookup or repair, when
  switching systems or commanders, or when disabling Raven access. A delayed
  failure from an earlier context no longer brings an old warning back.
- Current site-repair warnings name the affected system. Recovery preserves
  unrelated Raven status messages.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.19
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.19`
- Tag: `xp2-v2.1.3.0-rc.58.19`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.19-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.19-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.19-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.19-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
