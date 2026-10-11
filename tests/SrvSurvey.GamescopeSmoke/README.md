# Gamescope composition and pointer smoke tests

This manual Linux test runs the production `CombinedOverlayPresentationController`,
X11 overlay service, session resolver, and game tracker against a real headless
Gamescope compositor. A synthetic red Elite window runs on server 1. The real
Avalonia controller must place a 64×64 green panel on the transparent server-0
canvas, without replacing the game or changing its input focus.

Requirements: .NET 10, Gamescope with the headless backend and control protocol
v3, its matching source checkout, XWayland, a C compiler, X11 and Wayland client
development libraries, `wayland-scanner`, and ImageMagick. The test uses a private
runtime directory and compositor. It does not alter the current desktop session.

From the repository root:

```sh
python3 tests/SrvSurvey.GamescopeSmoke/run.py \
  --gamescope /path/to/gamescope \
  --protocol /path/to/gamescope-source/protocol/gamescope-control.xml \
  --output /tmp/srvsurvey-gamescope-primary

# Start SrvSurvey on the game server, then verify relocation through a Steam process.
python3 tests/SrvSurvey.GamescopeSmoke/run.py \
  --gamescope /path/to/gamescope \
  --protocol /path/to/gamescope-source/protocol/gamescope-control.xml \
  --from-game --output /tmp/srvsurvey-gamescope-routing
```

The second run uses a synthetic same-user Steam process with a primary-server
environment; it does not launch a real Steam client. Neither run uses mini-ed-launcher.
Success requires a 1280×800 compositor screenshot, a green marker at (24,24),
a red game pixel at (200,200), and a visible, foreground game in the production
tracker. Logs and the screenshot are retained in the output directory.

The screenshot client requests **all real layers** directly through the Wayland
protocol. Some `gamescopectl` versions silently discard the screenshot-type
argument and capture the base game only, which would give a false failure.

This verifies native rendering, alpha, property registration, display routing,
and focus against Gamescope. It does not simulate Steam client focus policies,
Proton, Deck touch/controller input, the performance HUD, or physical output changes.

## Live pointer interaction

The interaction harness runs the same production Avalonia DLL and injects actual
compositor seat input through Gamescope's private libei socket. It adds a
transparent, full-opacity simulated Steam overlay with a nonempty input region,
so a root focus-display property alone cannot falsely prove HUD delivery.
Additional requirements: `pkg-config` and libei, XRender, and XShape development
libraries; Gamescope must expose input emulation through `LIBEI_SOCKET`.

```sh
dotnet build tests/SrvSurvey.GamescopeSmoke/SrvSurvey.GamescopeSmoke.csproj
python3 tests/SrvSurvey.GamescopeSmoke/run-interaction.py \
  --dll tests/SrvSurvey.GamescopeSmoke/bin/Debug/net10.0/SrvSurvey.GamescopeSmoke.dll \
  --gamescope /path/to/gamescope \
  --protocol /path/to/gamescope-source/protocol/gamescope-control.xml \
  --output /tmp/srvsurvey-gamescope-interaction

# Repeat with --from-game to verify relocation from the inherited game display.
```

The harness asserts exactly two actual Avalonia HUD pointer presses: initial
live mode and reacquisition after a foreign Steam input request clears. It also
asserts that F8 reaches the game during live mode, blank clicks are swallowed by
the canvas, a Steam input request receives both pointer positions and keyboard,
and toggling off restores passive game input. Native inventory verifies the
lower router's empty input shape after Avalonia `Show`, and the HUD's full
canvas shape while live versus empty shape while yielded or passive. Screenshots
check composition, presenter hiding/showing, and final disposal.
Live mode also starts the production visible-cursor session, matching the real
shortcut sequence. Its cursor setup and disposal must preserve Gamescope's
keyboard handoff when the simulated Steam menu requests input.

The simulated Steam process, foreign input request, and synthetic Elite window
exercise the production routing contract without mini-ed-launcher. They do not
validate real Steam client policies, Proton/Elite, physical Deck devices, or
SteamOS version differences. The test never injects events into the user's
desktop; each run owns a private compositor and runtime directory.

## Startup and live scaling

This probe compares the production external tracker with actual compositor
pixels. It starts Gamescope with `-S integer`, a 960×600 game, and a 1280×800
output. The expected game viewport is (160,100) with size 960×600; reading only
the new-scaler command property would incorrectly report the full output.

Use the requirements for the composition test and an already-built smoke DLL.
The output directory must be new, so stale evidence cannot produce a pass.

