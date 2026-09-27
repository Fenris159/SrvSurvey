# SrvSurvey-XP 2.1.3.0-rc.58.7

RC58.7 focuses on FSS overlay sizing, editor and live overlay parity, and
reliable mouse interaction. It includes the changes from RC58.6 and earlier
release candidates.

## What's changed since RC58.6

### Frontier account linking

- Frontier authorization now includes Steam and Epic accounts alongside
  Frontier accounts when connecting a commander. This addresses a mismatch in
  the account types offered during sign-in.

### FSS overlay and editor

- FSS information now keeps body names, scan values, landable labels, signal
  details, and footer text inside the panel at its configured size. Long body
  names are shortened on screen instead of extending past the edge.
- The overlay editor and live overlay apply the same panel size to the shared
  FSS presentation, so the preview reflects the live result.
- Opening the position editor while live overlays are interactive restores the
  live windows to click-through mode before showing draggable previews. This
  removes the drag conflict and flicker.
- Live overlay panels can be dragged even when their content handles the mouse
  press. Overlays that appear while interaction mode is already enabled become
  clickable too.

### Mouse interaction and shortcuts

- Keyboard shortcuts respond on key press, with held-key repeat suppressed so
  one press changes a toggle once. On X11, modifier keys observed by the hook
  are retained when a letter event omits its Alt, Ctrl, or Shift flags. This
  improves chords such as Alt+Shift+O after idle.
- On the patched Gamescope setup described below, enabling live overlay
  interaction releases Gamescope's relative mouse capture for the whole edit
  session. Clicking Elite no longer prevents the pointer from returning to an
  overlay. Disabling interaction restores game mouse capture.

## Gamescope configuration

Automatic mouse release requires the runtime `force_relative_mouse` control in
our [Gamescope 3.16.29 patch](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/patches/gamescope-3.16.29-runtime-relative-mouse.patch)
and the updated [Elite wrapper](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/scripts/EliteGamescope.sh).
Follow the [Ubuntu Gamescope guide](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/UBUNTU_26_GAMESCOPE.md)
to install both. The wrapper manages the setting automatically; do not add
`--force-grab-cursor` to its launch options. Stock Gamescope's
`--force-grab-cursor` applies to the whole
session and does not provide this interaction behavior.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.7
  uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.7`
- Tag: `xp2-v2.1.3.0-rc.58.7`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.7-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.7-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.7-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.7-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
