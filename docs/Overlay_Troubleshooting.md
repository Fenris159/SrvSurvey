# Overlay Troubleshooting (Linux)

Overlays in SrvSurvey rely on X11 (or XWayland) window management features: click-through, always-on-top / layer placement, window tracking against the Elite Dangerous game window, and (where supported) global input. Most GNOME, Cinnamon, Xfce, and KDE Plasma setups should work out of the box once the display-server requirements in [INSTALL_LINUX.md](INSTALL_LINUX.md) are met.

## Gamescope and the combined overlay host

For the complete CachyOS workflow, including mouse confinement and a launcher
that starts Elite and SrvSurvey in the same nested display, see
[CachyOS: Elite Dangerous, SrvSurvey, KDE Plasma, and Gamescope](CACHYOS_GAMESCOPE.md).

Ordinary Windows, X11, and XWayland sessions continue to use one native window
per live overlay. A verified Gamescope session uses one transparent
canvas registered with the compositor. The existing controls, positioning,
opacity, and suppression models supply its content.

### SteamOS Gaming Mode

SrvSurvey verifies the Gamescope window manager and XWayland server ID at startup.
Before Avalonia initializes, it selects the compositor's primary server (ID `0`),
whose actual `DISPLAY` can be `:2`, `:7`, or another value. If SrvSurvey inherits
a game server instead, it finds the same user's Steam display and verifies that
it belongs to the same compositor. An ambiguous or inaccessible primary display
disables overlays and records the reason in the application log.

Start the native Linux AppImage in the same user's Gaming Mode session. Elite
can launch normally through Steam/Proton; mini-ed-launcher is optional. Its
additional-process configuration can continue to launch the AppImage unchanged.
SrvSurvey discovers Elite's display from the running process and follows game
startup, focus changes, and restart. A separate non-Steam SrvSurvey shortcut is
also supported by this discovery; return to Elite after configuring SrvSurvey.

**Turn the Deck performance overlay off.** Gamescope exposes one external-overlay
slot, which the performance HUD also uses. Simultaneous display is not supported.
The SrvSurvey canvas covers the compositor output, while panel anchors follow
Elite's projected viewport, including letterboxing and Gamescope scaling.

The HUD starts passive. Use the existing live-interaction shortcut to click its
controls or drag panels while playing, then press it again to save positions and
return pointer input to Elite. Live mode reserves the entire canvas's pointer
input, including blank areas; keyboard input stays with Elite. A separate
transparent routing window selects pointer-only focus without replacing the
game picture. Steam overlay and Quick Access Menu input requests temporarily
release the routing; live mode resumes after those requests clear. Close Steam
menus before first enabling live mode.

The position editor remains available for positions, opacity, and scale.
Desktop Mode is the recommended configuration environment until Deck controller
navigation is validated. Configuration windows are not external overlays.
Text entry in HUD controls is not supported by this pointer-only mode.

X11 screen-based detection is unavailable in this mode because the primary
overlay display does not contain Elite's composited game image. Journal-driven
panels and capture of SrvSurvey's own overlay controls remain available. A
separately supported screen-sharing backend is required for image-based features.

Check the log for `Gamescope overlay display:` and `Overlay presentation:
CombinedWindow`. `SRVSURVEY_OVERLAY_HOST=separate` cannot override a verified
external-overlay session because only one canvas can occupy that slot.

### Desktop Mode, including normal Steam/Proton launches

Without Gamescope, the existing desktop overlay path applies. When Elite runs
inside nested Gamescope on an X11/XWayland desktop, SrvSurvey can discover its
display and map it through the visible desktop window owned by its Gamescope
ancestor. This does not require mini-ed-launcher or a bridge file. SrvSurvey
keeps its UI on the desktop so it survives the nested compositor closing.
Multiple ambiguous outer windows are not guessed. For a native Wayland outer
window with no X11 geometry, the bridge described below is still required.

Check the application log for either `Overlay presentation: CombinedWindow` or
`Overlay presentation: MultipleWindows`. If Gamescope detection is missing,
launch SrvSurvey in the same Gamescope environment as Elite or test explicitly:

```bash
SRVSURVEY_OVERLAY_HOST=combined ./SrvSurvey.Desktop
```

