# SrvSurvey-XP 2.1.3.0-rc.5

This fifth release candidate is a targeted updater repair. The changes below
are the delta from `2.1.3.0-rc.4`.

## What's new since 2.1.3.0-rc.4

- Fixes guarded updates for Windows installations under protected locations
  such as `Program Files (x86)`. When the normal process cannot create the
  same-volume rollback candidate, SrvSurvey now requests administrator approval
  and completes that preparation in the external update helper.
- Keeps the privilege transition narrow: the helper revalidates the staged
  package, binds the request to the running installed SrvSurvey process, and
  signals readiness before the application closes.
- Preserves the existing whole-directory backup, startup health confirmation,
  and automatic rollback behavior. Cancelling the Windows approval prompt
  leaves the active installation and player profile unchanged.
- Leaves user-writable Windows installs and Linux update behavior on their
  existing non-elevated path.

## Upgrading from RC4 or earlier on Windows

The elevation repair runs from RC5, so an older build installed under
`Program Files` cannot use it until RC5 is installed. For this one upgrade,
either start the existing SrvSurvey build as administrator before using the
built-in updater, or install the RC5 Windows package manually. Future built-in
updates from RC5 will request administrator approval only when the installation
location requires it.

## Packaging

- Version: `2.1.3.0-rc.5`
- Tag: `xp-v2.1.3.0-rc.5`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.5-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.5-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.5-x86_64.AppImage`

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

## For testers

Install the complete platform package rather than separating the executable
from its companion files. See the
[`Windows installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_WINDOWS.md) or
[`Linux installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_LINUX.md), and report defects or suggestions through the
[`Fenris159/SrvSurvey` issue tracker](https://github.com/Fenris159/SrvSurvey/issues).
