# Ubuntu 26.04: Elite Dangerous, Gamescope, and SrvSurvey

This guide records a working Elite Dangerous setup for **Ubuntu 26.04 LTS,
GNOME Wayland, Steam/Proton, and a multi-monitor desktop**. Its main goal is to
keep the mouse inside Elite without also trapping or fullscreening the Frontier
launcher.

It also covers the display, game-file migration, and graphics choices used in
the tested setup. Controller configuration is deliberately out of scope.

For general SrvSurvey installation and display-server requirements, start with
[Install SrvSurvey on Linux](INSTALL_LINUX.md).

## Result and important limitation

The final behavior is:

1. Steam opens Frontier's launcher as a normal, decorated, movable window.
2. When Gamescope selects the actual Elite client, the outer window moves to
   fullscreen on the intended monitor.
3. Relative mouse mode turns on only for Elite, so the pointer cannot cross to
   another monitor.
4. When the game closes or Gamescope returns to the launcher, relative mode
   turns off and the launcher becomes a normal window again.

This launcher/game split is **not a stock Gamescope feature**. Upstream
Gamescope 3.16.29 has `--force-grab-cursor`, but that option applies to the
whole session and therefore traps the launcher too. The setup below applies a
small patch that adds a runtime `force_relative_mouse` control, then toggles it
with `gamescopectl` only while Elite's game surface is selected.

If trapping the launcher is acceptable, try stock Gamescope first. Otherwise,
both the patch and the matching wrapper are required.

## Tested configuration

This procedure was verified on 2026-09-19 with:

| Component | Tested value |
|---|---|
| Operating system | Ubuntu 26.04 LTS |
| Desktop | GNOME 50.1 on Wayland with XWayland |
| Gamescope | Locally built tag `3.16.29` |
| Ubuntu Gamescope package | `3.16.20+ds-1`, left installed and unchanged |
| Steam app | Elite Dangerous, app ID `359320` |
| Gamescope backend | SDL hosted through X11/XWayland |
| Primary display | 3840x2160 at 120 Hz, 150% GNOME scale |
| Secondary display | Physically and logically to the left |
| Gamescope output | 2560x1440 outer window |
| Resolution exposed to Elite | 3840x2160 at 120 Hz |
| Mouse sensitivity multiplier | `2.25` for this scaling/resolution ratio |

