# SrvSurvey-XP 2.1.3.0-rc.31

This release candidate makes overlay headings and typography consistent,
themeable, and easier to read. The changes below are the delta from
`2.1.3.0-rc.30`.

## What's changed since 2.1.3.0-rc.30

- Gives fixed, non-Guardian overlay headings a shared **Header** colour and
  standardized size while leaving contextual and Guardian overlay headings
  unchanged. Header colour is configurable above **Primary accent**, included
  in saved appearance states, and paired with each built-in theme preset.
- Adds persisted typography controls for **Header**, **Title**, **Value**,
  **Body**, **Detail**, and **Caption** text roles. Half-point adjustments are
  reflected in open overlays through Preview or Apply without changing the
  established default layout.
- Brightens the default muted overlay text colour to `#99AFBF` and gives the
  Cerulean Gold preset the requested `#FFCC33` heading colour.
- Keeps all typography values visible beside their increment and decrement
  controls, including at scaled desktop display settings.

## Packaging

- Version: `2.1.3.0-rc.31`
- Tag: `xp-v2.1.3.0-rc.31`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.31-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.31-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.31-x86_64.AppImage`

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
