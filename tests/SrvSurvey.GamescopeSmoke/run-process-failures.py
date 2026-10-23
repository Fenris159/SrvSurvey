"""Inject failed forks into the real driver on private Xvfb; intercept every kill and waitpid."""

import os
from pathlib import Path
import select
import shlex
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent


def read_display(server):
    if not select.select([server.stdout], [], [], 5)[0]:
        raise RuntimeError("Private Xvfb did not acknowledge startup")
    number = server.stdout.readline().strip()
    if not number.isascii() or not number.isdecimal() or server.poll() is not None:
        raise RuntimeError("Private Xvfb exited or returned an invalid display number")
    return ":" + number


def verify_failure(result, ordinal, stopped):
    if result.returncode != 2 or f"INJECTED_FORK {ordinal}" not in result.stdout.splitlines():
        raise RuntimeError(f"Fork {ordinal} injection was not reached: {result}")
    if result.stdout.count("SAFE_STOP ") != stopped:
        raise RuntimeError(f"Fork {ordinal} cleaned up unexpected children: {result}")


def main():
    flags = shlex.split(subprocess.check_output(
        ["pkg-config", "--cflags", "--libs", "libei-1.0", "x11", "xrender", "xext"], text=True
    ))
    with tempfile.TemporaryDirectory(prefix="srvsurvey-process-failures-") as directory:
        root = Path(directory)
        driver = root / "driver"
        subprocess.run([
            "cc", "-Wall", "-Wextra", "-Werror", "-O2",
            str(HERE / "native/production-avalonia-driver.c"), str(HERE / "native/process-failure-fixture.c"),
            "-Wl,--wrap=fork,--wrap=kill,--wrap=waitpid", "-o", str(driver), *flags,
        ], check=True)
        server = subprocess.Popen(["Xvfb", "-displayfd", "1", "-screen", "0", "1280x800x24", "-nolisten", "tcp"],
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            display = read_display(server)
            for ordinal, stopped in [(1, 0), (2, 1), (4, 2)]:
                output = root / str(ordinal)
                output.mkdir()
                environment = os.environ | {
                    "DISPLAY": display, "STEAM_GAME_DISPLAY_0": display,
                    "SRVSURVEY_TEST_FAIL_FORK": str(ordinal), "SRVSURVEY_TEST_OUTPUT": str(output),
                }
                result = subprocess.run([str(driver), "unused.dll", str(output), "0"], env=environment,
                                        capture_output=True, text=True, timeout=12)
                verify_failure(result, ordinal, stopped)
                print(f"PASS: fork {ordinal} fails promptly; only {stopped} validated child PIDs are cleaned up")
        finally:
            if server.poll() is None:
                server.terminate()
                server.wait(timeout=5)


if __name__ == "__main__":
    main()