Outside a verified external-overlay session, diagnose a compositor regression with
`SRVSURVEY_OVERLAY_HOST=separate`. The override is read at startup.

### SrvSurvey on the desktop with native Wayland Gamescope

A native Wayland Gamescope session can keep Elite on a nested X11 display while
SrvSurvey runs on the desktop XWayland display. In this arrangement SrvSurvey
cannot discover Elite through the desktop X11 window tree. SrvSurvey can instead
read a **Gamescope game-window bridge marker** from the user's
`$XDG_RUNTIME_DIR`. Its Gamescope launch wrapper creates the marker after it
finds the nested `DISPLAY`, and removes it when Gamescope exits. The user does
not create the marker for each game session.

The marker is named `GamescopeGameWindowBridge.<Gamescope PID>` and has three
lines: the Gamescope process start time from `/proc/<PID>/stat`, the nested X11
display (for example `:2`), and the desktop XWayland rectangle of the monitor
hosting fullscreen Gamescope (`x y width height`). SrvSurvey verifies the live
process before using the marker. It then tracks Elite on the nested display
while placing its ordinary, separately interactive overlay windows on the
desktop display. If there is no valid marker, SrvSurvey keeps using its normal
desktop game-window tracker.

Keyboard shortcuts also follow this bridge. SrvSurvey listens for game keys on
the nested display alongside its desktop keyboard listener, so shortcuts work
while Elite is focused and while interacting with desktop overlays. The nested
listener closes when the game display ends and reconnects when a new bridge is
available. Existing shortcut bindings and the keyboard-enable setting apply to
both listeners.

### Automatic Linux keyboard input

When keyboard shortcuts are enabled, SrvSurvey detects the desktop's Global
Shortcuts portal and silently restores previously approved shortcuts where the
desktop supports it, including a selection that approved only some shortcuts.
Startup, reconnects, and binding changes never open the desktop menu automatically.
Journal processing keeps running while shortcut discovery completes.

Use **Desktop shortcut settings** in **Settings → Input** to approve or configure
shortcuts explicitly. Its status distinguishes active bindings, missing approval,
and unsupported portals. The button works both for active sessions and when
approval is needed; keyboard input must be enabled. No app restart is needed.
Version-two portals open configuration for the current session. Older GNOME
desktops open SrvSurvey's **Applications** settings; choose **Global Shortcuts**
there. GNOME's existing approval dialog skips previously approved actions, so
requesting those bindings again cannot reopen that dialog. Other older portals
replace the session with a fresh binding request. Disabling keyboard input cancels pending
approval and closes the session. A declined request is not retried automatically.

SrvSurvey's key-code fields provide the requested shortcuts. The desktop can
assign different keys for portal input, which are shown in the desktop shortcut
status. Desktop edits take effect for portal actions but do not rewrite the
application key-code fields: the portable API provides a readable key description,
not a portable key-code assignment. Existing raw listeners apply changes to
application key codes immediately; changed portal bindings await explicit approval
through the button. Keep the requested and approved keys consistent if you switch
between portal and raw keyboard input.

SrvSurvey also discovers a running Elite process's local X11 display when its
same-user process metadata is accessible. This allows keyboard shortcuts on a
nested display without a Gamescope bridge. The X server must still permit the
connection and provide its RECORD extension. The bridge remains useful for
overlay placement; automatic keyboard discovery does not infer desktop window
coordinates from a nested display. With multiple Elite clients, discovery prefers
the focused client. As a fallback it follows the configured overlay monitor when
desktop window geometry or a validated bridge maps that monitor to a game display.
If several distinct game displays remain unmapped or equally suitable, it waits
instead of guessing which one to listen to.

One shared source is learned for all keyboard shortcuts only after an activation
with confirmed Elite focus: the desktop portal takes priority over a nested
display, which takes priority over the desktop keyboard hook. A portal that did
not accept all configured keyboard shortcuts cannot become the shared source.
Duplicate reports of the same press are combined into one action. The choice
stays in use for the session and is released when that source disconnects or
bindings or the game identity change. If a connected source stops delivering
shortcuts, repeated, separated shortcut presses received by another source with
confirmed game focus allow automatic recovery. Idle time alone does not switch
sources. Using shortcuts in SrvSurvey or before Elite runs does not choose the
game input source. Changes to portal registrations made in desktop settings update
availability without asking for approval again during gameplay.

