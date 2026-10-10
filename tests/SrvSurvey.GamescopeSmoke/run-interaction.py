"""Run the actual Avalonia smoke DLL against a private Gamescope compositor and libei input."""

import argparse, pathlib, subprocess, tempfile, os, signal, shutil, re, shlex

HERE = pathlib.Path(__file__).resolve().parent


def pixel(path, x, y):
    return subprocess.check_output(
        ["convert", str(path), "-format", f"%[pixel:p{{{x},{y}}}]", "info:"], text=True
    ).strip()


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument(
    "--dll", type=pathlib.Path, required=True, help="Built production smoke DLL"
)
parser.add_argument("--gamescope", default="gamescope")
parser.add_argument(
    "--protocol",
    type=pathlib.Path,
    required=True,
    help="Matching Gamescope protocol/gamescope-control.xml",
)
parser.add_argument(
    "--native-dir",
    type=pathlib.Path,
    default=HERE / "native" if (HERE / "native").is_dir() else HERE,
)
parser.add_argument("--from-game", action="store_true")
parser.add_argument("--output", type=pathlib.Path)
args = parser.parse_args()
args.dll = args.dll.resolve()
args.protocol = args.protocol.resolve()
args.native_dir = args.native_dir.resolve()
assert args.dll.is_file(), args.dll
gamescope = shutil.which(args.gamescope)
assert gamescope, f"Gamescope unavailable: {args.gamescope}"
output = (
    args.output
    or pathlib.Path(tempfile.mkdtemp(prefix="srvsurvey-production-interaction-"))
).resolve()
output.mkdir(parents=True, exist_ok=True)
native_flags = shlex.split(
    subprocess.check_output(
        ["pkg-config", "--cflags", "--libs", "libei-1.0", "x11", "xrender", "xext"],
        text=True,
    )
)
subprocess.run(
    [
        "cc",
        "-Wall",
        "-Wextra",
        "-O2",
        str(args.native_dir / "production-avalonia-driver.c"),
        "-o",
        str(output / "driver"),
        *native_flags,
    ],
    check=True,
)
subprocess.run(
    [
        "wayland-scanner",
        "client-header",
        str(args.protocol),
        str(output / "gamescope-control-client.h"),
    ],
    check=True,
)
subprocess.run(
    [
        "wayland-scanner",
        "private-code",
        str(args.protocol),
        str(output / "gamescope-control-client.c"),
    ],
    check=True,
)
subprocess.run(
    [
        "cc",
        "-Wall",
        "-Wextra",
        "-O2",
        "-I",
        str(output),
        str(args.native_dir / "screenshot-client.c"),
        str(output / "gamescope-control-client.c"),
        "-o",
        str(output / "screenshot-client"),
        "-lwayland-client",
    ],
    check=True,
)
for name in ["steam", "EliteDangerous64.exe"]:
    shutil.copy2(output / "driver", output / name)
env = os.environ.copy()
env["SCREENSHOT_CLIENT"] = str(output / "screenshot-client")
ctl = pathlib.Path(gamescope).with_name("gamescopectl")
if ctl.is_file():
    env["GAMESCOPECTL"] = str(ctl)
for key in [
    "DISPLAY",
    "WAYLAND_DISPLAY",
    "GAMESCOPE_WAYLAND_DISPLAY",
    "GAMESCOPE_DISPLAY",
    "LD_PRELOAD",
]:
    env.pop(key, None)
with tempfile.TemporaryDirectory(prefix="srvsurvey-production-runtime-") as runtime:
    env["XDG_RUNTIME_DIR"] = runtime
    command = [
        gamescope,
        "--backend",
        "headless",
        "-W",
        "1280",
        "-H",
        "800",
        "-w",
        "960",
        "-h",
        "600",
        "-e",
        "--xwayland-count",
        "2",
        "--",
        str(output / "driver"),
        str(args.dll),
        str(output),
        "1" if args.from_game else "0",
    ]
    p = subprocess.Popen(
        command,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        start_new_session=True,
    )
    try:
        stdout, stderr = p.communicate(timeout=50)
    except subprocess.TimeoutExpired:
        os.killpg(p.pid, signal.SIGTERM)
        try:
            stdout, stderr = p.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(p.pid, signal.SIGKILL)
            stdout, stderr = p.communicate()
    (output / "native.log").write_text(stdout)
    (output / "gamescope.log").write_text(stderr)
    print("Gamescope exit:", p.returncode, "Artifacts:", output)
    print(stdout)
    assert p.returncode == 0, (p.returncode, stderr[-3000:])
