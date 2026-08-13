# SrvSurvey-XP 2.1.3.0-rc.21

This release candidate improves the surface-gravity flight warning. The
changes below are the delta from `2.1.3.0-rc.20`.

## What's improved since 2.1.3.0-rc.20

- Shows the gravity warning only while the commander is actually near the
  current landable body, instead of retaining it with post-DSS biology panels.
- Uses four gravity severity styles with distinct colors and concise landing
  guidance, plus a skull icon for the highest-risk tier.
- Adds editor preview states named `Noticeable`, `Challenging`, `High risk`,
  and `Expert only` for checking every warning presentation.
- Lets the live warning window compact vertically to the shared presentation,
  matching its dimensions in the overlay editor.

## Packaging

- Version: `2.1.3.0-rc.21`
- Tag: `xp-v2.1.3.0-rc.21`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.21-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.21-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.21-x86_64.AppImage`

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
