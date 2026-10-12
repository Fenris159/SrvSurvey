"""Compile the repository's optional publisher patch and exercise failed writes privately."""
import os
from pathlib import Path
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
patch = HERE.parent.parent / "docs/patches/gamescope-3.16.29-native-wayland-focus.patch"
section = patch.read_text().split("+++ b/src/Backends/HostFocusPublisher.h\n", 1)[1]
header = "\n".join(line[1:] for line in section.splitlines() if line.startswith("+")) + "\n"
with tempfile.TemporaryDirectory(prefix="srvsurvey-publisher-test-") as directory:
    directory = Path(directory)
    (directory / "HostFocusPublisher.h").write_text(header)
    binary = directory / "publisher-test"
    subprocess.run(["c++", "-std=c++17", "-Wall", "-Wextra", "-Werror", "-I", str(directory),
                    str(HERE / "native/host-focus-publisher-test.cpp"), "-o", str(binary)], check=True)
    env = os.environ.copy()
    env.pop("GAMESCOPE_HOST_FOCUS_FILE", None)
    env.pop("ELITE_HOST_FOCUS_FILE", None)
    env["XDG_RUNTIME_DIR"] = str(directory)
    for lifetime in ("startup", "existing"):
        for focused in ("0", "1"):
            subprocess.run([str(binary), lifetime, focused], env=env, check=True, timeout=5)
