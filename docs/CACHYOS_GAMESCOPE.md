# CachyOS: Elite Dangerous, SrvSurvey, KDE Plasma, and Gamescope

This guide covers two related problems on **CachyOS with KDE Plasma**:

1. keeping the mouse inside Elite Dangerous; and
2. keeping SrvSurvey overlays visible and attached to Elite.

Start with the normal KDE/XWayland setup. Add Gamescope only if the pointer
still escapes. A nested Gamescope session isolates its applications from the
normal KDE desktop, so launching Elite inside Gamescope while launching
SrvSurvey normally is not a valid overlay setup.

This workflow has been reported working on CachyOS. Gamescope behavior can
still vary with the GPU, driver, Plasma, Proton, and Gamescope versions, so keep
the normal KDE/XWayland path as the recovery baseline.

For general requirements, see [Linux installation](INSTALL_LINUX.md) and
[overlay troubleshooting](Overlay_Troubleshooting.md).

## Recommended setup

- Prefer the native CachyOS/Arch packages for Steam, Gamescope, and XWayland.
  Flatpak Steam adds another sandbox boundary and is not covered by the
  same-session wrapper below.
- Use KDE Plasma Wayland with XWayland available, or use a Plasma X11 session.
  Pure Wayland without an X11 `DISPLAY` does not provide SrvSurvey's complete
  overlay path.
- First run Elite in **Borderless** mode without Gamescope and run SrvSurvey
  normally. Add the KDE window rule if overlays do not stay above the game.
- If the mouse still leaves the game, test Gamescope with
  `--force-grab-cursor`.
- If both Gamescope and SrvSurvey overlays are required, launch both programs
  into the same nested Gamescope display.

## 1. Check the desktop and packages

Open Konsole and run:

```bash
printf 'session=%s\nDISPLAY=%s\nWAYLAND_DISPLAY=%s\n' \
  "${XDG_SESSION_TYPE:-unset}" \
  "${DISPLAY:-unset}" \
  "${WAYLAND_DISPLAY:-unset}"

gamescope --version
pacman -Q gamescope xorg-xwayland
```

If either package is missing, install it with:

```bash
sudo pacman -S --needed gamescope xorg-xwayland
```

For a normal Plasma Wayland session, the expected result is:

- `session=wayland`
- `WAYLAND_DISPLAY` has a value such as `wayland-0`
- `DISPLAY` also has a value such as `:0` or `:1`

That `DISPLAY` value confirms XWayland is available. SrvSurvey uses its X11
backend for game-window tracking, transparent click-through overlays, screen
capture, and global input.

If `DISPLAY` is empty, install XWayland and then log out and back in:

```bash
sudo pacman -S --needed xorg-xwayland
```

If a full Plasma X11 session is needed as a fallback, CachyOS documents these
packages:

```bash
sudo pacman -S plasma-x11-session kwin-x11 xorg-server
```

After installing them, log out and select **Plasma (X11)** at the login screen.
Do not change desktop sessions unless the normal Wayland with XWayland path
fails.

## 2. Establish the KDE/XWayland overlay path first

Do this before adding Gamescope. It separates a KDE stacking problem from a
Gamescope isolation problem.

1. Remove any Gamescope text from Elite Dangerous -> **Properties -> General
   -> Launch Options** in Steam.
2. Start SrvSurvey normally as the same desktop user. Do not use `sudo`.
3. Start Elite Dangerous.
4. In Elite's graphics options, choose **Borderless** rather than exclusive
   fullscreen while testing.
5. Trigger an SrvSurvey overlay and confirm that it follows the Elite window.

SrvSurvey asks KWin for its on-screen-display window type automatically. If an
overlay remains behind Elite, create this manual fallback rule.

### KDE window rule for SrvSurvey overlays

1. Open **System Settings -> Window Management -> Window Rules**.
2. Select **Add New...**.
3. Add the following matching values and property:

