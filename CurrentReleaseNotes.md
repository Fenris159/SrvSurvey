# SrvSurvey-XP 2.1.3.0-rc.58.12

RC58.12 combines the AppImage restart fixes from RC58.11 with corrections to
release discovery for standard AppImage update tools. It includes RC58.10 and
earlier release candidate changes.

## Updater changes in RC58.11 and RC58.12

### AppImage updates

- Shortened the local instance-control socket name so an AppImage launched by
  the updater can register and communicate with other running instances even
  when its installation directory is used for temporary extraction.
- The socket now stays within Linux's path length limit for normal AppImage
  installation paths. This prevents a shutdown error after an update and keeps
  cooperative instance handling available on the replacement build.
- New RC AppImages can find both RC and stable releases through standard
  AppImage delta update tools, allowing those tools to move from an RC to a
  later stable release.
- Future stable releases will be marked as GitHub's Latest release so stable
  AppImage update tools can find them.
- Development remains the default in-app update channel, with saved channel
  choices preserved.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.12
  uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.12`
- Tag: `xp2-v2.1.3.0-rc.58.12`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.12-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.12-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.12-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.12-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
