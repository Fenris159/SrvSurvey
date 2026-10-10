# Gamescope composition smoke test

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
