#!/usr/bin/env bash

# Launch Elite in native Wayland Gamescope with its launcher windowed and the
# game fullscreen. Publish the bridge used by desktop-hosted SrvSurvey overlays.
# See docs/UBUNTU_26_GAMESCOPE.md before using the matching Gamescope patch.
set -u

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
gamescope_root=${ELITE_GAMESCOPE_ROOT:-$HOME/.local/opt/gamescope-3.16.29}
gamescope_bin=$gamescope_root/bin/gamescope
gamescopectl_bin=$gamescope_root/bin/gamescopectl
bridge_helper=${ELITE_GAMESCOPE_BRIDGE_HELPER:-$script_dir/PublishGamescopeGameWindowBridge.sh}
game_output=${ELITE_GAMESCOPE_OUTPUT:-}
launcher_width=${ELITE_GAMESCOPE_LAUNCHER_WIDTH:-2560}
launcher_height=${ELITE_GAMESCOPE_LAUNCHER_HEIGHT:-1440}
game_width=${ELITE_GAMESCOPE_GAME_WIDTH:-3840}
game_height=${ELITE_GAMESCOPE_GAME_HEIGHT:-2160}
refresh=${ELITE_GAMESCOPE_REFRESH:-120}
mouse_sensitivity=${ELITE_GAMESCOPE_MOUSE_SENSITIVITY:-2.25}
game_title=${ELITE_GAMESCOPE_GAME_TITLE:-Elite - Dangerous (CLIENT)}
wayland_debug=${ELITE_GAMESCOPE_RADV_DEBUG:-}

if [[ -z $game_output || ! -x $gamescope_bin || ! -x $gamescopectl_bin || ! -x $bridge_helper || $# == 0 ]]; then
    printf 'Set ELITE_GAMESCOPE_OUTPUT, install the patched Gamescope and bridge helper, then pass the Steam-expanded Elite command.\n' >&2
    exit 2
fi

scope_pid=""
scope_display=""
nested_display=""
bridge_marker=""
relative_mouse=false
game_fullscreen=true

stop_scope() {
    if [[ -n "$bridge_marker" && -f "$bridge_marker" ]]; then
        unlink "$bridge_marker"
    fi
    if [[ -n "$scope_pid" ]] && kill -0 "$scope_pid" 2>/dev/null; then
        kill -TERM "$scope_pid" 2>/dev/null || true
    fi
}

publish_game_window_bridge() {
    [[ -n "$nested_display" && -n "$scope_pid" && -n "${XDG_RUNTIME_DIR:-}" ]] || return 1
    bridge_marker=$("$bridge_helper" "$scope_pid" "$nested_display" "$game_output")
}

find_scope_environment() {
    local child_pid display wayland_display

    while read -r child_pid; do
        [[ -n "$child_pid" ]] || continue
        display=$(tr '\0' '\n' < "/proc/${child_pid}/environ" 2>/dev/null \
            | sed -n 's/^DISPLAY=//p' | head -n 1)
        wayland_display=$(tr '\0' '\n' < "/proc/${child_pid}/environ" 2>/dev/null \
            | sed -n 's/^GAMESCOPE_WAYLAND_DISPLAY=//p' | head -n 1)
        if [[ -n "$display" && -n "$wayland_display" ]]; then
            nested_display=$display
            scope_display=$wayland_display
            return 0
        fi
    done < <(pgrep -P "$scope_pid" 2>/dev/null || true)

    return 1
}

set_relative_mouse() {
    local enabled=$1
    [[ -n "$scope_display" ]] || return 1

    GAMESCOPE_WAYLAND_DISPLAY="$scope_display" \
        "$gamescopectl_bin" force_relative_mouse "$enabled" \
        >/dev/null 2>&1
}

set_window_fullscreen() {
    local enabled=$1
    [[ -n "$scope_display" ]] || return 1

    GAMESCOPE_WAYLAND_DISPLAY="$scope_display" \
        "$gamescopectl_bin" wayland_fullscreen "$enabled" \
        >/dev/null 2>&1
}

srv_survey_interaction_is_active() {
    local marker pid
    local runtime_dir="${XDG_RUNTIME_DIR:-}"
    [[ -d "$runtime_dir" ]] || return 1

    for marker in "$runtime_dir"/X11OverlayInteractionMarker.*; do
        [[ -f "$marker" ]] || continue
        pid="${marker##*.}"
        if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
            return 0
        fi
    done

    return 1
}

game_surface_is_selected() {
    local window title
    [[ -n "$nested_display" ]] || return 1

    window=$(DISPLAY="$nested_display" xprop -root _NET_ACTIVE_WINDOW 2>/dev/null \
        | awk '{print $NF}')
    [[ -n "$window" && "$window" != "0x0" ]] || return 1
    title=$(DISPLAY="$nested_display" xprop -id "$window" _NET_WM_NAME WM_NAME 2>/dev/null)
    [[ "$title" == *"$game_title"* ]]
}

overlay_is_open() {
    [[ -n "$nested_display" ]] || return 1
    DISPLAY="$nested_display" xwininfo -root -tree 2>/dev/null \
        | grep -Eq '"Steam Overlay"|"gameoverlayui"|"Steam": .*( [0-9]{4}x[0-9]+)'
}

trap stop_scope EXIT INT TERM HUP

GAMESCOPE_WAYLAND_PREFERRED_OUTPUT="$game_output" \
RADV_DEBUG="$wayland_debug" "$gamescope_bin" \
    --backend wayland \
    -b -f -m 1 \
    -o "$refresh" \
    -W "$launcher_width" -H "$launcher_height" \
    -w "$game_width" -h "$game_height" \
    -r "$refresh" \
    --mouse-sensitivity "$mouse_sensitivity" \
    -- env -u RADV_DEBUG -- "$@" &
scope_pid=$!

for _ in $(seq 1 100); do
    if ! kill -0 "$scope_pid" 2>/dev/null; then
        break
    fi
    if find_scope_environment; then
        break
    fi
    sleep 0.1
done

while kill -0 "$scope_pid" 2>/dev/null; do
    if [[ -z "$scope_display" || -z "$nested_display" ]]; then
        find_scope_environment || true
    fi
    if [[ -z "$bridge_marker" ]]; then
        publish_game_window_bridge || true
    fi

    if game_surface_is_selected; then
        if [[ "$game_fullscreen" == false ]] && set_window_fullscreen true; then
            game_fullscreen=true
        fi
        if srv_survey_interaction_is_active || overlay_is_open; then
            if [[ "$relative_mouse" == true ]] && set_relative_mouse false; then
                relative_mouse=false
            fi
        elif [[ "$relative_mouse" == false ]] && set_relative_mouse true; then
            relative_mouse=true
        fi
    else
        if [[ "$relative_mouse" == true ]] && set_relative_mouse false; then
            relative_mouse=false
        fi
        if [[ "$game_fullscreen" == true ]] && set_window_fullscreen false; then
            game_fullscreen=false
        fi
    fi
    sleep 0.25
done

wait "$scope_pid"