Ubuntu 26.04 currently packages Gamescope `3.16.20+ds-1`, while `3.16.29` is
the newest numbered upstream tag as of the verification date. This guide uses
a versioned, side-by-side install under `~/.local/opt`; it does not overwrite
Ubuntu's package. See the [Ubuntu package](https://packages.ubuntu.com/source/resolute/gamescope)
and [upstream 3.16.29 source](https://github.com/ValveSoftware/gamescope/tree/3.16.29).

## Values every user must customize

Do not copy the tested machine's numbers without checking them. At minimum,
review these values for each installation:

| Setting | How to determine it |
|---|---|
| Primary monitor and physical refresh | Ubuntu **Settings → Displays** |
| XWayland output origin and size | `xrandr --current` |
| `ELITE_GAMESCOPE_X` / `Y` | Centering calculation in section 4 |
| Gamescope output width/height | Desired logical outer window size |
| Elite game width/height | Resolution the GPU should render |
| Gamescope refresh | A mode actually supported by the primary monitor |
| Mouse sensitivity | Start from the formula in section 4, then test |
| Gamescope version/root | The private versioned prefix actually built |
| Elite outer-window title | Inspect with `xprop _NET_WM_NAME` |
| Steam library and Proton prefix | Locate app ID `359320` in the user's Steam libraries |
| SrvSurvey executable | The user's AppImage or portable-build location |

The supplied wrapper exposes each machine-specific value as an environment
variable. Keep local values in Steam's launch option or in a separate personal
launcher; do not edit the shared script just to hard-code one computer's
monitor layout.

## 1. Establish a recovery baseline

Before changing anything:

1. Clear Elite's Steam launch options.
2. Start Elite normally and confirm it runs under Proton.
3. Select **Borderless** in Elite's display settings.
4. Set the intended display as **Primary** in Ubuntu Settings.
5. Set the physical monitor to its intended refresh rate in Ubuntu Settings.
6. Close Elite and the Frontier launcher.

Gamescope's `-r` option does not change the physical monitor mode. A 120 Hz
Gamescope session on a desktop still configured for 60 Hz cannot produce a
120 Hz display.

Record the desktop and XWayland geometry:

```bash
printf 'session=%s display=%s wayland=%s\n' \
  "${XDG_SESSION_TYPE:-unset}" \
  "${DISPLAY:-unset}" \
  "${WAYLAND_DISPLAY:-unset}"
xrandr --current
```

On a scaled GNOME Wayland desktop, XWayland geometry may be larger than the
physical panel mode. In the tested setup the 3840x2160 panel at 150% scale was
reported to XWayland as `5120x2880`. That is expected; use the geometry that
`xrandr` reports when calculating `wmctrl` placement.

## 2. Try the stock path first

Install Ubuntu's Gamescope package and the X11 utilities used for diagnosis:

```bash
sudo apt update
sudo apt install gamescope wmctrl x11-utils xwayland
```

Temporarily use this Steam launch option:

```text
gamescope --backend sdl -f --force-grab-cursor -- %command%
```

If the game starts, the mouse is confined, and launcher confinement is not a
problem, this simpler setup may be sufficient. Remove this launch option
before continuing with the patched build.

Do not combine this test with pointer-warp scripts, `XGrabPointer` helpers, or
Wine `MouseWarpOverride` settings. They change the input path and make the
result difficult to diagnose.

## 3. Build the current Gamescope tag side by side

### Install build dependencies

Gamescope's 3.16.29 README lists the following Debian-family dependencies. The
last line adds Ubuntu's `libei` development packages for the explicitly enabled
input-emulation feature.

```bash
sudo apt install \
  git meson ninja-build pkg-config cmake libpipewire-0.3-dev hwdata \
  libx11-dev libwayland-dev libvulkan-dev wayland-protocols \
  libx11-xcb-dev libxdamage-dev libxcomposite-dev libxcursor-dev \
  libxxf86vm-dev libxtst-dev libxres-dev libxmu-dev \
  libxkbcommon-dev libcap-dev libsdl2-dev libavif-dev \
  libpixman-1-dev liblcms2-dev libseat-dev libinput-dev xwayland \
  libxcb-composite0-dev libxcb-ewmh-dev libxcb-icccm4-dev \
  libxcb-res0-dev glslang-tools libluajit-5.1-dev libcatch2-dev \
  libei-dev libeis-dev wmctrl x11-utils
```

The authoritative list is the
[Gamescope 3.16.29 README](https://github.com/ValveSoftware/gamescope/blob/3.16.29/README.md#building).
Package names may change in later Ubuntu releases.

### Clone the exact tested tag

```bash
gamescope_version=3.16.29
gamescope_source="$HOME/.local/src/gamescope-${gamescope_version}"
gamescope_prefix="$HOME/.local/opt/gamescope-${gamescope_version}"

mkdir -p "$HOME/.local/src" "$HOME/.local/opt"
git clone --branch "$gamescope_version" --depth 1 --recurse-submodules \
  https://github.com/ValveSoftware/gamescope.git "$gamescope_source"
git -C "$gamescope_source" rev-parse HEAD
```

For 3.16.29, the expected commit is:

```text
8f212644c46460549035971ab914421180f61a7c
```

Do not silently substitute a newer tag. Review and retest the local patch when
upgrading because the surrounding source may have changed.

### Apply the runtime relative-mouse patch

From a SrvSurvey repository checkout:

```bash
srvsurvey_root=$(pwd)
git -C "$gamescope_source" apply --unidiff-zero --check \
  "$srvsurvey_root/docs/patches/gamescope-3.16.29-runtime-relative-mouse.patch"
git -C "$gamescope_source" apply --unidiff-zero \
  "$srvsurvey_root/docs/patches/gamescope-3.16.29-runtime-relative-mouse.patch"
```

The `srvsurvey_root` assignment assumes the current directory is the SrvSurvey
repository root. Otherwise, set it to the checkout's absolute path. The patch
uses zero-context hunks so the stored patch itself passes whitespace checks;
that is why both commands include `--unidiff-zero`.

The patch adds a Boolean Gamescope convar named `force_relative_mouse`. It
affects cursor composition and the nested relative-mouse hint, but defaults to
`false`, so an unmodified launch behaves as before. The new control is local to
this build; it is not part of stock Gamescope 3.16.29.

### Configure, compile, and install

```bash
cd "$gamescope_source"
meson setup build \
  --buildtype=release \
  --prefix="$gamescope_prefix" \
  -Dsdl2_backend=enabled \
  -Dpipewire=enabled \
  -Dinput_emulation=enabled \
  -Denable_gamescope_wsi_layer=false \
  -Denable_openvr_support=false \
  -Denable_tests=false

ninja -C build
meson install -C build --skip-subprojects
```

The WSI layer is disabled deliberately. This private build is for the nested
SDL/X11 route, not a system-wide Vulkan layer replacement.

Verify the installed binaries:

```bash
"$gamescope_prefix/bin/gamescope" --version
test -x "$gamescope_prefix/bin/gamescopectl"
```

## 4. Install and configure the Elite wrapper

The maintained wrapper is [EliteGamescope.sh](../scripts/EliteGamescope.sh).
Install it under a stable per-user path:

```bash
mkdir -p "$HOME/.local/bin"
install -m 0755 scripts/EliteGamescope.sh \
  "$HOME/.local/bin/elite-gamescope"
```

Again, run that command from the SrvSurvey repository root or use an absolute
source path.

The wrapper intentionally does **not** pass `-b`, `-f`, or
`--force-grab-cursor` at startup:

- omitting `-b` leaves normal Linux decorations on the Frontier launcher;
- omitting `-f` prevents the launcher from starting fullscreen; and
- omitting `--force-grab-cursor` prevents the launcher from inheriting a
  session-wide relative mouse mode.

It watches the Gamescope outer window title. When the title becomes exactly
`Elite - Dangerous (CLIENT)`, it fullscreens that outer window and runs:

```text
gamescopectl force_relative_mouse true
```

When the title changes back, it disables the convar, removes fullscreen, and
restores the configured launcher position. Watching the selected surface title
is safer than watching `EliteDangerous64.exe`: the process can exist before
Gamescope has actually selected its game surface.

### Set the monitor coordinates

The wrapper defaults to position `0,0`. Override that for a multi-monitor
desktop by adding variables before the wrapper path in Steam's launch option.

Find the primary XWayland output in `xrandr --current`. A line has this form:

```text
DP-2 connected primary 5120x2880+3840+0
```

For an outer launcher window of 2560x1440, center it with:

```text
x = 3840 + (5120 - 2560) / 2 = 5120
y =    0 + (2880 - 1440) / 2 = 720
```

This calculation preserves the real left/right monitor arrangement. There is
no need to pretend that a left-hand monitor is on the right merely to influence
where the game opens.

### Set resolution, refresh, and sensitivity

The wrapper's defaults reproduce the tested values:

| Environment variable | Default | Meaning |
|---|---:|---|
| `ELITE_GAMESCOPE_OUTPUT_WIDTH` | `2560` | Outer Gamescope width (`-W`) |
| `ELITE_GAMESCOPE_OUTPUT_HEIGHT` | `1440` | Outer Gamescope height (`-H`) |
| `ELITE_GAMESCOPE_GAME_WIDTH` | `3840` | Resolution exposed to Elite (`-w`) |
| `ELITE_GAMESCOPE_GAME_HEIGHT` | `2160` | Resolution exposed to Elite (`-h`) |
| `ELITE_GAMESCOPE_REFRESH` | `120` | Nested refresh/frame target (`-r`) |
| `ELITE_GAMESCOPE_MOUSE_SENSITIVITY` | `2.25` | Gamescope pointer multiplier |
| `ELITE_GAMESCOPE_X` | `0` | Outer window X coordinate |
| `ELITE_GAMESCOPE_Y` | `0` | Outer window Y coordinate |

Uppercase `-W/-H` and lowercase `-w/-h` describe different layers. Changing
Elite's in-game resolution does not automatically rewrite the wrapper. Keep
the wrapper's game dimensions and Elite's selected mode aligned.

The `2.25` mouse multiplier is not universal. It compensated for the tested
150% desktop scale and the `3840/2560 = 1.5` game-to-output ratio. A useful
starting estimate is:

```text
desktop scale × (game width / Gamescope output width)
```

Then tune by feel. Changing either resolution ratio can change perceived mouse
speed. Gamescope's
[`--mouse-sensitivity`](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/main.cpp)
is a multiplier, not a copy of GNOME's pointer-speed setting.

## 5. Configure Steam

Open **Elite Dangerous → Properties → General → Launch Options** and use one
line. For the tested monitor geometry it was:

```text
ELITE_GAMESCOPE_X=5120 ELITE_GAMESCOPE_Y=720 /home/USERNAME/.local/bin/elite-gamescope %command%
```

Replace `USERNAME` with the actual account name. Steam launch options do not
reliably expand `~`, so use the absolute path printed by:

```bash
realpath "$HOME/.local/bin/elite-gamescope"
```

Start Steam normally from the desktop and launch Elite. Do not start Steam,
Elite, or SrvSurvey with `sudo`.

Expected behavior:

1. Frontier's launcher opens in a decorated 2560x1440 window on the primary
   monitor.
2. Selecting **Play** eventually changes the Gamescope outer title to
   `Elite - Dangerous (CLIENT)`.
3. The outer window becomes fullscreen on that same monitor.
4. The mouse remains confined while Elite is selected.
5. Steam Overlay and normal mouse clicking continue to work.

The transition is polled every 250 ms, so a short delay after the game appears
is normal.

## 6. Verify the runtime switch

While the patched Gamescope session is running, find its socket from one of its
child processes:

```bash
scope_pid=$(pgrep -n gamescope)
for child_pid in $(pgrep -P "$scope_pid"); do
  tr '\0' '\n' < "/proc/${child_pid}/environ" 2>/dev/null \
    | grep '^GAMESCOPE_WAYLAND_DISPLAY='
done
```

Set the displayed value and check the registered convar:

```bash
scope_display=gamescope-0  # replace with the value printed above
GAMESCOPE_WAYLAND_DISPLAY="$scope_display" \
  "$HOME/.local/opt/gamescope-3.16.29/bin/gamescopectl" help \
  | grep force_relative_mouse
```

The expected help text includes:

```text
force_relative_mouse: Force relative mouse mode at runtime.
```

If it does not, Steam is probably using the Ubuntu binary or an unpatched
private build.

## 7. Elite display and graphics settings

In Elite, keep **Display → Fullscreen** set to **Borderless**. For the tested
4K/120 configuration, the effective values in `DisplaySettings.xml` were:

```xml
<ScreenWidth>3840</ScreenWidth>
<ScreenHeight>2160</ScreenHeight>
<VSync>false</VSync>
<FullScreen>2</FullScreen>
<DX11_RefreshRateNumerator>120000</DX11_RefreshRateNumerator>
<DX11_RefreshRateDenominator>1000</DX11_RefreshRateDenominator>
<LimitFrameRate>true</LimitFrameRate>
<MaxFramesPerSecond>120</MaxFramesPerSecond>
```

The file is normally under:

```text
~/.local/share/Steam/steamapps/compatdata/359320/pfx/drive_c/users/steamuser/AppData/Local/Frontier Developments/Elite Dangerous/Options/Graphics/DisplaySettings.xml
```

Back it up before editing. Prefer changing values through the game UI because
Elite may rewrite the XML.

The tested quality profile used native 4K (`UpscalingQuality=0` and
`SSAAMultiplier=1.0`) with high shadows, high ambient occlusion, anti-aliasing,
and a 120 FPS cap. Those are hardware-dependent choices, not Gamescope
requirements. If frame times are unstable, reduce shadows, volumetrics,
ambient occlusion, or render scale before reducing the monitor refresh rate.

VSync was left off because Gamescope already owns the nested presentation path
and Elite was capped to 120 FPS. If tearing, pacing, or power use is worse on a
different GPU/display stack, test VSync on and compare measured frame pacing.

## 8. Move Windows bindings, EDHM files, and journals safely

Launch Elite once on Linux before copying files so Proton creates app ID
`359320` and its directory structure. Close Elite and the Frontier launcher
before copying.

Define the destination once:

```bash
elite_prefix="$HOME/.local/share/Steam/steamapps/compatdata/359320/pfx"
elite_local="$elite_prefix/drive_c/users/steamuser/AppData/Local/Frontier Developments/Elite Dangerous"
elite_saved="$elite_prefix/drive_c/users/steamuser/Saved Games/Frontier Developments/Elite Dangerous"
```

If Steam uses another library, locate the prefix first:

```bash
find "$HOME" -path '*/steamapps/compatdata/359320/pfx' -type d 2>/dev/null
```

### Bindings

Copy the contents of Windows:

```text
%LOCALAPPDATA%\Frontier Developments\Elite Dangerous\Options\Bindings
```

to:

```text
$elite_local/Options/Bindings
```

Keep `.binds`, `StartPreset*.start`, and any companion files together. If a
binding names a device that Linux exposes differently, preserve the original
as a backup and let Elite create a fresh Linux binding for comparison. Do not
bulk-replace device identifiers without first confirming which input the old
identifier represented.

### Journals

Copy or merge Windows:

```text
%USERPROFILE%\Saved Games\Frontier Developments\Elite Dangerous
```

into:

```text
$elite_saved
```

An `rsync` merge preserves existing Linux journals:

```bash
mkdir -p "$elite_saved"
rsync -a --ignore-existing "/path/to/windows/Elite Dangerous/" "$elite_saved/"
```

SrvSurvey searches the normal Steam/Proton journal path automatically. Use
`SRVSURVEY_JOURNAL_DIR` only if the prefix is stored somewhere its automatic
Steam-library search cannot discover.

### EDHM

EDHM can place files in both the Elite product directory and the user's Elite
options directory. Back up both locations, then copy only the EDHM shader,
theme, and override files that match the installed Elite product. In the
tested installation these included the product's `d3dx.ini`/shader content and
`Options/Graphics/GraphicsConfigurationOverride.xml`.

Do **not** replace Linux `DisplaySettings.xml`, `Settings.xml`, or the active
graphics preset with the Windows copies. Those files contain monitor,
resolution, refresh, adapter, and graphics choices that should be regenerated
or tuned for the Linux/Gamescope environment.

Steam may replace files in the product directory during **Verify integrity**
or a game update. Keep an EDHM backup or reinstall/reapply EDHM after such an
operation.

## 9. SrvSurvey and the Gamescope boundary

Gamescope gives Elite its own nested XWayland display. A SrvSurvey process
started on the ordinary GNOME desktop is outside that boundary and cannot
reliably enumerate or track the inner Elite window.

For overlays, Elite and SrvSurvey must run as the same desktop user on the same
nested display. SrvSurvey detects Gamescope and uses its combined transparent
overlay host. Its log should contain:

```text
Overlay presentation: CombinedWindow
```

The wrapper in this guide reproduces the tested Elite launcher and confinement
behavior; it does not automatically launch SrvSurvey. Use the same-session
launch pattern described in
[the CachyOS Gamescope guide](CACHYOS_GAMESCOPE.md#5-launch-elite-and-srvsurvey-in-the-same-gamescope-session)
when overlays are required, preserving this guide's patched Gamescope path and
runtime mouse toggling.

If screen capture falls back to the Wayland portal, select the Elite window or
the Gamescope surface when GNOME asks. See
[Game screen capture on Wayland](INSTALL_LINUX.md#game-screen-capture-on-wayland).

## 10. Approaches that did not work cleanly

The following were tested and removed:

- **Wine `MouseWarpOverride`:** forced the pointer toward the bottom-right and
  produced unusable motion.
- **Moving the secondary monitor to the other side in software:** influenced
  initial placement but made the desktop topology incorrect.
- **A global `XGrabPointer` helper:** confined the pointer but interfered with
  clicks and Steam Overlay, and contributed to false “not responding” reports.
- **XFixes pointer barriers:** passed a synthetic X11 test but did not stop the
  real Wayland pointer crossing monitors.
- **A repeated X11 pointer-warp clamp:** flickered and split interaction between
  the game and the neighboring monitor.
- **Gamescope's global `--force-grab-cursor`:** confined the game but also
  captured the launcher and could make its mouse behavior awkward.
- **The default nested Wayland route on this stack:** was less stable than the
  explicit `SDL_VIDEODRIVER=x11 --backend sdl` route.
- **Matching only the Elite process:** enabled fullscreen/confinement too early.
  Matching the selected outer window title avoided capturing the launcher.

No separate pointer-confinement service is needed with the final setup.

Upstream has open reports involving forced grabbing and sensitivity, including
[slow motion under display scaling](https://github.com/ValveSoftware/gamescope/issues/1776)
and [3.16.x forced-grab instability](https://github.com/ValveSoftware/gamescope/issues/1851).
These reports are reasons to keep the rollback path, not proof that every
machine will fail.

## 11. Troubleshooting

### The game starts on the wrong monitor

Confirm the intended monitor is Primary in Ubuntu Settings, then recalculate
`ELITE_GAMESCOPE_X` and `ELITE_GAMESCOPE_Y` from `xrandr --current`. The wrapper
first positions the launcher; GNOME then fullscreens that same outer window on
its current monitor.

Do not rearrange monitors just to work around placement.

### The launcher becomes fullscreen

Confirm that the wrapper is matching the exact game title and that its command
does not include `-f` or `-b`. Inspect the title live:

```bash
xprop _NET_WM_NAME
```

Then click the Gamescope outer window. The launcher was `elite launcher`; the
actual game was `Elite - Dangerous (CLIENT)` on the tested English client.
Localized or future builds may require `ELITE_GAMESCOPE_GAME_TITLE` to be
changed.

### The mouse still escapes

Check, in order:

1. the Steam launch option points to the private wrapper;
2. the wrapper points to the patched private Gamescope prefix;
3. `gamescopectl help` lists `force_relative_mouse`;
4. the outer title matches `ELITE_GAMESCOPE_GAME_TITLE`; and
5. no old pointer helper or Wine mouse-warp override is active.

### The pointer is too slow or too fast

Adjust `ELITE_GAMESCOPE_MOUSE_SENSITIVITY`. Recalculate after changing the
desktop scale, Gamescope output size, or Elite game size. Test small changes;
upstream also has a report of sensitivity dead ranges on some stacks.

### Steam Overlay or mouse clicks stop working

Remove any separate grab/barrier/warp helper and restart Steam. The final
wrapper relies only on Gamescope's own relative input mode. If the problem
persists, clear Elite's launch option and confirm the plain Proton baseline.

### Black tiles or flickering appear along the top edge

On the tested Strix Halo/RADV system, the launcher and Elite could show black,
stair-step-shaped tiles flickering along the top of the Gamescope window. The
artifacts were easiest to see in the launcher, system map, and other static
menus. They also appeared in screenshots, which distinguished the problem from
a monitor, cable, or physical scanout fault.

This was resolved by disabling AMD Delta Color Compression (DCC) for the
**Gamescope process only**. Make these two changes to the wrapper's Gamescope
launch stanza while leaving its other options unchanged:

```diff
-SDL_VIDEODRIVER=x11 "$gamescope_bin" \
+SDL_VIDEODRIVER=x11 RADV_DEBUG=nodcc "$gamescope_bin" \
     --backend sdl \
     ...
-    -- "$@" &
+    -- env -u RADV_DEBUG -- "$@" &
```

The `env -u RADV_DEBUG` after `--` is important: it prevents the workaround
from propagating into Elite and the Frontier launcher, so the game keeps its
normal RADV/DCC path. Do not set `RADV_DEBUG=nodcc` globally or add it directly
to Steam's launch option without this child-process boundary.

Treat this as an AMD-specific troubleshooting workaround, not a required part
of the default setup. It was verified with a Radeon 8060S/8050S Strix Halo GPU,
Mesa 26.0.8, and Gamescope 3.16.29; the exact affected hardware and driver
range may differ. On that machine the artifacts disappeared in both internal
and desktop screenshots, no new GPU errors appeared, and pointer response also
improved. The pointer improvement is an observation from that system, not a
guaranteed effect.

After a Mesa or Gamescope update, retest without the workaround. To remove it,
reverse the two changed lines shown above. This symptom is consistent with
an independently documented Gamescope/RADV DMA-BUF modifier mismatch involving
DCC, but the workaround should still be validated on each affected machine.

### Gamescope or Steam closes unexpectedly

Reduce the setup to the plain baseline, then test the private binary directly:

```bash
"$HOME/.local/opt/gamescope-3.16.29/bin/gamescope" \
  --backend sdl -- vkcube
```

If `vkcube` is unavailable, install `vulkan-tools` or test with another simple
X11 client. Add resolution, refresh, and the wrapper only after the private
binary remains open.

## 12. Updating and rollback

### Updating Gamescope

The private versioned prefix survives Ubuntu package and Steam updates. It does
not update itself. For a new upstream tag:

1. clone into a new source directory;
2. test whether the patch still applies;
3. review the surrounding input code even if it applies cleanly;
4. build into a new versioned prefix;
5. set `ELITE_GAMESCOPE_VERSION` or `ELITE_GAMESCOPE_ROOT` for a test; and
6. keep the known-good 3.16.29 prefix until the new build is verified.

Do not install the private build over `/usr` and do not remove Ubuntu's package
just to use this wrapper.

### Roll back completely

1. Clear Elite's Steam launch option.
2. Start Elite normally and confirm the plain Proton path.
3. Remove the optional wrapper copy:

   ```bash
   rm "$HOME/.local/bin/elite-gamescope"
   ```

4. After confirming nothing references it, remove only the explicit private
   version directories:

   ```bash
   rm -r "$HOME/.local/opt/gamescope-3.16.29"
   rm -r "$HOME/.local/src/gamescope-3.16.29"
   ```

These commands do not touch Ubuntu's `/usr/games/gamescope` package install.

## Sources

- [Valve Gamescope 3.16.29 README](https://github.com/ValveSoftware/gamescope/blob/3.16.29/README.md)
- [Valve Gamescope 3.16.29 option definitions](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/main.cpp)
- [Valve `gamescopectl` implementation](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/Apps/gamescopectl.cpp)
- [Valve convar registry](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/convar.cpp)
- [Ubuntu 26.04 Gamescope source package](https://packages.ubuntu.com/source/resolute/gamescope)
- [Gamescope/RADV DCC and DMA-BUF modifier investigation](https://www.atty303.ninja/notes/Gamescope%E3%81%AEfilter%E3%81%A7%E9%BB%92%E7%94%BB%E9%9D%A2%E3%81%AB%E3%81%AA%E3%82%8B%E5%8E%9F%E5%9B%A0%E3%82%92DMA-BUF-modifier%E3%81%BE%E3%81%A7%E8%BF%BD%E3%81%A3%E3%81%9F)
- [Detailed source-verification notes](research/ubuntu-26-gamescope-elite-setup-sources.md)
- [SrvSurvey Linux installation](INSTALL_LINUX.md)
- [SrvSurvey overlay troubleshooting](Overlay_Troubleshooting.md)
