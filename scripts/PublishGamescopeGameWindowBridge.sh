#!/usr/bin/env bash
set -euo pipefail

# Publish the desktop location of a fullscreen native-Wayland Gamescope session.
# The caller owns the Gamescope process and removes the printed marker on exit.
if (( $# != 3 )); then
    printf 'Usage: %s GAMESCOPE_PID NESTED_X11_DISPLAY DESKTOP_OUTPUT\n' "$0" >&2
    exit 2
fi

scope_pid=$1
nested_display=$2
desktop_output=$3
runtime_dir=${XDG_RUNTIME_DIR:-}

if [[ ! $scope_pid =~ ^[1-9][0-9]*$ || ! $nested_display =~ ^:[0-9]+(\.[0-9]+)?$ || ! -d $runtime_dir ]]; then
    printf 'A live Gamescope PID, nested X11 display, and XDG_RUNTIME_DIR are required.\n' >&2
    exit 2
fi

stat_line=$(<"/proc/${scope_pid}/stat")
read -r -a process_fields <<< "${stat_line##*) }"
start_time=${process_fields[19]:-}
if [[ ! $start_time =~ ^[0-9]+$ ]]; then
    printf 'Could not read Gamescope process start time.\n' >&2
    exit 1
fi

output_line=$(xrandr --query | awk -v output="$desktop_output" \
    '$1 == output && $2 == "connected" { print; exit }')
geometry=$(printf '%s\n' "$output_line" | grep -oE \
    '[0-9]+x[0-9]+\+-?[0-9]+\+-?[0-9]+' | head -n 1) || true
if [[ -z $geometry ]]; then
    printf 'No XWayland geometry found for output %s.\n' "$desktop_output" >&2
    exit 1
fi
IFS='x+' read -r width height x y <<< "$geometry"

marker="${runtime_dir}/GamescopeGameWindowBridge.${scope_pid}"
temporary_marker=$(mktemp "${marker}.XXXXXX")
trap 'unlink "$temporary_marker" 2>/dev/null || true' EXIT
printf '%s\n%s\n%s %s %s %s\n' \
    "$start_time" "$nested_display" "$x" "$y" "$width" "$height" \
    > "$temporary_marker"
mv -f -- "$temporary_marker" "$marker"
printf '%s\n' "$marker"
