#!/usr/bin/env python3
"""Collect a read-only Gamescope snapshot without journals or full process environments."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess


SESSION_KEYS = (
    "DISPLAY", "WAYLAND_DISPLAY", "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP",
    "GAMESCOPE_DISPLAY", "GAMESCOPE_WAYLAND_DISPLAY", "STEAM_GAME_DISPLAY_0",
    "SteamAppId", "SteamGameId", "SRVSURVEY_OVERLAY_HOST",
)
PROCESS_NAMES = {
    "steam", "gamescope", "gamescope-wl", "EliteDangerous64.exe", "EliteDangerous64",
    "SrvSurvey.Desktop", "SrvSurvey.Desktop.dll",
}
ROOT_PROPERTIES = (
    "GAMESCOPE_XWAYLAND_SERVER_ID", "GAMESCOPE_PID", "_NET_SUPPORTING_WM_CHECK",
    "GAMESCOPE_FOCUS_DISPLAY", "GAMESCOPE_KEYBOARD_FOCUS_DISPLAY",
    "GAMESCOPE_FOCUSED_WINDOW", "GAMESCOPE_FOCUSED_APP", "GAMESCOPE_FOCUSED_APP_GFX",
    "GAMESCOPE_NEW_SCALING_SCALER", "GAMESCOPE_SCALING_FILTER",
)
LOCAL_DISPLAY = re.compile(r":[0-9]+(?:\.[0-9]+)?\Z")


def read_text(path):
    try:
        return path.read_text(errors="replace")
    except OSError:
        return ""


def selected_environment(text):
    entries = dict(entry.split("=", 1) for entry in text.split("\0") if "=" in entry)
    return {key: entries[key] for key in SESSION_KEYS if key in entries}


def process_owner(directory):
    for line in read_text(directory / "status").splitlines():
        if line.startswith("Uid:"):
            values = line.split()
            return values[1] if len(values) > 1 else None
    return None


def read_processes(proc_directory):
    owner = process_owner(proc_directory / "self")
    if owner is None:
        return []
    result = []
    try:
        directories = sorted(proc_directory.iterdir(), key=lambda path: path.name)
    except OSError:
        return result
    for directory in directories:
        if not directory.name.isdecimal() or process_owner(directory) != owner:
            continue
        arguments = read_text(directory / "cmdline").split("\0")
        # Wine can publish a Windows path and dotnet puts the DLL after its executable.
        # Inspect basenames to recognize either form; never serialize full paths or argv.
        names = (Path(argument.replace("\\", "/")).name for argument in arguments)
        name = next((name for name in names if name in PROCESS_NAMES), "")
        comm = read_text(directory / "comm").strip()
        if name not in PROCESS_NAMES and comm not in {"gamescope", "gamescope-wl"}:
            continue
        result.append({
            "pid": int(directory.name),
            "name": name if name in PROCESS_NAMES else comm,
            "environment": selected_environment(read_text(directory / "environ")),
        })
    return result


def run_command(arguments):
    try:
        completed = subprocess.run(arguments, capture_output=True, text=True, timeout=5, check=False)
        return {
            "exitCode": completed.returncode,
            "stdout": completed.stdout[:32768],
            "stderr": completed.stderr[:4096],
        }
    except FileNotFoundError:
        return {"error": "Command is not installed: " + arguments[0]}
    except subprocess.TimeoutExpired:
        return {"error": "Command timed out after five seconds."}
    except OSError as error:
        return {"error": str(error)}


def collect(proc_directory=Path("/proc"), os_release=Path("/etc/os-release"), environment=None,
            displays=(), command=run_command):
    environment = os.environ if environment is None else environment
    session = {key: environment[key] for key in SESSION_KEYS if key in environment}
    processes = read_processes(proc_directory)
    candidates = set(displays)
    for values in [session, *(process["environment"] for process in processes)]:
        candidates.update(values.get(key, "") for key in ("DISPLAY", "GAMESCOPE_DISPLAY", "STEAM_GAME_DISPLAY_0"))
    # :7 and :7.0 refer to the same screen. Never probe remote displays or guess :0.
    candidates = sorted({name[:-2] if name.endswith(".0") else name
                         for name in candidates if LOCAL_DISPLAY.fullmatch(name)})
    release = {}
    for line in read_text(os_release).splitlines():
        key, separator, value = line.partition("=")
        if separator and key in {"ID", "VERSION_ID", "BUILD_ID", "VARIANT_ID"}:
            release[key] = value.strip('"')
    return {
        "schemaVersion": 1,
        "recordedAtUtc": datetime.now(timezone.utc).isoformat(),
        "system": {"osRelease": release, "kernel": os.uname().release, "architecture": os.uname().machine},
        "session": session,
        "processes": processes,
        "gamescopeVersion": command(["gamescope", "--version"]),
        "displays": [{
            "display": name,
            "properties": command(["xprop", "-display", name, "-root", "-notype", *ROOT_PROPERTIES]),
            "geometry": command(["xwininfo", "-display", name, "-root", "-stats"]),
        } for name in candidates[:8]],
        "omittedDisplayCount": max(0, len(candidates) - 8),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Create a new JSON file; existing files are never overwritten.")
    parser.add_argument("--display", action="append", default=[], help="Additional local display, for example :7.")
    arguments = parser.parse_args()
    if any(not LOCAL_DISPLAY.fullmatch(name) for name in arguments.display):
        parser.error("--display requires a local display such as :7 or :7.0")
    if arguments.output is not None and arguments.output.exists():
        parser.error("The output file already exists. Choose a new name for this snapshot.")
    report = json.dumps(collect(displays=arguments.display), indent=2) + "\n"
    if arguments.output is None:
        print(report, end="")
    else:
        try:
            # The snapshot contains local display and process IDs; keep it private until shared.
            descriptor = os.open(arguments.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w") as output:
                output.write(report)
        except OSError as error:
            parser.error(str(error))


if __name__ == "__main__":
    main()
