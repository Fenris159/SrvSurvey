# SrvSurvey-XP 2.1.3.0-rc.49.3

RC49.3 makes Linux Wayland screen capture an explicit opt-in for each image-based
tracker. You can now enable one tracker at a time while testing the ScreenCast
and PipeWire integration.

## Wayland tracker controls

Open **Settings → Application → Wayland screen capture** to configure:

- **Enable Wayland screen capture**, the master permission. It remains off by
  default.
- **FSS tuning completion**, off by default.
- **First-footfall detection**, off by default.
- **Surface Mining Rhino rig tracking**, off by default.

The individual permissions are new in RC49.3. On an upgrade from RC49.2 or an
earlier build, SrvSurvey keeps the existing master preference but treats each
missing tracker permission as off. No tracker will use the portal until it is
explicitly selected.

These controls govern only the Wayland ScreenCast fallback used when normal X11
capture is unavailable. Windows and native X11 capture behavior is unchanged.
FSS tuning and Rhino rig tracking must also be enabled in their normal feature
settings before they will request an image.

Turning off one tracker closes that tracker's active portal session without
disabling another permitted tracker. Turning off the master permission closes
all active Wayland capture sessions. **Choose capture source again** is available
only when the master permission and at least one tracker are enabled.

## Wayland capture reliability

- PipeWire portal startup no longer uses `DllImport SetLastError`, which .NET
  rejects when runtime marshalling is disabled. This was the immediate failure
  shown in the supplied RC49.0 logs after a window or monitor was selected.
- SrvSurvey owns a close-on-exec duplicate of the portal file descriptor and
  cleans up the PipeWire loop if connection startup fails.
- ScreenCast portal v6 uses the stable PipeWire serial; portal v5 retains the
  numeric node-ID path used by the supplied systems.
- Capture failures include the exception chain and SrvSurvey/PipeWire throw
  site, then retry with backoff instead of remaining stuck until restart.
- The selected portal source can be cleared from Settings before restarting and
  choosing the Elite Dangerous client window again.

## Suggested test sequence

1. Enable the Wayland master permission.
2. Enable only one tracker permission.
3. For FSS or Rhino, enable that detector in its normal settings panel.
4. When the desktop picker opens, select the **Elite Dangerous client window**.
5. Exercise that feature before enabling another tracker.

If capture fails, attach the current log from
`~/.local/share/SrvSurvey/logs`. The added portal and PipeWire stages should make
it clear whether the failure occurred during permission selection, remote
opening, stream negotiation, frame delivery, or cropping.

## Packaging

- Version: `2.1.3.0-rc.49.3`
- Tag: `xp-v2.1.3.0-rc.49.3`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.49.3-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.49.3-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.49.3-x86_64.AppImage`

Windows and Linux packages are self-contained. Linux packaging tools and the
AppImage runtime use versioned, checksum-verified downloads. AppImages are
updated manually through the selected XP release. Numeric Windows FileVersion
remains `2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

A compositor-approved Elite Dangerous window capture is still required to
confirm the full portal path on each target Wayland desktop. Pure native Wayland
is not yet a full-functionality overlay target.
