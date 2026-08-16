# SrvSurvey-XP 2.1.3.0-rc.27

This release candidate improves the in-app update experience and makes
reference-data status clearer. The changes below are the delta from
`2.1.3.0-rc.26`.

## What's changed since 2.1.3.0-rc.26

- Protects automatic updates when more than one SrvSurvey instance is running.
  After confirmation, the app closes sibling instances before downloading the
  update; cancellation leaves every instance running. Windows and Linux both
  use a bounded graceful-close attempt before forcing a remaining process to
  exit, and installation stops safely if an instance cannot be closed.
- Adds a **Release notes** button beside the install action. It opens the
  selected GitHub release's title, introduction, and **What's changed** section
  inside SrvSurvey while omitting packaging and testing notices.
- Makes the published reference-data message describe whether the local
  catalog is already current and clarifies that no backup is expected until a
  refresh actually replaces data.
- Corrects **Review update** navigation so the Application updates panel is
  aligned to the top of the Diagnostics view instead of landing partway down
  the page.

## Packaging

- Version: `2.1.3.0-rc.27`
- Tag: `xp-v2.1.3.0-rc.27`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.27-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.27-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.27-x86_64.AppImage`

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
