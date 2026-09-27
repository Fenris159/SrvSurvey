# SrvSurvey-XP 2.1.3.0-rc.58.11

RC58.11 makes AppImage update restarts and multi-instance handling reliable on
Linux installations with longer paths. It includes RC58.10 and earlier release
candidate changes.

## What's changed since RC58.10

### AppImage updates

- Shortened the local instance-control socket name so an AppImage launched by
  the updater can register and communicate with other running instances even
  when its installation directory is used for temporary extraction.
- The socket now stays within Linux's path length limit for normal AppImage
  installation paths. This prevents a shutdown error after an update and keeps
  cooperative instance handling available on the replacement build.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.11
  uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.11`
- Tag: `xp2-v2.1.3.0-rc.58.11`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.11-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.11-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.11-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.11-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