| Setting | Value |
|---|---|
| Description | `SrvSurvey overlays` |
| Window class / application | Exact match: `SrvSurvey.Desktop SrvSurvey.Desktop` |
| Match whole window class | Yes |
| Window types | All window types |
| Window title | Regular expression: `^SrvSurvey.*overlay$` |
| Layer | Force: **On-screen display** |

![KDE Plasma Window Rules configured for SrvSurvey overlays](kde-window-rules-srvsurvey-overlays.png)

The screenshot was captured with an older build and may show **Normal window**
or an older title expression. Use the exact values in the table above.

4. Apply the rule and restart SrvSurvey, or close and reopen the affected
   overlays.

KDE's rule editor can also use **Detect Window Properties** while an overlay is
visible. Do not make the rule match the main SrvSurvey window; the title
expression is deliberately limited to overlay windows. KDE documents this in
its [Window Rules manual](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/windowspecific/index.html).

If this setup works and the mouse no longer escapes, stop here. Gamescope is
not required merely because it is installed.

## 3. Test Gamescope for mouse confinement

Valve documents the Steam pattern `gamescope [options] -- %command%`. Start
with the smallest useful command so resolution, HDR, scaling, and GPU selection
are not mixed into the test.

In Steam, open **Elite Dangerous -> Properties -> General -> Launch Options**
and enter:

```text
gamescope -f -g --force-grab-cursor -- %command%
```

The options mean:

- `-f`: make the outer Gamescope window fullscreen;
- `-g`: grab the keyboard in nested mode; and
- `--force-grab-cursor`: always use relative mouse mode so the pointer remains
  inside the Gamescope boundary.

Verify that the installed Gamescope build supports the options:

```bash
gamescope --help 2>&1 | grep -E -- 'fullscreen|grab|force-grab-cursor'
```

Useful Gamescope shortcuts include:

- **Super+F**: toggle fullscreen;
- **Super+G**: toggle keyboard grab.

Click the Gamescope window once after Elite appears, then test mouse movement
near every monitor edge. Do not add `--expose-wayland`, HDR variables, custom
Vulkan variables, scaling, or a forced resolution until the minimal command
works. Elite under Proton uses Gamescope's XWayland path; the Wayland-client
switch is not needed for this test.

### Optional fixed resolution

Only after the minimal command works, set the output and game resolutions. For
a 2560x1440 display, for example:

```text
gamescope -f -g --force-grab-cursor -w 2560 -h 1440 -W 2560 -H 1440 -- %command%
```

Lowercase `-w/-h` set the game resolution. Uppercase `-W/-H` set the outer
Gamescope output. Replace all four values with the intended resolution.
Omitting them is better for initial diagnosis.

## 4. Understand the Gamescope boundary

When Steam starts Elite with `gamescope ... -- %command%`, Elite is placed on a
nested XWayland display owned by Gamescope. A separately launched SrvSurvey is
still on KDE's normal `DISPLAY`. It cannot reliably enumerate or follow the
inner Elite window, and KDE window rules cannot manage windows inside the
nested display.

SrvSurvey has a Gamescope-aware presentation mode. When its process sees
`GAMESCOPE_WAYLAND_DISPLAY`, `GAMESCOPE_DISPLAY`, or a Gamescope desktop
identity and has an X11-compatible host, it selects one combined transparent
overlay window instead of many native overlay windows. Its log records either:

```text
Overlay presentation: CombinedWindow
```

or:

```text
Overlay presentation: MultipleWindows
```

Combined mode reduces the number of windows Gamescope must handle. It does not
move a SrvSurvey process launched on the KDE desktop into Elite's nested
display. Do not treat SrvSurvey outside Gamescope over Elite inside Gamescope as
a supported arrangement.

## 5. Launch Elite and SrvSurvey in the same Gamescope session

Use this path after both independent tests pass:

- the KDE/XWayland setup shows working SrvSurvey overlays without Gamescope;
- the minimal Gamescope launch keeps Elite's pointer confined.

The wrapper below launches SrvSurvey and Steam's expanded Elite command as
children of the same Gamescope instance. Both inherit the same nested `DISPLAY`
and Gamescope variables. It also asks SrvSurvey to use its combined host
explicitly.

### Prepare a stable AppImage path

