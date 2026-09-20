# Ubuntu 26.04, Gamescope, and Elite Dangerous input confinement research

Date checked: 2026-09-19

## Conclusion

Ubuntu 26.04 LTS (`resolute`) ships Gamescope `3.16.20+ds-1`, while the newest
numbered upstream tag is `3.16.29` at commit
`8f212644c46460549035971ab914421180f61a7c`. The working local setup therefore
installs a side-by-side source build of 3.16.29 rather than replacing Ubuntu's
package ([Ubuntu source package](https://packages.ubuntu.com/source/resolute/gamescope);
[upstream 3.16.29 tree](https://github.com/ValveSoftware/gamescope/tree/3.16.29)).

The important behavior is **not available in stock Gamescope 3.16.29**.
Upstream `--force-grab-cursor` enables relative mouse mode for the entire
Gamescope session. That also captures the Frontier launcher. The working build
adds a runtime Boolean convar named `force_relative_mouse`; a wrapper leaves it
off for `EDLaunch.exe`, then enables it through `gamescopectl` only while the
outer Gamescope window title is `Elite - Dangerous (CLIENT)`.

This distinction must be explicit in a public guide: installing upstream
3.16.29 and copying only the wrapper will not reproduce the result. The source
patch must be applied and the patched `gamescope` and matching `gamescopectl`
must be built and installed together.

## Version facts

| Item | Verified value | Primary source |
|---|---:|---|
| Newest numbered upstream tag | `3.16.29` | [Upstream tag/tree](https://github.com/ValveSoftware/gamescope/tree/3.16.29) |
| Tag commit | `8f212644c46460549035971ab914421180f61a7c` | [Commit](https://github.com/ValveSoftware/gamescope/commit/8f212644c46460549035971ab914421180f61a7c) |
| Ubuntu 26.04 package | `3.16.20+ds-1` in multiverse | [Ubuntu package source](https://packages.ubuntu.com/source/resolute/gamescope) |
| Local patched source | `~/.local/src/gamescope-3.16.29` | Inspected local checkout; `git describe` is `3.16.29-dirty` |
| Local install prefix | `~/.local/opt/gamescope-3.16.29` | Inspected Meson build configuration |
| Steam wrapper | `~/.local/bin/elite-gamescope-3.16.29` | Inspected local executable |

Gamescope's GitHub project uses numbered tags but has no conventional GitHub
release entries, so “latest release” should be described as “latest numbered
upstream tag.” The local annotated tag was signed and dated 2026-09-16.

## Official build requirements and commands

The 3.16.29 README gives this Debian-family dependency command
([source](https://github.com/ValveSoftware/gamescope/blob/3.16.29/README.md#L18-L38)):

```bash
sudo apt install \
  meson ninja-build pkg-config cmake libpipewire-0.3-dev hwdata \
  libx11-dev libwayland-dev libvulkan-dev wayland-protocols \
  libx11-xcb-dev libxdamage-dev libxcomposite-dev libxcursor-dev \
  libxxf86vm-dev libxtst-dev libxres-dev libxmu-dev \
  libxkbcommon-dev libcap-dev libsdl2-dev libavif-dev \
  libpixman-1-dev liblcms2-dev libseat-dev libinput-dev xwayland \
  libxcb-composite0-dev libxcb-ewmh-dev libxcb-icccm4-dev \
  libxcb-res0-dev glslang-tools libluajit-5.1-dev libcatch2-dev
```

The local build also enables `input_emulation`, which Gamescope defines as
“XTest/Input Emulation with libei.” Install Ubuntu's development packages for
that feature too:

```bash
sudo apt install libei-dev libeis-dev
```

See the 3.16.29
[`meson_options.txt`](https://github.com/ValveSoftware/gamescope/blob/3.16.29/meson_options.txt#L1-L10)
and [`src/meson.build`](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/meson.build#L13-L21).
The packages are available as version `1.5.0-3` in Ubuntu 26.04 on the checked
host.

Upstream's basic build is:

```bash
git submodule update --init
meson setup build/
ninja -C build/
build/src/gamescope -- <game>
meson install -C build/ --skip-subprojects
```

The actual successful side-by-side build used these Meson choices:

```bash
meson setup build \
  --buildtype=release \
  --prefix="$HOME/.local/opt/gamescope-3.16.29" \
  -Dsdl2_backend=enabled \
  -Dpipewire=enabled \
  -Dinput_emulation=enabled \
  -Denable_gamescope_wsi_layer=false \
  -Denable_openvr_support=false \
  -Denable_tests=false

ninja -C build
meson install -C build --skip-subprojects
```

Disabling the WSI layer here avoids installing a system Vulkan layer from this
private build; this configuration is for the nested SDL/X11 route used by the
wrapper, not a general replacement for every Gamescope feature.

## Command-line option semantics

Upstream documents the resolution options in its
[README](https://github.com/ValveSoftware/gamescope/blob/3.16.29/README.md#L51-L80)
and in the program's
[`--help` source](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/main.cpp#L167-L231):

- `-W` / `-H` set the Gamescope output/window resolution in nested mode.
  Resizing the outer window updates these values. They default to `1280x720`
  and are ignored in embedded mode.
- `-w` / `-h` set the virtual resolution exposed to the game. They default to
  the `-W` / `-H` values.
- `-r` sets the nested refresh/frame-rate target in frames per second. The
  program help calls this “game refresh rate”; the README calls it a game
  frame-rate limit. It does not change the host monitor's physical mode, which
  must be configured separately in the desktop display settings.
- `-b` creates a borderless outer window.
- `-f` creates a fullscreen outer window.
- `-s` / `--mouse-sensitivity` multiplies mouse movement by the supplied
  decimal number.
- `--force-grab-cursor` always uses relative mouse mode rather than changing
  it based on cursor visibility.

`-b`, `-f`, sensitivity, and forced grabbing are parsed into startup state in
[`main.cpp`](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/main.cpp#L730-L780)
and
[`main.cpp`](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/main.cpp#L823-L839).
In particular, upstream exposes no command-line way to turn forced relative
mouse mode off again after startup.

## `gamescopectl` and runtime convars

Stock 3.16.29 includes and installs `gamescopectl`
([build definition](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/meson.build#L220-L229)).
It connects to the display named by `GAMESCOPE_WAYLAND_DISPLAY`, falling back
to `gamescope-0`, and forwards command/convar names and values to the running
compositor
([client implementation](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/Apps/gamescopectl.cpp#L81-L135)).
Running `gamescopectl help` lists registered commands and convars
([client help](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/Apps/gamescopectl.cpp#L243-L281);
[command registry](https://github.com/ValveSoftware/gamescope/blob/3.16.29/src/convar.cpp#L32-L79)).

Upstream 3.16.29 does **not** register a `force_relative_mouse` convar. The
local checkout adds this patch to `src/steamcompmgr.cpp`:

```diff
@@
+gamescope::ConVar<bool> cv_force_relative_mouse{
+    "force_relative_mouse", false, "Force relative mouse mode at runtime."
+};
 gamescope::ConVar<int> cv_cursor_composite{ ... };
@@
-if ( g_bForceRelativeMouse )
+if ( g_bForceRelativeMouse || cv_force_relative_mouse )
     return true;
@@
-const bool bRelativeMouseMode = bImageEmpty && bHasPointerConstraint && !bExcludedAppId;
+const bool bRelativeMouseMode = cv_force_relative_mouse ||
+    ( bImageEmpty && bHasPointerConstraint && !bExcludedAppId );
```

The first use keeps the cursor composited while the runtime override is active.
The second changes the nested input hint without setting upstream's global
startup-only `g_bForceRelativeMouse`. The wrapper can therefore run:

```bash
GAMESCOPE_WAYLAND_DISPLAY="$scope_display" \
  gamescopectl force_relative_mouse true

GAMESCOPE_WAYLAND_DISPLAY="$scope_display" \
  gamescopectl force_relative_mouse false
```

The generic `gamescopectl` transport and convar registry are upstream; the
specific convar and its behavior are local additions.

## Exact working wrapper behavior

`~/.local/bin/elite-gamescope-3.16.29` currently does the following:

1. Starts the private Gamescope build with `SDL_VIDEODRIVER=x11` and
   `--backend sdl`.
2. Creates a `2560x1440` outer window and exposes `3840x2160` to Elite at
   `120` Hz:

   ```text
   -W 2560 -H 1440 -w 3840 -h 2160 -r 120
   ```

3. Applies `--mouse-sensitivity 2.25`.
4. Does not pass `-b`, `-f`, or `--force-grab-cursor` at startup. That keeps
   the launcher decorated, movable, and free from forced relative mouse mode.
5. Finds the outer Gamescope X11 window by matching its PID in `wmctrl -lp`.
6. Places the launcher at `2560x1440+5120+720` with `wmctrl`.
7. Polls the outer window's `_NET_WM_NAME` using `xprop`. When the title becomes
   `Elite - Dangerous (CLIENT)`, it makes the outer window fullscreen and sends
   `gamescopectl force_relative_mouse true`.
8. When that title is no longer selected, it first sends
   `force_relative_mouse false`, then removes fullscreen and re-centers the
   launcher.
9. Discovers the correct private Wayland socket by reading
   `GAMESCOPE_WAYLAND_DISPLAY` from a direct child process's `/proc/.../environ`.
10. Terminates the Gamescope child on `INT`, `TERM`, or `HUP`, and otherwise
    returns its exit status.

The wrapper therefore depends on `wmctrl`, `xprop`, `pgrep`, access to the
Gamescope child process environment, the exact English game-window title, and
the current desktop coordinate layout. The placement values are machine
specific. The match is based on the selected window title, not directly on
`EliteDangerous64.exe`; this avoids enabling confinement while only the
launcher surface is selected inside the same Gamescope session.

Steam's launch option points at the wrapper followed by `%command%`:

```text
/home/USERNAME/.local/bin/elite-gamescope-3.16.29 %command%
```

## Why the stock global option was not retained

The upstream option is accurately named: it **always** uses relative mouse
mode. In a Steam title that starts a launcher and later a separate game client,
that scope is too broad. It captures the launcher as well as the game.

Upstream issue reports also show that forced cursor behavior is not uniformly
reliable across Gamescope, desktop, GPU, and game combinations:

- [Issue #1851](https://github.com/ValveSoftware/gamescope/issues/1851)
  reports startup crashes and severe mouse-movement stalls with
  `--force-grab-cursor` on several 3.16.x versions in one Proton game.
- [Issue #1776](https://github.com/ValveSoftware/gamescope/issues/1776)
  reports slow-feeling motion after enabling forced grabbing.
- [Issue #1521](https://github.com/ValveSoftware/gamescope/issues/1521)
  reports a dead range when using `--mouse-sensitivity`.
- [Issue #2404](https://github.com/ValveSoftware/gamescope/issues/2404)
  reports an upstream 2026 regression where automatic cursor locking stopped;
  the reporter says `--force-grab-cursor` still locks it.
- [Issue #1307](https://github.com/ValveSoftware/gamescope/issues/1307)
  reports that Steam Input-emulated mouse movement was not captured even when
  the physical mouse was captured.

These are user reports in the upstream tracker, not claims that every system
will fail. A public guide should preserve a plain non-Gamescope launch path,
pin the exact tested tag, and tell users how to revert the Steam launch option.

## AMD DCC top-edge artifact investigation

The tested Strix Halo system later developed black stair-step/tiled artifacts
along the top edge of both the Frontier launcher and Elite when nested through
Gamescope. The corruption was present in desktop screenshots and in
Gamescope's internal screenshot output, so it was in the rendered image rather
than being a cable, panel, or physical scanout problem.

The relevant tested stack was:

| Item | Verified value |
|---|---|
| GPU | AMD Radeon 8060S/8050S, PCI ID `1002:1586` |
| RADV family | `STRIX_HALO` |
| Mesa | `26.0.8-1ubuntu0.3` |
| Gamescope | Locally built `3.16.29` |
| Desktop | GNOME Shell 50.1, Wayland, XWayland native scaling enabled |
| Gamescope path | SDL/X11, 2560x1440 output with 3840x2160 game resolution |

Scoping `RADV_DEBUG=nodcc` to Gamescope removed the corruption:

```text
RADV_DEBUG=nodcc gamescope [Gamescope options] \
  -- env -u RADV_DEBUG -- [game command]
```

The `env -u RADV_DEBUG` boundary is deliberate. It disables DCC for the
compositor while allowing Elite and the launcher to use their normal RADV
configuration. Applying `nodcc` to the whole Steam/game process tree is broader
than the verified fix and may unnecessarily affect game performance.

Verification after relaunch found no qualifying black top-edge component in
either an external desktop capture or Gamescope's internal screenshot, and no
new AMD GPU errors. The user also reported more responsive pointer movement;
that secondary observation is machine-specific and should not be presented as
a general performance claim.

This closely matches an independent September 2026 investigation of black
Gamescope output on Radeon/RADV while scaling 1080p to 4K. That report traced
the failure to an AMD DCC DMA-BUF modifier/import mismatch and verified the
same compositor-only pattern:
`RADV_DEBUG=nodcc gamescope ... -- env -u RADV_DEBUG ...`.
See the
[Gamescope/RADV DCC and DMA-BUF modifier investigation](https://www.atty303.ninja/notes/Gamescope%E3%81%AEfilter%E3%81%A7%E9%BB%92%E7%94%BB%E9%9D%A2%E3%81%AB%E3%81%AA%E3%82%8B%E5%8E%9F%E5%9B%A0%E3%82%92DMA-BUF-modifier%E3%81%BE%E3%81%A7%E8%BF%BD%E3%81%A3%E3%81%9F).
Mesa's `RADV_DEBUG` help also defines `nodcc` as disabling DCC for color
images on the supported GFX generations.

The workaround should remain opt-in and be removed for a retest after Mesa or
Gamescope updates. It was verified on the stack above, not established as a
universal fix for every black-frame or flicker symptom.

## Documentation guidance

- Present the upstream 3.16.29 build and the local patch as separate steps.
- Ship the `steamcompmgr.cpp` change as a normal patch file rather than asking
  readers to edit line numbers manually.
- Use a configurable wrapper for output dimensions, game dimensions, refresh,
  sensitivity, monitor coordinates, and the game-title match.
- Describe `3840x2160 @ 120 Hz` as the resolution/refresh exposed to Elite;
  separately require the physical monitor to be set to 120 Hz in Ubuntu.
- Warn that output size (`-W/-H`) and game size (`-w/-h`) are different layers.
- Do not recommend global `--force-grab-cursor` for the Frontier-launcher flow;
  it recreates the launcher-capture problem the runtime patch solves.
- Note that changing the ratio between the game and output resolutions can
  change perceived mouse speed and may require recalibrating
  `--mouse-sensitivity`.
