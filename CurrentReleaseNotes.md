# SrvSurvey-XP 2.1.3.0-rc.58.9

RC58.9 improves Frontier account linking for Linux users with more than one
commander. It includes the changes from RC58.8 and earlier release candidates.

## What's changed since RC58.8

### Frontier account linking on Linux

- Each commander now has a separate saved Frontier authorization. Steam, Epic,
  and directly linked commanders can be connected without one commander's
  credential replacing another's.
- Existing authorizations move from the former shared keyring entry when saved.
  An empty old entry no longer blocks a fresh connection attempt.
- SrvSurvey reads an authorization back after saving it. If the keyring returns
  an empty or different value, the connection reports the storage failure and
  keeps the previous valid authorization available.
- A failed save of connection state no longer makes a newer, incomplete account
  update appear linked when the workspace is reopened.

Linux still needs an unlocked Secret Service wallet that can save credentials.
If the wallet cannot return a saved authorization, SrvSurvey reports the problem
at connection time so the wallet can be checked before trying again.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.9
  uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.9`
- Tag: `xp2-v2.1.3.0-rc.58.9`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.9-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.9-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.9-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.9-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
