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

The simulated Steam process, foreign input request, and synthetic Elite window
exercise the production routing contract without mini-ed-launcher. They do not
validate real Steam client policies, Proton/Elite, physical Deck devices, or
SteamOS version differences. The test never injects events into the user's
desktop; each run owns a private compositor and runtime directory.
