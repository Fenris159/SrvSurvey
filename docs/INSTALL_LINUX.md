# Install SrvSurvey on Linux

The Linux review build targets 64-bit x86 Linux. The AppImage is the simplest
package for most desktops; the `.tar.gz` archive is a portable fallback. Both
are self-contained and do not require a separate .NET installation.

## Download the package

Download the AppImage or portable archive from the repository's
[latest GitHub release](https://github.com/Fenris159/SrvSurvey/releases/latest).

Repository maintainers can build and publish a new release as follows:

1. Open the repository's
   [Publish Windows and Linux release workflow](https://github.com/Fenris159/SrvSurvey/actions/workflows/manual-release-packages.yml).
2. Select **Run workflow**, enter a three- or four-part release version, and
   start the run.
3. After all builds and tests pass, the workflow creates the corresponding
   `v<version>` tag and GitHub Release and attaches the AppImage, portable
   archive, Windows ZIP, checksums, release index, and software bills of
   materials.

## Run the AppImage

Keep the AppImage in a dedicated **container folder**. Here, that means an
ordinary directory used to hold the downloaded application; it does not mean a
Docker container. Run it while that directory is your current working
directory:

```bash
mkdir -p "$HOME/Applications/SrvSurvey"
mv "$HOME/Downloads/SrvSurvey-Avalonia-1.2.3-x86_64.AppImage" \
    "$HOME/Applications/SrvSurvey/"
cd "$HOME/Applications/SrvSurvey"
chmod +x SrvSurvey-Avalonia-1.2.3-x86_64.AppImage
./SrvSurvey-Avalonia-1.2.3-x86_64.AppImage
```

Replace `1.2.3` with the downloaded version. Keep the AppImage in this folder;
create a launcher or shortcut that points to it instead of moving internal
files out of the AppImage.

If FUSE is unavailable, use AppImage's temporary extract-and-run fallback from
the same folder:

```bash
cd "$HOME/Applications/SrvSurvey"
./SrvSurvey-Avalonia-1.2.3-x86_64.AppImage --appimage-extract-and-run
```

## Run the portable archive

The extracted archive directory is the application's container folder. Keep
all files together and run `SrvSurvey.Desktop` from that directory:

```bash
mkdir -p "$HOME/Applications/SrvSurvey/1.2.3"
tar -xzf "$HOME/Downloads/SrvSurvey-Avalonia-1.2.3-linux-x64.tar.gz" \
    -C "$HOME/Applications/SrvSurvey/1.2.3"
cd "$HOME/Applications/SrvSurvey/1.2.3"
chmod +x SrvSurvey.Desktop
./SrvSurvey.Desktop
```

Do not copy `SrvSurvey.Desktop` out by itself. It needs the managed assemblies,
native libraries, and self-contained .NET runtime beside it.

## Display-server modes

Check your current desktop session before troubleshooting overlays:

```bash
printf 'session=%s\nDISPLAY=%s\nWAYLAND_DISPLAY=%s\n' \
    "${XDG_SESSION_TYPE:-unset}" \
    "${DISPLAY:-unset}" \
    "${WAYLAND_DISPLAY:-unset}"
```

- **Native X11/Xorg:** `XDG_SESSION_TYPE` is normally `x11` and `DISPLAY` is
  set. SrvSurvey uses its complete X11 overlay, capture, window-tracking, and
  global-input path. XWayland is not required.
- **Wayland with XWayland:** `XDG_SESSION_TYPE` is normally `wayland`, while
  both `WAYLAND_DISPLAY` and `DISPLAY` are set. SrvSurvey runs through XWayland
  and uses the same complete X11-compatible feature path.
- **Pure Wayland without XWayland:** `WAYLAND_DISPLAY` is set but `DISPLAY` is
  empty. This is not a supported full-functionality mode and the application
  may fail to open because this build uses Avalonia's X11 backend. Install or
  enable XWayland, or select an Xorg session from the desktop login screen.

Run Elite Dangerous and SrvSurvey as the same desktop user on the same display.
Do not start SrvSurvey with `sudo` or from an unrelated SSH session. If Elite is
inside a nested Gamescope session and cannot be detected, first test with both
programs in the same normal X11/XWayland desktop session.

## Distribution prerequisites

Most full GNOME, KDE, Cinnamon, and Xfce installations already contain the
required X11 libraries. Install the following only when they are missing. On a
native Xorg desktop, omit the XWayland package.

### Ubuntu and Debian derivatives

```bash
sudo apt update
sudo apt install libx11-6 libxext6 libice6 libsm6 libfontconfig1
sudo apt install xwayland  # Wayland sessions only
```

For AppImage FUSE support, Ubuntu 24.04 and newer use `libfuse2t64`; Ubuntu
22.04 and older Ubuntu releases use `libfuse2`. Debian and its derivatives may
provide either name depending on the release:

```bash
sudo apt install libfuse2t64  # Ubuntu 24.04 or newer
# or
sudo apt install libfuse2     # Ubuntu 22.04 or a distro providing this name
```

### Fedora

```bash
sudo dnf install fuse-libs libX11 libXext libICE libSM fontconfig
sudo dnf install xorg-x11-server-Xwayland  # Wayland sessions only
```

### Arch Linux and Manjaro

```bash
sudo pacman -S --needed fuse2 libx11 libxext libice libsm fontconfig
sudo pacman -S --needed xorg-xwayland  # Wayland sessions only
```

### openSUSE Leap and Tumbleweed

```bash
sudo zypper install fuse libfuse2 libX11-6 libXext6 libICE6 libSM6 fontconfig
sudo zypper install xwayland  # Wayland sessions only
```

Package names can change between distribution releases. If one is unavailable,
search your distribution for the package that provides the corresponding
shared library rather than installing an untrusted binary manually.

## Troubleshooting

Common launch and library problems are listed below. For a fuller set of issues
(including KDE Plasma's automatic overlay handling and its manual fallback) see the dedicated
**[Linux Troubleshooting](Linux_Troubleshooting.md)** document.

- `Permission denied`: run `chmod +x` on the AppImage or
  `SrvSurvey.Desktop`.
- `AppImages require FUSE to run`: install the distribution's FUSE 2 runtime or
  use `--appimage-extract-and-run`.
- `cannot open shared object file`: install the distribution prerequisites
  above and start the application again from its complete container folder.
- Overlays do not follow Elite: confirm `DISPLAY` is set, both applications are
  on the same display, and neither was started as a different user.
  **On KDE Plasma**, current builds request KWin's advertised on-screen-display
  window type automatically. If that does not work, use the manual fallback in
  [Overlay Troubleshooting](Overlay_Troubleshooting.md).
- `DISPLAY` is empty in a Wayland session: enable XWayland or log into an Xorg
  session; native Wayland is not the backend used by this package.

## Reference documentation

- [Linux Troubleshooting](Linux_Troubleshooting.md)
- [Overlay Troubleshooting (KDE Plasma automatic handling and fallback rule)](Overlay_Troubleshooting.md)
- [Avalonia Linux platform behavior](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- [Avalonia Linux runtime dependencies](https://docs.avaloniaui.net/docs/deployment/linux)
- [AppImage FUSE setup and extract-and-run fallback](https://docs.appimage.org/user-guide/troubleshooting/fuse.html)