av = (output / "avalonia.log").read_text()
print("AVALONIA LOG\n" + av)
header = re.search(r"HOST PRIMARY=([^ ]+) GAME=([^ ]+) FOREIGN=0x([0-9a-f]+)", stdout)
assert header, "missing display inventory"
primary, game_display, foreign = header.groups()
assert f"Gamescope overlay display: {primary} (server 0)" in av, av
assert f"inherited display: {game_display if args.from_game else primary}." in av, av
phases = {
    name: body
    for name, body in re.findall(
        r"\nPHASE ([^\n]+)\n(.*?)(?=\nPHASE |\Z)", stdout, re.S
    )
}


def event(phase, label, kind, xid=None, point=None):
    body = phases[phase]
    expression = rf"EVENT {label} {kind}=[0-9]+ type=" + (
        "4" if kind == "button" else "2"
    )
    expression += rf" window=0x{xid}" if xid else r" window=0x[0-9a-f]+"
    if point is not None:
        expression += rf" x={point} y={point}"
    assert re.search(expression, body), f"expected {phase} {label} {kind}:\n{body}"


for phase in ["passive", "after-off", "source-shown", "disposed"]:
    event(phase, "game", "button", point=24)
    event(phase, "game", "button", point=200)
    event(phase, "game", "keycode")
for phase in ["live", "reacquired"]:
    assert re.search(
        r"AV_NATIVE window=0x[0-9a-f]+ external=0 overlay=1 focus=2 mapped=2 size=1280x800 shapeRectangles=0",
        phases[phase],
    ), phases[phase]
    assert re.search(
        r"AV_NATIVE window=0x[0-9a-f]+ external=1 overlay=0 focus=0 mapped=2 size=1280x800 shapeRectangles=1 shape=0,0,1280,800",
        phases[phase],
    ), phases[phase]
    event(phase, "game", "keycode")
    assert not re.search(
        r"EVENT (game|primary) button=[0-9]+ type=4", phases[phase]
    ), phases[phase]
event("steam-yield", "primary", "button", foreign, 24)
event("steam-yield", "primary", "button", foreign, 200)
event("steam-yield", "primary", "keycode", foreign)
for phase, body in phases.items():
    assert "GAMESCOPE_FOCUSED_APP_GFX=00057b98" in body, (phase, body)
assert re.findall(r"HUD POINTER count=(\d+)", av) == ["1", "2"], av
assert pixel(output / "before-app.png", 24, 24) in ["srgb(255,0,0)", "srgba(255,0,0,1)"]
for name in [
    "passive",
    "live",
    "steam-yield",
    "reacquired",
    "after-off",
    "source-shown",
]:
    assert pixel(output / f"{name}.png", 24, 24) in [
        "srgb(0,255,0)",
        "srgba(0,255,0,1)",
    ], (name, pixel(output / f"{name}.png", 24, 24))
    assert pixel(output / f"{name}.png", 1000, 700) in [
        "srgb(255,0,0)",
        "srgba(255,0,0,1)",
    ], (name, pixel(output / f"{name}.png", 1000, 700))
assert pixel(output / "source-hidden.png", 24, 24) in [
    "srgb(255,0,0)",
    "srgba(255,0,0,1)",
]
assert pixel(output / "disposed.png", 24, 24) in ["srgb(255,0,0)", "srgba(255,0,0,1)"]
print(
    "PASS: actual production Avalonia HUD receives exactly2 control clicks, blank clicks are swallowed duringlive, keyboard staysgame, Steam request yields/reacquires, release/hide/show restore passive input and screenshots preserve geometry/transparency."
)