Below **Enable key chords** in **Settings → Input**, **Keyboard input source**
offers Automatic, Desktop keyboard, Game display, and Wayland portal. One saved
choice applies to every keyboard shortcut in that commander profile. Unavailable
sources cannot be newly selected; an existing manual choice remains selected if
its provider disappears and does not silently fall back. The status explains the
chosen source, focus, and the last configured shortcut received, including an
ignored duplicate. It does not record arbitrary keystrokes.

**Reset input detection** returns to Automatic and clears remembered input and
held-key state. It preserves shortcut bindings and desktop approval, works without
restarting SrvSurvey, and waits for a working shortcut with Elite focused before
learning a source again.

Native Wayland game input requires a desktop backend implementing Global
Shortcuts. The portal reports approved shortcut actions rather than arbitrary
keystrokes or the focused window. When Elite's foreground window can be tracked,
SrvSurvey preserves its foreground checks. Otherwise portal shortcuts remain
global while Elite is running, and stop affecting SrvSurvey when Elite exits.
Those unconfirmed-focus activations do not lock in an input source.
Application text entry continues to suppress shortcut actions. A restricted
nested display with no accessible process metadata or X11 connection requires
portal support. See the [Global Shortcuts portal specification](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html).

Portal support depends on the desktop backend, rather than the Linux distribution
name. A message saying `Could not register app ID: App info not found for
'io.github.fenris159.SrvSurvey'` means the portal could not discover SrvSurvey's
desktop entry; it does not establish that Global Shortcuts is unsupported.
Host registration requires an installed `io.github.fenris159.SrvSurvey.desktop`
entry. A desktop file inside an AppImage alone is not an installed entry visible
to the portal service. See the [host Registry specification](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.host.portal.Registry.html).

SrvSurvey prepares its host desktop identity before requesting portal shortcuts,
independently of Frontier account linking. An unintegrated AppImage uses a hidden
entry in the current user's XDG applications directory, with the stable AppImage
path rather than its temporary mount. Existing launcher visibility, icons, actions,
and MIME associations are preserved. Shortcut discovery does not change default
protocol handlers or require `desktop-file-utils`.

Missing application metadata and unsupported shortcut interfaces stop automatic
retries and leave the other keyboard listeners available. Repair desktop
integration, then restart SrvSurvey or disable and re-enable key chords to retry.
Temporary portal outages still reconnect automatically, with one diagnostic per
outage rather than a repeated message on every reconnect attempt. Restoring a
session does not open an approval dialog during gameplay; new approval continues
to require an explicit settings request.

The helper [PublishGamescopeGameWindowBridge.sh](../scripts/PublishGamescopeGameWindowBridge.sh)
writes this marker atomically. A native Wayland Gamescope launch wrapper should
call it **after** it knows the Gamescope PID and nested X11 `DISPLAY`, pass the
desktop output name shown by `xrandr --query`, and remove the returned path when
Gamescope stops. For example, inside that wrapper:

```bash
# scope_pid is the PID of the running Gamescope process; nested_display is the
# DISPLAY from Gamescope's XWayland child, not the desktop DISPLAY.
bridge_marker=$(PublishGamescopeGameWindowBridge.sh \
    "$scope_pid" "$nested_display" "$game_output")
# Add to the wrapper's existing exit cleanup:
[[ -z ${bridge_marker:-} ]] || unlink "$bridge_marker"
```

