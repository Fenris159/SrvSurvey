# SrvSurvey-XP 2.1.3.0-rc.58.10

RC58.10 fixes AppImage auto-installation when another SrvSurvey instance is open.
It includes RC58.9 and earlier release candidate changes.

## What's changed since RC58.9

### AppImage updates

- The replacement AppImage now skips the normal multiple-instance prompt during
  update verification. It can finish starting and confirm the installation
  while another commander instance remains open, instead of timing out and
  rolling back to the previous version.
- If an installation does roll back for another reason, the restored application
  also skips that prompt so it can show the update result.
- Ordinary launches still use the multiple-instance check.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.10
  uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.10`
- Tag: `xp2-v2.1.3.0-rc.58.10`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.10-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.10-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.10-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.10-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
