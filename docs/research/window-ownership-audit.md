# Desktop window ownership audit

Date checked: 2026-10-15. Baseline: RC64.9 (`6ff95c69`).

The audit covers desktop window creation, repeated opening, visibility restoration,
portal permission/configuration dialogs, and native file/folder pickers.

| Surface | Ownership and result |
| --- | --- |
| Main window and pre-startup duplicate-instance prompt | Root windows assigned to the application lifetime. The startup prompt precedes the main window, so it has no application owner. |
| Route, journey, system notes, biology codex/bingo/predictions, colonization preview, and error report | Coordinators use `Show(owner)`. |
| Boxel library/statistics/information and overlay category settings | Use their invoking window as owner. |
| Spansh search and Boxel help | Owned by the route window and statistics window respectively. |
| Network integration, firegroup deletion, Boxel save, release notes, overlay exceptions, runtime duplicate-instance and Wayland guidance dialogs | Modal `ShowDialog(owner)` calls already supply the invoking window. |
| File, folder, import and export pickers | Obtain `StorageProvider` from the invoking top level. Avalonia's X11 backend supplies that window's portal identifier. |
| Wayland capture source picker | Previously sent an empty `parent_window`. Now resolves the active application window, falling back to the visible main window, immediately before the portal request. |
| Shortcut approval and version-two shortcut configuration | Had the same empty-parent gap. Both now use the shared portal parent resolver. Silent shortcut restoration still cannot open a permission dialog. |
| Overlay position editor and draggable previews | Independent game-facing windows, governed by overlay window policy rather than the main configuration window. Parenting them to the main window would couple their stacking/minimization to configuration UI. |
| Mining calibration and its tools | Calibration is now owned by the editor; its tools were already owned by the calibration window. |
| Map zoom control | Already owned by the map when first shown. The registry now preserves that relationship across suppression/restoration. |
| Runtime, combined, alignment and Guardian overlay surfaces | Independent game-facing presentations with explicit native management and visibility lifecycles. |

## Portal contract and threading

The currently shipped Avalonia Desktop backend uses X11, including XWayland on a
Wayland desktop. Its native handle is an `XID`; the resolver formats it as
`x11:<hexadecimal id>`. Native window access runs on the Avalonia dispatcher.
Portal I/O and capture cleanup continue on workers.

A raw native Wayland surface pointer is not an exported `xdg_foreign` handle.
Unsupported or unavailable handles use the portal's documented empty-parent
fallback. Adding a native Wayland backend would require an exported-handle lease
that remains alive until the portal response finishes.

References: [portal window identifiers](https://flatpak.github.io/xdg-desktop-portal/docs/window-identifiers.html),
[Avalonia 12.1.3 X11 backend](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.X11/X11Window.cs),
[Avalonia window hide/show ownership](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.Controls/Window.cs).

## Verification boundary

The isolated Xvfb/D-Bus probe creates an actual native window and compares its
identifier with the `ScreenCast.Start` request sent by the production portal
client. The released RC64.9 binary fails that assertion: expected `x11:20000f`,
received an empty string. This verifies the missing association; it does not
simulate Plasma's final stacking/activation policy.

Headless tests cover dispatcher access, active-window selection, missing/hidden
owners, cancellation, shortcut parent forwarding, calibration ownership and
repeated overlay restoration in either registration order. Validation on the
reporter's CachyOS/Plasma session is still needed to confirm compositor behavior.
