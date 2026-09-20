# Install SrvSurvey on Linux

Current release candidate version: **SrvSurvey-XP 2.1.3.0-rc.50**.

The Linux review build targets 64-bit x86 Linux. The AppImage is the simplest
package for most desktops; the `.tar.gz` archive is a portable fallback. Both
are self-contained and do not require a separate .NET installation.

Ubuntu 26.04 users running Elite through Gamescope can follow the dedicated
[Ubuntu 26.04, Elite Dangerous, and Gamescope guide](UBUNTU_26_GAMESCOPE.md).
It covers multi-monitor placement, selective mouse confinement, 4K/120 Hz
configuration, Windows file migration, and the values each user must tailor.

## Download the package

Download the AppImage or portable archive from the relevant
[SrvSurvey-XP release](https://github.com/Fenris159/SrvSurvey/releases).

Repository maintainers can build and publish a new release as follows:

1. Open the repository's
   [Build and publish SrvSurvey-XP release workflow](https://github.com/Fenris159/SrvSurvey/actions/workflows/build-srvsurvey-xp.yml).
2. Select **Run workflow**, choose the source branch/tag/commit and release
   channel. The workflow reads the complete version, including any RC suffix,
   from the desktop project.
3. After all builds and tests pass, the workflow creates an `xp-v<version>`
   release. Development builds append `-rc.<number>` and are GitHub
   pre-releases; stable builds use the base version and are explicitly not
   assigned GitHub's **Latest** badge.

## Run the AppImage

Keep the AppImage in a dedicated **container folder**. Here, that means an
ordinary directory used to hold the downloaded application; it does not mean a
Docker container. Run it while that directory is your current working
directory:

```bash
mkdir -p "$HOME/Applications/SrvSurvey"
mv "$HOME/Downloads/SrvSurvey-XP-2.1.3.0-rc.50-x86_64.AppImage" \
    "$HOME/Applications/SrvSurvey/SrvSurvey.AppImage"
cd "$HOME/Applications/SrvSurvey"
chmod +x SrvSurvey.AppImage
./SrvSurvey.AppImage
```

To launch the standalone diagnostic replay controller from the same AppImage,
pass its explicit dispatcher option:

```bash
./SrvSurvey.AppImage --replay-controller
```

Replace `2.1.3.0-rc.50` with the downloaded version. Keeping the installed name
as `SrvSurvey.AppImage` gives launchers and the in-application updater a stable
path. Keep it in this folder instead of moving internal files out of the
AppImage.

## Update the AppImage

SrvSurvey can update a running AppImage from its normal update card. The
AppImage and its containing folder must be writable by the current user. The
updater downloads the AppImage selected by the configured release channel,
checks its release-index size and SHA-256 checksum, preserves the current image
as a rollback copy, and starts the replacement. If the replacement does not
confirm a healthy startup, SrvSurvey restores the previous AppImage.

The update helper uses AppImage extract-and-run mode, so updating does not add
a FUSE requirement. Published releases also include embedded AppImage update
information and a matching `.zsync` asset for compatibility with standard
AppImage update tools. If the installed file or folder is read-only, the update
card keeps the manual download instructions available.

If FUSE is unavailable, use AppImage's temporary extract-and-run fallback from
the same folder:

```bash
cd "$HOME/Applications/SrvSurvey"
./SrvSurvey.AppImage --appimage-extract-and-run
```

## Run the portable archive

The extracted archive directory is the application's container folder. Install
it at a stable path so launchers and the in-application updater continue to
point to the same location. Keep all files together and run
`SrvSurvey.Desktop` from that directory:

```bash
mkdir -p "$HOME/Applications/SrvSurvey/portable"
tar -xzf "$HOME/Downloads/SrvSurvey-XP-2.1.3.0-rc.50-linux-x64.tar.gz" \
    -C "$HOME/Applications/SrvSurvey/portable"
cd "$HOME/Applications/SrvSurvey/portable"
chmod +x SrvSurvey.Desktop
./SrvSurvey.Desktop
```

Do not copy `SrvSurvey.Desktop` out by itself. It needs the managed assemblies,
native libraries, and self-contained .NET runtime beside it.

## Update the portable archive

SrvSurvey can update an installation extracted from the portable archive using
its normal update card. Keep the complete extracted package, including
`release-package.json`, in one directory. The installation directory and its
parent must be writable by the current user because the updater stages the new
package beside the current directory, swaps the complete installation, and
keeps the previous version temporarily for rollback. Allow enough free space
for the download, staged package, current installation, and rollback copy.

The updater downloads the `linux-x64` archive for the configured release
channel, verifies its release-index size and SHA-256 checksum, validates the
package manifest, and relaunches from the same stable path. If the replacement
does not confirm a healthy startup, SrvSurvey restores the previous directory.
No privilege-elevation prompt is provided on Linux. Install under your home
directory for automatic updates; a root-owned or otherwise protected location
such as `/opt` must be replaced manually by its administrator.

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
  for its windows and overlays. If X11 cannot read the game pixels needed by
  FSS tuning, first-footfall inference, or rig detection, SrvSurvey asks the
  desktop ScreenCast portal to share the Elite Dangerous window through
  PipeWire.
- **Pure Wayland without XWayland:** `WAYLAND_DISPLAY` is set but `DISPLAY` is
  empty. This is not a supported full-functionality mode and the application
  may fail to open because this build uses Avalonia's X11 backend. Install or
  enable XWayland, or select an Xorg session from the desktop login screen.

### Overlay window strategy

SrvSurvey keeps its established separate-window overlay implementation on
ordinary X11 and XWayland desktops. When the process environment identifies a
Gamescope session, live overlay controls are instead placed in one transparent
window matching the Elite client area. This avoids asking Gamescope to stack
many independent overlay windows while preserving the existing behavior on
KWin, Mutter, Xfwm, Muffin, and Windows.

The selected strategy is recorded in the application log as `Overlay
presentation`. These diagnostic overrides take effect after restarting
SrvSurvey:

```bash
# Force one combined host on X11 or XWayland.
SRVSURVEY_OVERLAY_HOST=combined ./SrvSurvey.Desktop

# Force the established separate-window path, including inside Gamescope.
SRVSURVEY_OVERLAY_HOST=separate ./SrvSurvey.Desktop
```

Place the variable before the AppImage command in the same way when using the
AppImage. Pure native Wayland remains unavailable because topmost placement,
global positioning, game-window tracking, and click-through still require the
X11/XWayland integration in this package.

Run Elite Dangerous and SrvSurvey as the same desktop user on the same display.
Do not start SrvSurvey with `sudo` or from an unrelated SSH session. If Elite is
inside a nested Gamescope session, SrvSurvey must be launched into the same
session so it can see the game window and the Gamescope environment. If it
cannot detect Elite there, first test both programs in the same normal
X11/XWayland desktop session.

### Game screen capture on Wayland

When FSS tuning, first-footfall inference, rig detection, or the rig calibration
panel's **Test** option first needs pixels that XWayland cannot provide, the
desktop opens its normal screen-sharing picker. Select only the **Elite
Dangerous** window. If the game is not offered as a separate window, select the
desktop or monitor where Elite is running. SrvSurvey explains these choices
before opening the picker; use **Alt+Tab** if either prompt appears behind the
game. SrvSurvey crops each requested game region from that shared source;
existing rig calibration controls and saved positions continue to work as they
do on Xorg. The desktop may remember the selection; it can ask again after a
restart or when its permission token expires.

Temporary X11 or portal capture failures are retried with an increasing delay
and normal capture resumes after the next successful frame. Repeated expected
X11 capture errors are summarized periodically in the application log instead
of being written once per FSS sample.

If the application UI repeatedly reports `glXMakeContextCurrent failed`, fully
close every SrvSurvey process before reopening it. To diagnose a driver-specific
GLX failure, launch one session with Avalonia's X11 framebuffer renderer:

```bash
SRVSURVEY_SOFTWARE_RENDERING=1 ./SrvSurvey.AppImage
```

This override affects SrvSurvey's UI renderer only. It does not change Elite
Dangerous, Gamescope, or PipeWire rendering and should normally be used only for
troubleshooting. Replace `SrvSurvey.AppImage` with the actual filename when the
download has not been renamed.

The fallback requires PipeWire, WirePlumber (or another PipeWire session
manager), `xdg-desktop-portal`, and the portal backend for the active desktop,
such as `xdg-desktop-portal-gnome` or `xdg-desktop-portal-kde`. Full GNOME and
KDE installations normally provide these. If the picker is canceled or the
wrong source is shared, open **Settings → Application → Wayland screen
capture** and choose **Choose capture source again**. SrvSurvey restarts, and
the picker opens if the next capture attempt must fall back from X11 to Wayland
screen sharing.

## Elite journal discovery

SrvSurvey detects every existing Elite journal folder it can find, rather than
stopping at the first Steam prefix. The automatic Linux search covers:

- Steam and Flatpak Steam's default `359320` Proton prefixes, plus additional
  Steam libraries listed in `libraryfolders.vdf`;
- Heroic's native and Flatpak game configuration, including its usual
  `~/Games/Heroic/Prefixes` tree;
- Lutris game configuration and the usual prefixes under `~/Games`;
- native and Flatpak Bottles prefixes; and
- a conventional `~/.wine` prefix.

The Multiple commanders card combines commander identities found in all of
those journal folders. Launching another SrvSurvey instance passes that
commander's own journal folder to the new process, so Steam and Epic/Heroic
clients can be monitored at the same time without mixing their companion files.
The selector is disabled when only the current Commander is available.

Starting the executable or AppImage again outside that card shows a confirmation
instead of silently opening a duplicate. Choose **No** to keep the running
instance, or choose **Yes, close it and continue** to terminate it cleanly and
open the replacement. Only **Launch instance** from the Multiple commanders card
authorizes an intentional parallel SrvSurvey process.

Launcher prefixes remain configurable and can live elsewhere. If an unusual
layout is not detected, set `SRVSURVEY_JOURNAL_DIR` or pass
`--journal-directory` with the folder containing `Journal.*.log` and
`Status.json`.

## Distribution prerequisites

Most full GNOME, KDE, Cinnamon, and Xfce installations already contain the
required X11 libraries. Install the following only when they are missing. On a
native Xorg desktop, omit the XWayland package.

### Ubuntu and Debian derivatives

```bash
sudo apt update
sudo apt install libx11-6 libxext6 libice6 libsm6 libfontconfig1 libpipewire-0.3-0
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

### Secure Frontier account storage

Frontier account linking additionally requires the `secret-tool` command and
an unlocked Secret Service-compatible keyring. Full GNOME and KDE Plasma
installations commonly already provide the keyring service. Install the package
that supplies `secret-tool` if it is missing:

```bash
# Ubuntu and Debian derivatives
sudo apt install libsecret-tools

# Arch Linux, Manjaro, and CachyOS
sudo pacman -S --needed libsecret
```

Package names vary on Fedora and openSUSE; use the distribution package search
to find the package providing `/usr/bin/secret-tool`. SrvSurvey deliberately
does not fall back to a plaintext token file. Linking remains unavailable until
both `secret-tool` and an unlocked keyring service are present.

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
- Frontier linking reports that secure token storage is unavailable: install
  `libsecret-tools` on Debian/Ubuntu or `libsecret` on
  Arch/Manjaro/CachyOS, make sure the desktop keyring is unlocked, and restart
  SrvSurvey. These packages provide the required `secret-tool` executable. See
  [Frontier account linking](FRONTIER.md).

## Reference documentation

- [Linux Troubleshooting](Linux_Troubleshooting.md)
- [Overlay Troubleshooting (KDE Plasma automatic handling and fallback rule)](Overlay_Troubleshooting.md)
- [Ubuntu 26.04, Elite Dangerous, and patched Gamescope setup](UBUNTU_26_GAMESCOPE.md)
- [CachyOS, KDE Plasma, and Gamescope setup](CACHYOS_GAMESCOPE.md)
- [Frontier account linking and local data](FRONTIER.md)
- [Avalonia Linux platform behavior](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- [Avalonia Linux runtime dependencies](https://docs.avaloniaui.net/docs/deployment/linux)
- [AppImage FUSE setup and extract-and-run fallback](https://docs.appimage.org/user-guide/troubleshooting/fuse.html)
