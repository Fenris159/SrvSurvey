#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "Usage: $0 build|runtime DEB_CACHE_DIRECTORY" >&2
    exit 2
fi

source /etc/os-release
if [[ $ID != ubuntu || $(dpkg --print-architecture) != amd64 ]]; then
    echo "Release dependency setup requires an Ubuntu x64 runner." >&2
    exit 2
fi
case "$VERSION_ID" in
    24.04) icu_package=libicu74 ;;
    26.04) icu_package=libicu78 ;;
    *) echo "Unsupported Ubuntu release: '$VERSION_ID'." >&2; exit 2 ;;
esac

# Hosted images normally provide the graphics/globalization libraries already.
# Declare them explicitly so smaller runner images can still run the same checks.
packages=(binutils file "$icu_package" libfontconfig1 libice6 libsm6 libx11-6 libxext6 xauth xvfb)
case "$1" in
    build)
        packages+=(appstream desktop-file-utils imagemagick patchelf)
        ;;
    runtime) ;;
    *) echo "Unknown release dependency profile: '$1'." >&2; exit 2 ;;
esac

missing=()
for package in "${packages[@]}"; do
    if [[ $(dpkg-query -W -f='${db:Status-Status}' "$package" 2>/dev/null || true) != installed ]]; then
        missing+=("$package")
    fi
done

if (( ${#missing[@]} == 0 )); then
    echo "All release dependencies are installed; skipping APT."
    exit 0
fi

# Intended for disposable Ubuntu x64 release runners. Do not change the host's
# source files or restore installed system directories from a cache.
cache_directory=$(realpath -m "$2")
mkdir -p "$cache_directory"
work_root=$(mktemp -d)
trap 'rm -rf -- "$work_root"' EXIT
chmod 0755 "$work_root"

as_root=()
if (( EUID != 0 )); then
    as_root=(sudo -n)
fi

apt_options=(
    -o "Dir::Cache::archives=$cache_directory"
    -o "Dir::Etc::sourcelist=$work_root/ubuntu.list"
    -o Dir::Etc::sourceparts=-
    -o APT::Get::List-Cleanup=0
    -o APT::Keep-Downloaded-Packages=true
    -o Acquire::Retries=2
    -o Acquire::http::Timeout=20
    -o Acquire::https::Timeout=20
)

# The Azure mirror took ten minutes to transfer 26 MB in the RC70 release.
# Prefer Ubuntu's official archive over HTTPS, with Azure as a bounded fallback.
# Time out only index/archive downloads; never interrupt an active dpkg install.
for mirror in https://archive.ubuntu.com/ubuntu https://azure.archive.ubuntu.com/ubuntu; do
    echo "Fetching missing release dependencies from $mirror: ${missing[*]}"
    security_mirror=https://security.ubuntu.com/ubuntu
    if [[ $mirror == https://azure.archive.ubuntu.com/ubuntu ]]; then
        security_mirror=$mirror
    fi
    cat > "$work_root/ubuntu.list" <<EOF
deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] $mirror $VERSION_CODENAME main universe
deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] $mirror $VERSION_CODENAME-updates main universe
deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] $security_mirror $VERSION_CODENAME-security main universe
EOF
    if timeout 90s "${as_root[@]}" apt-get "${apt_options[@]}" update --error-on=any \
        && timeout 180s "${as_root[@]}" apt-get "${apt_options[@]}" \
            --yes --no-install-recommends --download-only install "${missing[@]}"; then
        "${as_root[@]}" env DEBIAN_FRONTEND=noninteractive apt-get "${apt_options[@]}" \
            --yes --no-install-recommends --no-download install "${missing[@]}"
        echo "Release dependencies installed; downloaded .deb archives retained for caching."
        exit 0
    fi
    echo "Dependency download failed or timed out; trying the fallback mirror." >&2
done

echo "Unable to download required release dependencies from either Ubuntu mirror." >&2
exit 1
