"""Inject failed forks into the real driver on private Xvfb; intercept every kill and waitpid."""

import os
from pathlib import Path
import select
import shlex
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
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
        if not select.select([server.stdout], [], [], 5)[0]:
            raise RuntimeError("Private Xvfb did not start")
        display = ":" + server.stdout.readline().strip()
        for ordinal, stopped in [(1, 0), (2, 1), (4, 2)]:
            output = root / str(ordinal)
            output.mkdir()
            environment = os.environ | {
                "DISPLAY": display, "STEAM_GAME_DISPLAY_0": display,
                "SRVSURVEY_TEST_FAIL_FORK": str(ordinal), "SRVSURVEY_TEST_OUTPUT": str(output),
            }
            result = subprocess.run([str(driver), "unused.dll", str(output), "0"], env=environment,
                                    capture_output=True, text=True, timeout=12)
            assert result.returncode == 2, (ordinal, result.returncode, result.stdout, result.stderr)
            assert result.stdout.count("SAFE_STOP ") == stopped, (ordinal, result.stdout)
            print(f"PASS: fork {ordinal} fails promptly; only {stopped} validated child PIDs are cleaned up")
    finally:
        if server.poll() is None:
            server.terminate()
            server.wait(timeout=5)