```sh
python3 tests/SrvSurvey.GamescopeSmoke/run-scaling.py \
  --dll tests/SrvSurvey.GamescopeSmoke/bin/Release/net10.0/SrvSurvey.GamescopeSmoke.dll \
  --gamescope /path/to/gamescope \
  --protocol /path/to/gamescope-source/protocol/gamescope-control.xml \
  --output /tmp/srvsurvey-gamescope-scaling
```

Nine phases cover startup, new and legacy scaling commands on the primary and
game displays, repeated unchanged command values, and batched requests from
both servers in both orders. Each phase requires matching tracker bounds and
the red game rectangle measured in a compositor screenshot. The runner retains
`snapshots.json`, screenshots, and the compositor log. This proves the inspected
Gamescope version's scaling contract; actual Deck/Steam settings and custom
maximum or per-window scaling still require separate validation.

## Desktop SrvSurvey with nested Gamescope

This tracking probe keeps SrvSurvey on a private Xvfb desktop while a synthetic
Elite client runs inside two successive real headless Gamescope sessions. The
same production `GameWindowTracker.CreateCurrent` lease must survive both.
Synthetic outer windows use the compositor's actual `GAMESCOPE_PID`, including
its `gamescope-wl` process name and intervening launch/reaper processes.

Additional requirements: Xvfb, `xprop`, `pkg-config`, a C compiler, and X11
development libraries. This probe only needs Gamescope's headless backend;
the screenshot protocol and libei are not used.

```sh
dotnet build tests/SrvSurvey.GamescopeSmoke/SrvSurvey.GamescopeSmoke.csproj
python3 tests/SrvSurvey.GamescopeSmoke/run-desktop.py \
  --dll tests/SrvSurvey.GamescopeSmoke/bin/Debug/net10.0/SrvSurvey.GamescopeSmoke.dll \
  --gamescope /path/to/gamescope \
  --output /tmp/srvsurvey-desktop-gamescope
```

Assertions cover foreground/background switching, movement, resizing, unmap
and WM-hidden state, duplicate outer-window ambiguity, game/compositor exit,
and reconnection after restart. Native Wayland companion fixtures cover host
foreground/background/restoration, stale process identity, malformed data, and
publisher loss while nested Elite remains focused. Existing manual markers
without a publisher retain their unknown desktop-focus semantics. Startup also verifies the ordinary desktop
overlay path and capture-backend availability. The runner retains JSON
snapshots, process ancestry, native properties, and compositor/error logs.

The outer windows are synthetic geometry and focus fixtures. This evidence
covers production tracking; real SDL/Wayland presentation, HUD pixel placement,
letterboxing/custom scaling, and keyboard delivery still need desktop testing.

## Native Wayland host focus smoke

This probe privately launches GNOME Shell headless, a real native Wayland
Gamescope, a synthetic Elite X11 surface inside Gamescope, and a native zenity
competitor. It verifies the actual native keyboard-enter/leave publisher; with
`--dll`, it also queries the production desktop tracker while SrvSurvey runs on
an isolated outer Xvfb display. The old three-line geometry marker is a fixture:
this proves focus propagation, not automatic native Wayland global geometry.

Requires GNOME Shell with `--headless --automation-script`, zenity,
dbus-run-session, Xvfb, cc, pkg-config, X11 development files, and a functioning
Gamescope Vulkan backend. Build the smoke DLL separately. The probe performs no
.NET build and changes no installed Gamescope, launcher, source, or live window.
It uses private D-Bus, Wayland, XDG runtime/config/cache/data directories and an
in-process headless virtual keyboard. Runtime files are removed after the run.
Use a fresh artifact directory for every run.

```console
python3 tests/SrvSurvey.GamescopeSmoke/run-native-wayland-focus.py \
  --inner /path/to/gamescope-with-native-focus-patch \
  --expect-sidecar \
  --dll tests/SrvSurvey.GamescopeSmoke/bin/Release/net10.0/SrvSurvey.GamescopeSmoke.dll \
  --output /tmp/srvsurvey-native-focus-proof
```

For a compositor with the older `ELITE_HOST_FOCUS_FILE` byte publisher, omit
`--expect-sidecar`; the private runner creates and declares its initial false
file. No installed launcher changes are necessary.

Expected phases: enter -> available/visible/foreground; competitor activation
-> available/visible/background; return -> foreground; compositor exit ->
unavailable. The `--expect-sidecar` run omits both focus-file environment variables, proving automatic publication. Sidecar checks also require initial false, validated
PID/start identity on each phase, and deletion on normal process shutdown.
Logs and `tracker-snapshots.json` are retained in the specified artifact path.