The bridge applies when Gamescope fills the chosen monitor; its rectangle is
the whole monitor. For a smaller or movable Gamescope window, the host rectangle
must instead track that window. The tested native Wayland setup also toggles
Gamescope's relative mouse mode when SrvSurvey enables mouse interaction; see
[the Ubuntu Gamescope guide](UBUNTU_26_GAMESCOPE.md#9-srvsurvey-and-the-gamescope-boundary).

## Dragging panels across multiple monitors

Under **Global overlay behavior**, choose **Overlay monitor**, then enable
**Keep overlays on the selected monitor**. This confines the panel body to that
display while dragging live overlays or editor previews, including displays
with negative coordinates or different vertical offsets. The boundary is
captured when each drag starts so a settings change cannot alter a drag midway.
The lock is off by default.

**Automatic** uses the display containing most of Elite's window, or the primary
display when Elite is not detected. A manually selected display takes precedence
for the drag boundary. Existing game-relative layout coordinates are retained
when saving positions. In combined presentation, the panel must also remain
inside the game-sized host; a selected display outside that host falls back to
the host boundary.

Dragging stops on release, capture loss, or a subsequent pointer event that says
the left button is up. The last accepted move is applied before live positions
are saved. This also protects against a missing release event.

For X11/XWayland diagnosis, look for `Overlay drag:` in the application log under
`~/.local/share/SrvSurvey/logs/` (or the configured XDG data directory). Each drag
records its starting position, scale and lock boundary, a native/reported cursor
sample when available, and its stop reason and largest measured coordinate
difference. Native cursor samples can be ahead of queued input events, so a
nonzero `maxPointerDifferencePx` alone does not establish a coordinate bug.
Include the trace for a problematic drag when reporting continued drifting.

## KDE Plasma — overlays not appearing or not staying above Elite

SrvSurvey classifies X11/XWayland runtime overlays, edit previews, and the position editor as utility windows before they appear. KDE's **Slide Plasma notifications in and out** effect and standard popup opening/closing animations exclude this type. Notification and on-screen-display window types are no longer used. Other window managers can use the normal-window fallback if utility windows are unsupported.

The utility classification applies with **Bypass Window Management** on or off. Bypass lets SrvSurvey place and raise separate windows directly; it does not turn off the compositor's effects globally. Desktop-wide effects and custom animation scripts remain controlled by KDE.

Edit previews remain interactive and use SrvSurvey's direct pointer dragging. With bypass off, KDE manages stacking and can keep an exclusive fullscreen game above topmost utility windows. Use borderless mode or the following stacking rule if this occurs.

If overlays still remain behind Elite, use the following manual rule as a fallback.

### Create the window rule

1. Open **System Settings → Window Management → Window Rules**.
2. Click **Add Rule…** (or the **+** button).
3. Configure the rule exactly as shown below (or use **Detect Window Properties** while an overlay is visible and then refine the match).

| Setting | Value |
|---------|-------|
| **Description** | `SRV Survey Overlays` |
| **Window class (application)** | Exact match → `SrvSurvey.Desktop SrvSurvey.Desktop` |
| **Match whole window class** | Yes |
| **Window types** | All window types |
| **Window title** | Regular expression → `^SrvSurvey.*overlay$` |
| **Layer** | Force → **On-screen display** |

![KDE Plasma Window Rules configured for SrvSurvey overlays](kde-window-rules-srvsurvey-overlays.png)

The screenshot was captured with an older build and may show **Normal window** or the previous longer title expression. Use the exact values in the table so the rule also matches utility windows.

4. Click **Apply**.

You can also use **Detect Window Properties** while an overlay is visible to capture the class and title, then set the Layer to **On-screen display**.

Every runtime overlay, combined host, stream-capture surface, position preview, and position editor now starts with `SrvSurvey` and ends with `overlay`. The regular expression therefore covers all overlay surfaces without matching the main `SrvSurvey` window.

After applying the rule, restart SrvSurvey (or simply close and re-open the affected overlays). The overlays should now appear above Elite Dangerous even when the game is exclusive full-screen.

### Why the OSD layer is used on Plasma

The **On-screen display** stacking layer can place overlays above exclusive fullscreen applications. Set the **Layer** property, keeping the utility window type intact; changing the type to a notification or on-screen display can make the overlay eligible for KDE animations again.

## Other desktop environments

- **GNOME / Mutter**: Usually works without extra configuration when running under X11 or XWayland.
- **Xfce / Cinnamon / MATE**: Generally work once the X11 libraries listed in the install guide are present.
- **Pure Wayland (no XWayland)**: Not supported for full overlay functionality. Enable XWayland or switch to an Xorg session.

## Still not working?

1. Confirm Elite and SrvSurvey run as the same user. They normally need the same display; the native Wayland Gamescope bridge above supports separate desktop and nested displays.
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

See also the general [Linux Troubleshooting](Linux_Troubleshooting.md) document
and the [CachyOS and Gamescope guide](CACHYOS_GAMESCOPE.md).
