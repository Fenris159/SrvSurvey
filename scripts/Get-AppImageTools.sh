#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 TOOL_CACHE_DIRECTORY" >&2
    exit 2
fi

cache_directory=$(realpath -m "$1")
mkdir -p "$cache_directory"
temporary_file=
trap 'if [[ -n $temporary_file ]]; then rm -f -- "$temporary_file"; fi' EXIT

get_tool() {
    local name=$1 url=$2 checksum=$3
    local target="$cache_directory/$name"
    if [[ -f $target ]] && echo "$checksum  $target" | sha256sum --check --status; then
        echo "Verified cached $name."
    else
        echo "Downloading $name (missing or invalid cache entry)."
        temporary_file=$(mktemp "$cache_directory/$name.XXXXXXXX")
        curl --fail --location --retry 3 --connect-timeout 20 --max-time 180 \
            --proto '=https' --proto-redir '=https' \
            --output "$temporary_file" "$url"
        echo "$checksum  $temporary_file" | sha256sum --check --strict
        mv -f -- "$temporary_file" "$target"
        temporary_file=
    fi
    chmod 0755 "$target"
}

# Changing a pinned URL/checksum changes the workflow's cache key. Always verify
# cached files as well as fresh downloads before making either executable.
get_tool linuxdeploy-x86_64.AppImage \
    https://github.com/linuxdeploy/linuxdeploy/releases/download/1-alpha-20251107-1/linuxdeploy-x86_64.AppImage \
    c20cd71e3a4e3b80c3483cef793cda3f4e990aca14014d23c544ca3ce1270b4d
get_tool appimagetool-x86_64.AppImage \
    https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage \
    ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
get_tool runtime-x86_64 \
    https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64 \
    2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d