Place the current AppImage at:

```text
~/Applications/SrvSurvey/SrvSurvey.AppImage
```

You can rename the downloaded AppImage to `SrvSurvey.AppImage`, create a
symbolic link with that name, or change `srv_app` in the wrapper to the exact
installed path. Make sure the AppImage is executable:

```bash
chmod +x "$HOME/Applications/SrvSurvey/SrvSurvey.AppImage"
```

### Create the wrapper

Create a local scripts directory and open a new file:

```bash
mkdir -p "$HOME/.local/bin"
nano "$HOME/.local/bin/elite-srvsurvey-gamescope"
```

Paste this script:

```bash
#!/usr/bin/env bash
set -Eeuo pipefail

# Change this if SrvSurvey is installed somewhere else.
srv_app="${SRVSURVEY_APPIMAGE:-$HOME/Applications/SrvSurvey/SrvSurvey.AppImage}"

if [[ ! -x "$srv_app" ]]; then
  printf 'SrvSurvey AppImage is missing or not executable: %s\n' "$srv_app" >&2
  exit 1
fi

exec gamescope -f -g --force-grab-cursor -- bash -c '
  srv_app=$1
  shift

  "$@" &
  game_pid=$!

  # Let Elite create the first Gamescope window, then start SrvSurvey on the
  # same nested display.
  sleep 5
  SRVSURVEY_OVERLAY_HOST=combined "$srv_app" &
  srv_pid=$!

  cleanup() {
    kill "$srv_pid" 2>/dev/null || true
    kill "$game_pid" 2>/dev/null || true
  }
  trap cleanup EXIT INT TERM

  wait "$game_pid"
' bash "$srv_app" "$@"
```

Save the file, exit the editor, and make it executable:

```bash
chmod +x "$HOME/.local/bin/elite-srvsurvey-gamescope"
```

### Use the wrapper from Steam

Print the wrapper's absolute path:

```bash
realpath "$HOME/.local/bin/elite-srvsurvey-gamescope"
```

Replace Elite's Steam launch options with that printed path followed by
`%command%`. For example:

```text
/home/USERNAME/.local/bin/elite-srvsurvey-gamescope %command%
```

Replace `USERNAME` with the actual value. An absolute path avoids depending on
Steam's shell-expansion behavior.

Start Elite from Steam. The expected result is:

1. one Gamescope fullscreen surface opens;
2. SrvSurvey and Elite are on the same nested XWayland display;
3. the pointer remains confined; and
4. SrvSurvey's log reports `CombinedWindow`.

If SrvSurvey replaces the game view, steals focus, remains invisible, or
cannot find Elite, stop using the wrapper and return to the KDE/XWayland setup
in section 2.

### Choose the screen-sharing source

If X11 cannot read the game pixels needed for FSS, first-footfall, or Surface
Mining detection, SrvSurvey explains the choice before KDE opens its
screen-sharing picker. Select the **Elite Dangerous** window when it is listed.
Gamescope may expose only its desktop surface; in that case, select the desktop
or monitor where Elite is running. Select one source and click **Share**.

Use **Alt+Tab** if the SrvSurvey explanation or KDE picker appears behind the
game. KDE may remember a successful selection, so the prompts normally return
only when permission is unavailable or has expired.

## 6. Confirm SrvSurvey's selected path

The default Linux log directory is:

```text
~/.local/share/SrvSurvey/logs
```

Check the latest overlay decisions with:

```bash
grep -h "Overlay presentation" "$HOME/.local/share/SrvSurvey/logs/"*.txt 2>/dev/null | tail -n 5
```

If `XDG_DATA_HOME` is customized, use `$XDG_DATA_HOME/SrvSurvey/logs` instead.
Inside Gamescope, `CombinedWindow` is the intended result. On the normal KDE
desktop, `MultipleWindows` is normal.

## 7. Troubleshooting

### Elite does not start with Gamescope

Reduce the Steam launch option to:

```text
gamescope -- %command%
```

