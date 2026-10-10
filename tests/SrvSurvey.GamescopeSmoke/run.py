"""Exercise the real Avalonia controller on an isolated, two-server Gamescope compositor."""
import argparse
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
PROBE = HERE / "bin/Debug/net10.0/SrvSurvey.GamescopeSmoke.dll"


def run(*arguments, **options):
    return subprocess.run(arguments, check=True, **options)


def child(output, from_game):
    environment = os.environ.copy()
    primary = environment["DISPLAY"]
    game_environment = environment | {"DISPLAY": environment["STEAM_GAME_DISPLAY_0"], "PRIMARY_DISPLAY": primary}
    processes = []
    try:
        if from_game:
            processes.append(subprocess.Popen([str(output / "steam"), "--steam"], env=environment))
        with (output / "game.log").open("w") as log:
            processes.append(subprocess.Popen([str(output / "EliteDangerous64.exe")], env=game_environment, stdout=log))
        time.sleep(1)
        run(str(output / "screenshot-client"), str(output / "before-overlay.png"), timeout=15)
        with (output / "avalonia.log").open("w") as log:
            probe = subprocess.Popen(["dotnet", str(PROBE)], env=game_environment if from_game else environment,
                                     stdout=log, stderr=subprocess.STDOUT)
            processes.append(probe)
        time.sleep(5)
        run(str(output / "screenshot-client"), str(output / "avalonia-overlay.png"), timeout=15)
        if probe.wait(timeout=20) != 0:
            raise RuntimeError((output / "avalonia.log").read_text())
    finally:
        for process in processes:
            if process.poll() is None:
                process.terminate()
        for process in processes:
            process.wait(timeout=5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gamescope", default="gamescope")
    parser.add_argument("--protocol", type=Path, help="Gamescope checkout's protocol/gamescope-control.xml")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--from-game", action="store_true", help="Start SrvSurvey on the game server; discover primary through same-user Steam")
    parser.add_argument("--child", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.child:
        child(args.output, args.from_game)
        return
    if args.protocol is None:
        parser.error("--protocol is required")
    output = (args.output or Path(tempfile.mkdtemp(prefix="srvsurvey-gamescope-smoke-"))).resolve()
    output.mkdir(parents=True, exist_ok=True)
    run("dotnet", "build", str(HERE / "SrvSurvey.GamescopeSmoke.csproj"), "-v:minimal")
    run("wayland-scanner", "client-header", str(args.protocol), str(output / "gamescope-control-client.h"))
    run("wayland-scanner", "private-code", str(args.protocol), str(output / "gamescope-control-client.c"))
    run("cc", "-Wall", "-Wextra", "-O2", "-I", str(output), str(HERE / "native/screenshot-client.c"),
        str(output / "gamescope-control-client.c"), "-o", str(output / "screenshot-client"), "-lwayland-client")
    run("cc", "-Wall", "-Wextra", "-O2", str(HERE / "native/game.c"), "-o", str(output / "EliteDangerous64.exe"), "-lX11")
    shutil.copy2(output / "EliteDangerous64.exe", output / "steam")
    environment = os.environ.copy()
    for key in ("DISPLAY", "WAYLAND_DISPLAY", "GAMESCOPE_WAYLAND_DISPLAY", "GAMESCOPE_DISPLAY", "LD_PRELOAD"):
        environment.pop(key, None)
    with tempfile.TemporaryDirectory(prefix="srvsurvey-gamescope-runtime-") as runtime:
        environment["XDG_RUNTIME_DIR"] = runtime
        arguments = [args.gamescope, "--backend", "headless", "-W", "1280", "-H", "800", "-w", "960", "-h", "600",
                     "-e", "--xwayland-count", "2", "--", sys.executable, str(Path(__file__).resolve()), "--child", "--output", str(output)]
        if args.from_game:
            arguments.append("--from-game")
        with (output / "gamescope.log").open("w") as log:
            process = subprocess.Popen(arguments, env=environment, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                if process.wait(timeout=55) != 0:
                    raise RuntimeError(f"Gamescope failed; inspect {output}")
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGTERM)
                    process.wait(timeout=5)
    screenshot = output / "avalonia-overlay.png"
    dimensions = subprocess.check_output(["identify", "-format", "%wx%h", str(screenshot)], text=True)
    marker = subprocess.check_output(["convert", str(screenshot), "-format", "%[pixel:p{24,24}]", "info:"], text=True)
    background = subprocess.check_output(["convert", str(screenshot), "-format", "%[pixel:p{200,200}]", "info:"], text=True)
    before = subprocess.check_output(["convert", str(output / "before-overlay.png"), "-format", "%[pixel:p{24,24}]", "info:"], text=True)
    assert dimensions == "1280x800", dimensions
    assert marker in ("srgb(0,255,0)", "srgba(0,255,0,1)"), marker
    assert background in ("srgb(255,0,0)", "srgba(255,0,0,1)"), background
    assert before in ("srgb(255,0,0)", "srgba(255,0,0,1)"), before
    print(f"PASS: real combined Avalonia HUD, transparent background, game focus, output-sized canvas. Artifacts: {output}")


if __name__ == "__main__":
    main()
