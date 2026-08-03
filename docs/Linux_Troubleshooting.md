# Linux Troubleshooting

This page collects common problems and fixes for the Linux AppImage / portable builds of SrvSurvey (Avalonia). For installation and display-server prerequisites see [INSTALL_LINUX.md](INSTALL_LINUX.md).

## Overlay windows do not appear or stay behind Elite Dangerous

**Most common on KDE Plasma.** Plasma is stricter about window layers over exclusive full-screen applications. Current builds automatically use KWin's on-screen-display type when the window manager advertises it.

→ See the dedicated **[Overlay Troubleshooting](Overlay_Troubleshooting.md)** guide for behavior details and a manual KDE Plasma Layer rule if the automatic path is insufficient.

## Permission denied when launching the AppImage or binary

```bash
chmod +x SrvSurvey-XP-*-x86_64.AppImage
# or
chmod +x SrvSurvey.Desktop
```

## AppImages require FUSE to run

Install the distribution’s FUSE 2 package, or use the extract-and-run fallback:

```bash
./SrvSurvey-XP-*-x86_64.AppImage --appimage-extract-and-run
```

- Ubuntu 24.04+: `sudo apt install libfuse2t64`
- Older Ubuntu / many Debian derivatives: `sudo apt install libfuse2`
- Fedora: `sudo dnf install fuse-libs`
- Arch: `sudo pacman -S fuse2`

## cannot open shared object file / missing libraries

Install the X11 runtime packages listed in [INSTALL_LINUX.md](INSTALL_LINUX.md#distribution-prerequisites). Start the application from its complete container folder (do not move the executable alone).

## Overlays do not follow the Elite window / game not detected

- Confirm `DISPLAY` is set and both programs are on the same display.
- Both must run as the same desktop user (never start SrvSurvey with `sudo`).
- If Elite is inside a nested Gamescope session, first test both programs in a normal X11/XWayland desktop session.
- Pure Wayland without XWayland is not a supported full-functionality mode.

## DISPLAY is empty under Wayland

Enable XWayland or log into an Xorg session. This build uses Avalonia’s X11 backend for overlays, capture, and window tracking.

## Journal files not found

Override the journal directory with the environment variable or CLI flag:

```bash
export SRVSURVEY_JOURNAL_DIR="/path/to/your/journals"
# or
./SrvSurvey.Desktop --journal-directory "/path/to/your/journals"
```

## Still stuck?

Open an issue at https://github.com/Fenris159/SrvSurvey/issues and include:

- Distribution and desktop environment (e.g. Fedora 42 + KDE Plasma 6)
- Output of:

  ```bash
  printf 'session=%s\nDISPLAY=%s\nWAYLAND_DISPLAY=%s\n' \
      "${XDG_SESSION_TYPE:-unset}" \
      "${DISPLAY:-unset}" \
      "${WAYLAND_DISPLAY:-unset}"
  ```
- Whether you are using the AppImage or the portable `.tar.gz`
- Any relevant window-rule screenshots (especially on Plasma)
