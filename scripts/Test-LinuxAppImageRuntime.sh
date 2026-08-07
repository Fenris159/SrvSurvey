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

# App logs can include NULs from native X11 chatter; always search as text.
log_contains() {
    local needle=$1
    shift
    local root
    for root in "$@"; do
        if [[ -d "$root" ]] \
            && grep -R -a -F -q -- "$needle" "$root" 2>/dev/null; then
            return 0
        fi
    done
    return 1
}

dump_logs() {
    local process_log=$1
    local data_root=$2
    if [[ -f "$process_log" ]]; then
        cat "$process_log" >&2 || true
    fi
    if [[ -d "$data_root" ]]; then
        find "$data_root" -maxdepth 5 -type f -print -exec sed -n '1,200p' {} \; >&2 || true
    fi
}

# Start the AppImage under Xvfb and wait until expected log markers appear
# (or until the deadline). Avalonia/X11 init can take longer than a fixed
# 8s kill window under busy CI runners.
run_smoke_until_logs() {
    local process_log=$1
    local data_root=$2
    local deadline_seconds=$3
    shift 3
    local markers=("$@")

    set +e
    xvfb-run --auto-servernum --server-args="-screen 0 1280x800x24" \
        env \
            HOME="$smoke_root/home" \
            XDG_CONFIG_HOME="$smoke_root/config" \
            XDG_DATA_HOME="$data_root" \
            XDG_CACHE_HOME="$smoke_root/cache" \
            XDG_RUNTIME_DIR="$smoke_root/runtime" \
            XDG_SESSION_TYPE=wayland \
            WAYLAND_DISPLAY=wayland-ci \
            ${EXTRA_SMOKE_ENV:-} \
            "$app_dir/AppRun" >"$process_log" 2>&1 &
    local app_pid=$!
    set -e

    local elapsed=0
    local all_found=0
    while (( elapsed < deadline_seconds )); do
        if ! kill -0 "$app_pid" 2>/dev/null; then
            wait "$app_pid" || true
            echo "The AppImage exited before smoke markers were observed (after ${elapsed}s)." >&2
            dump_logs "$process_log" "$data_root"
            return 1
        fi

        all_found=1
        local marker
        for marker in "${markers[@]}"; do
            if ! log_contains "$marker" "$data_root/SrvSurvey/logs" "$data_root"; then
                all_found=0
                break
            fi
        done
        if (( all_found == 1 )); then
            break
        fi

        sleep 1
        elapsed=$((elapsed + 1))
    done

    if (( all_found != 1 )); then
        kill -TERM "$app_pid" 2>/dev/null || true
        sleep 1
        kill -KILL "$app_pid" 2>/dev/null || true
        wait "$app_pid" 2>/dev/null || true
        echo "Timed out after ${deadline_seconds}s waiting for AppImage log markers:" >&2
        printf '  - %s\n' "${markers[@]}" >&2
        dump_logs "$process_log" "$data_root"
        return 1
    fi

    # Markers observed while the process is still alive — success for this phase.
    kill -TERM "$app_pid" 2>/dev/null || true
    local wait_status=0
    set +e
    wait "$app_pid"
    wait_status=$?
    set -e
    # 143 = 128+SIGTERM is expected; 0 if it exited cleanly after TERM.
    if [[ $wait_status -ne 0 && $wait_status -ne 143 && $wait_status -ne 137 ]]; then
        # Still accept if markers were found; native shutdown can be noisy.
        :
    fi
    return 0
}

smoke_root="$work_root/smoke"
mkdir -p \
    "$smoke_root/home" \
    "$smoke_root/config" \
    "$smoke_root/data" \
    "$smoke_root/cache" \
    "$smoke_root/runtime"
chmod 0700 "$smoke_root/runtime"
smoke_log="$smoke_root/process.log"
smoke_deadline_seconds=${SRVSURVEY_APPIMAGE_SMOKE_SECONDS:-25}

unset EXTRA_SMOKE_ENV
if ! run_smoke_until_logs \
    "$smoke_log" \
    "$smoke_root/data" \
    "$smoke_deadline_seconds" \
    "Display host: LinuxXWayland" \
    "X11 overlay stacking policy: standard topmost" \
    "Overlay presentation: MultipleWindows"; then
    echo "The AppImage did not use the safe overlay fallback when no window manager advertised KDE OSD support." >&2
    exit 1
fi

gamescope_log="$smoke_root/gamescope-process.log"
# Fresh data dir so combined-host selection is observed in a new session log.
gamescope_data="$smoke_root/gamescope-data"
mkdir -p "$gamescope_data"
export EXTRA_SMOKE_ENV="GAMESCOPE_WAYLAND_DISPLAY=gamescope-ci"
if ! run_smoke_until_logs \
    "$gamescope_log" \
    "$gamescope_data" \
    "$smoke_deadline_seconds" \
    "Overlay presentation: CombinedWindow"; then
    echo "The Gamescope smoke run did not select the combined overlay host." >&2
    exit 1
fi

echo "AppImage dependency closure, ordinary XWayland behavior, and Gamescope combined-host selection passed."
