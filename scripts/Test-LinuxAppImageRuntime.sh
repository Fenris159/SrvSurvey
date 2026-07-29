#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 APPIMAGE_PATH" >&2
    exit 2
fi

appimage=$(realpath "$1")
if [[ ! -x "$appimage" ]]; then
    echo "The AppImage is missing or is not executable: '$appimage'." >&2
    exit 1
fi

for command_name in file ldd readelf timeout xvfb-run; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "Required validation command is unavailable: '$command_name'." >&2
        exit 1
    fi
done

work_root=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/srvsurvey-appimage-runtime.XXXXXXXX")
cleanup() {
    rm -rf -- "$work_root"
}
trap cleanup EXIT

cd "$work_root"
"$appimage" --appimage-extract >/dev/null
app_dir="$work_root/squashfs-root"
app_library_directory="$app_dir/usr/lib/srvsurvey"
system_library_directory="$app_dir/usr/lib"
library_path="$app_library_directory:$system_library_directory"

dependency_failure=0
while IFS= read -r -d '' candidate; do
    if ! readelf --file-header "$candidate" >/dev/null 2>&1; then
        continue
    fi

    set +e
    dependency_output=$(LD_LIBRARY_PATH="$library_path" ldd "$candidate" 2>&1)
    dependency_status=$?
    set -e
    if grep -Fq "not found" <<<"$dependency_output"; then
        echo "Unresolved dependency for '$candidate':" >&2
        echo "$dependency_output" >&2
        dependency_failure=1
    elif [[ $dependency_status -ne 0 ]] \
        && ! grep -Eq "not a dynamic executable|statically linked" \
            <<<"$dependency_output"; then
        echo "Dependency inspection failed for '$candidate':" >&2
        echo "$dependency_output" >&2
        dependency_failure=1
    fi
done < <(find "$app_dir" -type f -print0)

if [[ $dependency_failure -ne 0 ]]; then
    exit 1
fi

smoke_root="$work_root/smoke"
mkdir -p \
    "$smoke_root/home" \
    "$smoke_root/config" \
    "$smoke_root/data" \
    "$smoke_root/cache" \
    "$smoke_root/runtime"
chmod 0700 "$smoke_root/runtime"
smoke_log="$smoke_root/process.log"

set +e
timeout --signal=TERM --kill-after=2s 8s \
    xvfb-run --auto-servernum --server-args="-screen 0 1280x800x24" \
    env \
        HOME="$smoke_root/home" \
        XDG_CONFIG_HOME="$smoke_root/config" \
        XDG_DATA_HOME="$smoke_root/data" \
        XDG_CACHE_HOME="$smoke_root/cache" \
        XDG_RUNTIME_DIR="$smoke_root/runtime" \
        XDG_SESSION_TYPE=wayland \
        WAYLAND_DISPLAY=wayland-ci \
        "$app_dir/AppRun" >"$smoke_log" 2>&1
smoke_status=$?
set -e

if [[ $smoke_status -ne 124 ]]; then
    echo "The AppImage did not remain running for the XWayland-mode smoke window (status $smoke_status)." >&2
    cat "$smoke_log" >&2
    exit 1
fi

if ! grep -R -Fq \
    "Display host: LinuxXWayland" \
    "$smoke_root/data/SrvSurvey/logs"; then
    echo "The AppImage did not report the LinuxXWayland display host." >&2
    cat "$smoke_log" >&2
    find "$smoke_root/data" -maxdepth 4 -type f -print -exec sed -n '1,120p' {} \; >&2
    exit 1
fi

if ! grep -R -Fq \
    "X11 overlay stacking policy: standard topmost" \
    "$smoke_root/data/SrvSurvey/logs"; then
    echo "The AppImage did not use the safe overlay fallback when no window manager advertised KDE OSD support." >&2
    cat "$smoke_log" >&2
    find "$smoke_root/data" -maxdepth 4 -type f -print -exec sed -n '1,120p' {} \; >&2
    exit 1
fi

echo "AppImage ELF dependency closure, XWayland startup, and overlay policy fallback passed."