If that works, add the options back one at a time: first `-f`, then `-g`, then
`--force-grab-cursor`. Keep custom resolution, scaling, HDR, MangoHud, and GPU
selection disabled until the basic launch is stable.

If even the minimal command fails, record the installed version and options:

```bash
gamescope --version
gamescope --help
```

Update CachyOS normally and retest before adding workarounds copied from old
forum posts. Gamescope is actively developed, and its flags and regressions can
change.

### The mouse still leaves Elite

1. Confirm `--force-grab-cursor` is present before the `--` separator.
2. Click the Gamescope surface once.
3. Press **Super+F** to make it fullscreen.
4. Press **Super+G** once to toggle keyboard grab.
5. Retest with only one monitor enabled to distinguish pointer capture from a
   multi-monitor placement problem.

### Input freezes, stutters, or the pointer disappears

Remove `--force-grab-cursor` and test again:

```text
gamescope -f -g -- %command%
```

A version-specific regression in cursor capture is different from a SrvSurvey
overlay problem.

### SrvSurvey works but overlays do not appear

1. Remove the Gamescope Steam option and prove section 2 still works.
2. Confirm SrvSurvey and Elite are running as the same desktop user.
3. Confirm `DISPLAY` is set.
4. Confirm the KDE rule matches `SrvSurvey.Desktop` and
   `^SrvSurvey.*overlay$`.
5. Look for `Overlay presentation` in the SrvSurvey log.
6. If using the wrapper, verify its AppImage path and executable bit.

Do not start SrvSurvey with `sudo`; doing so separates it from the user's
display session and application data.

### SrvSurvey does not find the commander journals

Gamescope does not change where Proton stores journals. SrvSurvey searches the
default Steam and Flatpak Steam Proton prefixes, extra Steam libraries,
Heroic/Epic, Lutris, Bottles, and conventional Wine prefixes. Check the
candidate list in SrvSurvey Diagnostics.

For an unusual location, start SrvSurvey with the directory containing
`Journal.*.log` and `Status.json`:

```bash
SRVSURVEY_JOURNAL_DIR="/absolute/path/to/Elite Dangerous" \
  "$HOME/Applications/SrvSurvey/SrvSurvey.AppImage"
```

For the same-session wrapper, set `SRVSURVEY_JOURNAL_DIR` before the SrvSurvey
command in the script or export it before starting Steam.

### Fullscreen opens on the wrong monitor

First set the intended monitor as **Primary** in KDE System Settings and restart
the test. Gamescope also has a `--display-index` option in current builds, but
display numbering is machine-specific. Inspect `gamescope --help` and test an
index only after the basic command works.

## 8. Undo the setup

Gamescope has no effect on Elite after its Steam launch option is removed.

1. Clear Elite's **Steam Launch Options**.
2. Delete the optional wrapper:

   ```bash
   rm "$HOME/.local/bin/elite-srvsurvey-gamescope"
   ```

3. In **System Settings -> Window Management -> Window Rules**, remove the
   `SrvSurvey overlays` rule if it is no longer wanted.
4. Log out and back into the original Plasma session if an X11 fallback session
   was tested.
5. Start SrvSurvey normally, then start Elite in Borderless mode and retest.

Leaving Gamescope and XWayland installed is harmless. Avoid removing Gamescope
blindly because it may be a dependency of a CachyOS gaming meta-package; review
Pacman's proposed removals before accepting a package-removal command.

## Sources

- [Valve Gamescope README](https://github.com/ValveSoftware/gamescope)
- [Valve Gamescope option definitions](https://github.com/ValveSoftware/gamescope/blob/master/src/main.cpp)
- [Arch Linux Gamescope package](https://archlinux.org/packages/extra/x86_64/gamescope/)
- [CachyOS KDE documentation](https://wiki.cachyos.org/configuration/desktop_environments/kde/)
- [KDE Window Rules documentation](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/windowspecific/index.html)
- [SrvSurvey Linux installation](INSTALL_LINUX.md)
- [SrvSurvey overlay troubleshooting](Overlay_Troubleshooting.md)
- [SrvSurvey Gamescope mode selector](../src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationMode.cs)
