# Overlay Troubleshooting (Linux)

Overlays in SrvSurvey rely on X11 (or XWayland) window management features: click-through, always-on-top / layer placement, window tracking against the Elite Dangerous game window, and (where supported) global input. Most GNOME, Cinnamon, and Xfce setups work out of the box once the display-server requirements in [INSTALL_LINUX.md](INSTALL_LINUX.md) are met. KDE Plasma is stricter about which windows may paint over exclusive full-screen applications and therefore often needs an explicit window rule.

## KDE Plasma — overlays not appearing or not staying above Elite

KDE Plasma can refuse to place normal application windows above an exclusive full-screen game. A community report confirmed that forcing the overlay windows onto the **On-screen display** layer resolves the problem.

### Create the window rule

1. Open **System Settings → Window Management → Window Rules**.
2. Click **Add Rule…** (or the **+** button).
3. Configure the rule exactly as shown below (or use **Detect Window Properties** while an overlay is visible and then refine the match).

| Setting | Value |
|---------|-------|
| **Description** | `SRV Survey Overlays` |
| **Window class (application)** | Exact match → `SrvSurvey.Desktop SrvSurvey.Desktop` |
| **Match whole window class** | Yes |
| **Window types** | Normal window |
| **Window title** | Regular expression → `^SrvSurvey .* overlay$` |
| **Layer** | Force → **On-screen display** |

4. Click **Apply**.

(The matching rule dialog looks like the System Settings → Window Rules page with the values above filled in. You can also use **Detect Window Properties** while an overlay is visible to capture the class and title, then set the Layer to On-screen display.)

The regular expression matches the titles used by the current Avalonia overlays (they begin with `SrvSurvey` and end with `overlay`). If a future overlay uses a different title pattern you can widen the expression or add a second rule.

After applying the rule, restart SrvSurvey (or simply close and re-open the affected overlays). The overlays should now appear above Elite Dangerous even when the game is exclusive full-screen.

### Why this is needed on Plasma

Plasma is more restrictive than GNOME about the stacking order of windows relative to exclusive full-screen clients. Setting the layer to **On-screen display** places the overlays in the same category as system notifications and on-screen indicators, which are allowed to paint above full-screen applications.

## Other desktop environments

- **GNOME / Mutter**: Usually works without extra configuration when running under X11 or XWayland.
- **Xfce / Cinnamon / MATE**: Generally work once the X11 libraries listed in the install guide are present.
- **Pure Wayland (no XWayland)**: Not supported for full overlay functionality. Enable XWayland or switch to an Xorg session.

## Still not working?

1. Confirm both Elite Dangerous and SrvSurvey are running as the **same user** on the **same display** (`echo $DISPLAY`).
2. Verify the session is X11 or XWayland (`echo $XDG_SESSION_TYPE` and `echo $WAYLAND_DISPLAY`).
3. Check that the window class reported by KDE’s “Detect Window Properties” matches `SrvSurvey.Desktop`.
4. Temporarily disable any other compositor effects or “focus stealing prevention” rules that might interfere.
5. Open an issue on the [repository](https://github.com/Fenris159/SrvSurvey/issues) with the output of:

   ```bash
   printf 'session=%s\nDISPLAY=%s\nWAYLAND_DISPLAY=%s\n' \
       "${XDG_SESSION_TYPE:-unset}" \
       "${DISPLAY:-unset}" \
       "${WAYLAND_DISPLAY:-unset}"
   ```

   and a screenshot of any relevant window rules.

See also the general [Linux Troubleshooting](Linux_Troubleshooting.md) document.
