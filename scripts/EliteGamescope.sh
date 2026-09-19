#!/usr/bin/env bash

# Run Elite Dangerous in a nested Gamescope session while leaving Frontier's
# launcher as an ordinary decorated window. This script requires the matching
# runtime-relative-mouse patch documented in docs/UBUNTU_26_GAMESCOPE.md.

set -u

gamescope_version="${ELITE_GAMESCOPE_VERSION:-3.16.29}"
gamescope_root="${ELITE_GAMESCOPE_ROOT:-$HOME/.local/opt/gamescope-${gamescope_version}}"
gamescope_bin="${gamescope_root}/bin/gamescope"
gamescopectl_bin="${gamescope_root}/bin/gamescopectl"

# XWayland coordinates and outer launcher size. Obtain the primary output's
# geometry from `xrandr --current`; see the guide for the centering formula.
window_x="${ELITE_GAMESCOPE_X:-0}"
window_y="${ELITE_GAMESCOPE_Y:-0}"
window_width="${ELITE_GAMESCOPE_OUTPUT_WIDTH:-2560}"
window_height="${ELITE_GAMESCOPE_OUTPUT_HEIGHT:-1440}"

# Resolution and refresh rate exposed to Elite, plus an empirical pointer
# multiplier. Change these together and recalibrate sensitivity when needed.
game_width="${ELITE_GAMESCOPE_GAME_WIDTH:-3840}"
game_height="${ELITE_GAMESCOPE_GAME_HEIGHT:-2160}"
game_refresh="${ELITE_GAMESCOPE_REFRESH:-120}"
mouse_sensitivity="${ELITE_GAMESCOPE_MOUSE_SENSITIVITY:-2.25}"
game_title="${ELITE_GAMESCOPE_GAME_TITLE:-Elite - Dangerous (CLIENT)}"

if [[ ! -x "$gamescope_bin" || ! -x "$gamescopectl_bin" ]]; then
    printf 'Patched Gamescope installation not found under: %s\n' "$gamescope_root" >&2
    exit 1
fi

if (( $# == 0 )); then
    printf 'Usage: %s <Steam-expanded Elite command>\n' "$0" >&2
    exit 2
fi

scope_pid=""
scope_window=""
scope_display=""

stop_scope() {
    if [[ -n "$scope_pid" ]] && kill -0 "$scope_pid" 2>/dev/null; then
        kill -TERM "$scope_pid" 2>/dev/null || true
    fi
    return 0
}

find_scope_window() {
    wmctrl -lp 2>/dev/null | awk -v pid="$scope_pid" '$3 == pid { print $1; exit }'
    return $?
}

find_scope_display() {
    local child_pid
    local display

    while read -r child_pid; do
        [[ -n "$child_pid" ]] || continue
        display=$(tr '\0' '\n' < "/proc/${child_pid}/environ" 2>/dev/null \
            | sed -n 's/^GAMESCOPE_WAYLAND_DISPLAY=//p' \
            | head -n 1)
        if [[ -n "$display" ]]; then
            printf '%s\n' "$display"
            return 0
        fi
    done < <(pgrep -P "$scope_pid" 2>/dev/null || true)

    return 1
}

set_relative_mouse() {
    local enabled=$1

    if [[ -z "$scope_display" ]]; then
        scope_display=$(find_scope_display 2>/dev/null || true)
    fi
    [[ -n "$scope_display" ]] || return 1

    GAMESCOPE_WAYLAND_DISPLAY="$scope_display" \
        "$gamescopectl_bin" force_relative_mouse "$enabled" \
        >/dev/null 2>&1
}

game_surface_is_selected() {
    [[ -n "$scope_window" ]] || return 1
    xprop -id "$scope_window" _NET_WM_NAME 2>/dev/null \
        | grep -Fq "$game_title"
}

center_launcher() {
    [[ -n "$scope_window" ]] || return 0
    wmctrl -ir "$scope_window" -b remove,fullscreen 2>/dev/null || true
    wmctrl -ir "$scope_window" \
        -e "0,${window_x},${window_y},${window_width},${window_height}" \
        2>/dev/null || true
}

fullscreen_game() {
    [[ -n "$scope_window" ]] || return 0
    wmctrl -ir "$scope_window" -b add,fullscreen 2>/dev/null || true
}

trap stop_scope INT TERM HUP

SDL_VIDEODRIVER=x11 "$gamescope_bin" \
    --backend sdl \
    -W "$window_width" -H "$window_height" \
    -w "$game_width" -h "$game_height" \
    -r "$game_refresh" \
    --mouse-sensitivity "$mouse_sensitivity" \
    -- "$@" &
scope_pid=$!

for _ in $(seq 1 100); do
    if ! kill -0 "$scope_pid" 2>/dev/null; then
        break
    fi
    scope_window=$(find_scope_window)
    if [[ -n "$scope_window" ]]; then
        center_launcher
        break
    fi
    sleep 0.1
done

game_fullscreen=false
relative_mouse=false
while kill -0 "$scope_pid" 2>/dev/null; do
    if game_surface_is_selected; then
        if [[ "$game_fullscreen" == false ]]; then
            fullscreen_game
            game_fullscreen=true
        fi
        if [[ "$relative_mouse" == false ]] && set_relative_mouse true; then
            relative_mouse=true
        fi
    elif [[ "$game_fullscreen" == true ]]; then
        if [[ "$relative_mouse" == true ]] && ! set_relative_mouse false; then
            sleep 0.25
            continue
        fi
        relative_mouse=false
        center_launcher
        game_fullscreen=false
    fi
    sleep 0.25
done

wait "$scope_pid"
