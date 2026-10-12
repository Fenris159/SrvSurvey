"""Compare the production tracker with real compositor pixels for startup and live scaling changes."""
import argparse
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import sys
import tempfile
import time

HERE = Path(__file__).resolve().parent


def line(process, timeout=5):
    if not select.select([process.stdout], [], [], timeout)[0]:
        raise RuntimeError("Timed out waiting for the isolated probe")
    result = process.stdout.readline().decode()
    if not result:
        raise RuntimeError(f"Probe exited: {process.poll()}")
    return result.strip()


def child(args):
    primary = os.environ["DISPLAY"]
    game_env = os.environ | {"DISPLAY": os.environ["STEAM_GAME_DISPLAY_0"], "PRIMARY_DISPLAY": primary}
    game = subprocess.Popen([str(args.output / "EliteDangerous64.exe")], env=game_env,
                            stdin=subprocess.PIPE, stdout=subprocess.PIPE, bufsize=0)
    probe = None
    records = []
    try:
        assert line(game) == "GAME_READY"
        probe = subprocess.Popen(["dotnet", str(args.dll), "--gamescope-tracking"],
                                 stdin=subprocess.PIPE, stdout=subprocess.PIPE, bufsize=0)
        while line(probe) != "GAMESCOPE_READY external=true capture=false":
            pass

        def verify(label, expected):
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                probe.stdin.write(b"snapshot\n")
                probe.stdin.flush()
                snapshot = json.loads(line(probe))
                rectangle = tuple(snapshot[field] for field in ("X", "Y", "Width", "Height"))
                if snapshot["Available"] and rectangle == expected:
                    break
                time.sleep(0.05)
            else:
                raise AssertionError((label, expected, snapshot))
            image = args.output / f"{label}.png"
            subprocess.run([str(args.output / "screenshot-client"), str(image)], check=True, timeout=8)
            rectangle = subprocess.check_output(["convert", str(image), "-fill", "white", "-opaque", "red",
                                                "-fill", "black", "+opaque", "white", "-bordercolor", "black", "-border", "1", "-trim",
                                                "-format", "%wx%h%O", "info:"], text=True)
            width, height, x, y = map(int, rectangle.replace("x", "+").split("+"))
            measured = (x - 1, y - 1, width, height)
            assert measured == expected, (label, measured, snapshot)
            records.append({"Phase": label, "Snapshot": snapshot, "Pixels": measured})
            print(label, measured, flush=True)

        integer = (160, 100, 960, 600)
        auto = (0, 0, 1280, 800)
        verify("startup-cli-integer", integer)
        for label, command, expected in [
            ("primary-new-auto", "primary new 0", auto),
            ("game-legacy-integer", "game legacy 2", integer),
            ("primary-same-new-auto", "primary new 0", auto),
            ("primary-legacy-auto", "primary legacy 0", auto),
            ("game-new-integer", "game new 1", integer),
            ("primary-same-legacy-auto", "primary legacy 0", auto),
        ]:
            game.stdin.write((command + "\n").encode())
            game.stdin.flush()
            assert line(game) == "ACK"
            time.sleep(0.12)
            verify(label, expected)
        for label, commands in [
            ("batched-game-then-primary", ["game new 1", "primary new 0"]),
            ("batched-primary-then-game", ["primary new 1", "game legacy 0"]),
        ]:
            for command in commands:
                game.stdin.write((command + "\n").encode())
                game.stdin.flush()
                assert line(game) == "ACK"
                time.sleep(0.08)
            verify(label, auto)
        (args.output / "snapshots.json").write_text(json.dumps(records, indent=2))
    finally:
        for process in (probe, game):
            if process and process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gamescope", default="gamescope")
    parser.add_argument("--protocol", type=Path, required=True)
    parser.add_argument("--dll", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--child", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    args.dll = args.dll.resolve()
    args.output = args.output.resolve()
    if args.child:
        child(args)
        return
    args.output.mkdir(parents=True, exist_ok=False)
    native = HERE / "native"
    subprocess.run(["cc", "-Wall", "-Wextra", "-O2", str(native / "scaling-game.c"),
                    "-o", str(args.output / "EliteDangerous64.exe"), "-lX11"], check=True)
    subprocess.run(["wayland-scanner", "client-header", str(args.protocol), str(args.output / "gamescope-control-client.h")], check=True)
    subprocess.run(["wayland-scanner", "private-code", str(args.protocol), str(args.output / "gamescope-control-client.c")], check=True)
    subprocess.run(["cc", "-Wall", "-Wextra", "-O2", "-I", str(args.output), str(native / "screenshot-client.c"),
                    str(args.output / "gamescope-control-client.c"), "-o", str(args.output / "screenshot-client"), "-lwayland-client"], check=True)
    env = os.environ.copy()
    for key in ("DISPLAY", "WAYLAND_DISPLAY", "GAMESCOPE_WAYLAND_DISPLAY", "GAMESCOPE_DISPLAY", "LD_PRELOAD"):
        env.pop(key, None)
    with tempfile.TemporaryDirectory(prefix="srvsurvey-scaling-runtime-") as runtime:
        env["XDG_RUNTIME_DIR"] = runtime
        command = [shutil.which(args.gamescope) or args.gamescope, "--backend", "headless", "-e", "-S", "integer",
                   "-W", "1280", "-H", "800", "-w", "960", "-h", "600", "--xwayland-count", "2", "--",
                   sys.executable, str(Path(__file__).resolve()), "--child", "--dll", str(args.dll),
                   "--protocol", str(args.protocol), "--output", str(args.output)]
        with (args.output / "gamescope.log").open("w") as log:
            process = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                assert process.wait(timeout=45) == 0, args.output
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
    # Gamescope itself may exit zero even when its child fails. Require all
    # child assertions and freshly written evidence, not just compositor exit.
    records = json.loads((args.output / "snapshots.json").read_text())
    assert len(records) == 9, records
    print("PASS: verified startup scaler and live new/legacy commands on both servers match real compositor pixels.")


if __name__ == "__main__":
    main()
