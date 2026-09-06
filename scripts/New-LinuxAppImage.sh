#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 7 ]]; then
    echo "Usage: $0 PUBLISH_DIRECTORY VERSION ICON_PATH LINUXDEPLOY APPIMAGETOOL RUNTIME_FILE OUTPUT_PATH" >&2
    exit 2
fi

publish_directory=$(realpath "$1")
version=$2
icon_path=$(realpath "$3")
linuxdeploy=$(realpath "$4")
appimagetool=$(realpath "$5")
runtime_file=$(realpath "$6")
output_path=$7
repository_root=$(realpath "$(dirname "${BASH_SOURCE[0]}")/..")
packaging_root="$repository_root/packaging/linux"

if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(-rc\.[1-9][0-9]*(\.(0|[1-9][0-9]*))?)?$ ]]; then
    echo "Invalid AppImage version: '$version'." >&2
    exit 2
fi

if [[ ! -x "$publish_directory/SrvSurvey.Desktop" ]]; then
    echo "The Linux publish entry point is missing or is not executable." >&2
    exit 1
fi

if [[ ! -f "$publish_directory/release-package.json" ]]; then
    echo "The checksum-indexed release-package.json manifest is missing." >&2
    exit 1
fi

if [[ ! -x "$linuxdeploy" ]]; then
    echo "linuxdeploy is missing or is not executable." >&2
    exit 1
fi

if [[ ! -x "$appimagetool" ]]; then
    echo "appimagetool is missing or is not executable." >&2
    exit 1
fi

if [[ ! -s "$runtime_file" ]]; then
    echo "The verified AppImage runtime is missing or empty." >&2
    exit 1
fi

if ! command -v pwsh >/dev/null 2>&1; then
    echo "PowerShell is required to refresh the post-deployment package manifest." >&2
    exit 1
fi

if [[ -e "$output_path" ]]; then
    echo "Refusing to overwrite existing AppImage output: '$output_path'." >&2
    exit 1
fi

work_root=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/srvsurvey-appimage.XXXXXXXX")
app_dir="$work_root/SrvSurvey.AppDir"
mkdir -p \
    "$app_dir/usr/lib/srvsurvey" \
    "$app_dir/usr/share/applications" \
    "$app_dir/usr/share/icons/hicolor/256x256/apps" \
    "$app_dir/usr/share/metainfo" \
    "$app_dir/usr/share/licenses/srvsurvey"

cp -a "$publish_directory/." "$app_dir/usr/lib/srvsurvey/"
install -m 0755 "$packaging_root/AppRun" "$app_dir/AppRun"
install -m 0644 \
    "$packaging_root/io.github.fenris159.SrvSurvey.desktop" \
    "$app_dir/io.github.fenris159.SrvSurvey.desktop"
install -m 0644 \
    "$packaging_root/io.github.fenris159.SrvSurvey.desktop" \
    "$app_dir/usr/share/applications/io.github.fenris159.SrvSurvey.desktop"
install -m 0644 "$icon_path" "$app_dir/srvsurvey.png"
install -m 0644 \
    "$icon_path" \
    "$app_dir/usr/share/icons/hicolor/256x256/apps/srvsurvey.png"
install -m 0644 \
    "$packaging_root/io.github.fenris159.SrvSurvey.metainfo.xml" \
    "$app_dir/usr/share/metainfo/io.github.fenris159.SrvSurvey.metainfo.xml"
install -m 0644 \
    "$repository_root/LICENSE" \
    "$app_dir/usr/share/licenses/srvsurvey/LICENSE"
ln -s srvsurvey.png "$app_dir/.DirIcon"

NO_STRIP=1 "$linuxdeploy" --appimage-extract-and-run \
    --appdir="$app_dir" \
    --deploy-deps-only="$app_dir/usr/lib/srvsurvey" \
    --custom-apprun="$packaging_root/AppRun"

pwsh -NoLogo -NoProfile -File \
    "$repository_root/scripts/New-CrossPlatformPackageManifest.ps1" \
    -PublishDirectory "$app_dir/usr/lib/srvsurvey" \
    -Version "$version" \
    -RuntimeIdentifier linux-x64

mkdir -p "$(dirname "$output_path")"
ARCH=x86_64 VERSION="$version" \
    "$appimagetool" --appimage-extract-and-run \
    --runtime-file "$runtime_file" "$app_dir" "$output_path"
chmod 0755 "$output_path"

echo "Created $output_path from the checksum-indexed Linux publish output."
