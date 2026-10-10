# SteamOS Gaming Mode overlay pathways

Date checked: 2026-10-10

## Scope and evidence

Reported scenario: native Linux SrvSurvey and Elite Dangerous run in Steam Deck
Gaming Mode. The SrvSurvey AppImage starts through mini-ed-launcher's configured
`processes` setting. The original Windows SrvSurvey reportedly works
through Proton. No Deck logs, installed Gamescope version, launch environment,
or live reproduction were available.
The findings below identify upstream requirements and implementation limits;
they do not establish which requirement failed on that device.

Source inspection is pinned to Gamescope
[3.16.29, `8f212644`](https://github.com/ValveSoftware/gamescope/tree/8f212644c46460549035971ab914421180f61a7c),
with the core display/overlay behavior cross-checked against
[3.15.14, `b2505fe4`](https://github.com/ValveSoftware/gamescope/tree/b2505fe4dac917aadd1ab05473d863830a7b5859).
These are upstream snapshots, not a claim about the Deck's installed build.

SrvSurvey revision inspected: `f4ab61d4274b2bd2a15c859b5b72df0674321dbb`.
The initial investigation established upstream integration contracts. The
implementation and executable validation addendum records the subsequent code
changes and isolated Gamescope tests. The exact incident's cause and a fix on
the affected Deck remain unverified.

## Recommended direction

Reuse the existing combined overlay renderer, with a Gamescope-specific
platform adapter. That adapter must verify and use the primary XWayland
display for the passive canvas, set its external-overlay role, and obtain
Elite's window/focus from the game display independently. Current automatic
combined-window selection only changes the number of windows; it does not
implement these compositor requirements. Support for live editing and the
performance HUD needs separate validation.

## What the supplied mini-ed-launcher configuration does

The reported entry is:

```json
"processes": [
  { "fileName": "/home/USERNAME/AppImages/srvsurvey.appimage" }
]
```

The entry starts an executable; it does not register a Gamescope overlay.
At inspected launcher revision
[`84d080a3`](https://github.com/rfvgyhn/min-ed-launcher/tree/84d080a3b8b4728a50ae84079d5a6656ee7f943d),
the launcher creates a normal ProcessStartInfo with shell execution disabled,
redirects output, and does not set or clear DISPLAY for these extra processes.
The child therefore normally inherits the launcher's environment
([configuration mapping](https://github.com/rfvgyhn/min-ed-launcher/blob/84d080a3b8b4728a50ae84079d5a6656ee7f943d/src/MinEdLauncher/Settings.fs#L369-L391);
[process start](https://github.com/rfvgyhn/min-ed-launcher/blob/84d080a3b8b4728a50ae84079d5a6656ee7f943d/src/MinEdLauncher/Process.fs#L8-L28)).

**Inference:** a launcher started in Elite's Steam game environment can put
the AppImage on that game's XWayland server. This remains possible even though
SrvSurvey and Elite share a launch chain. The actual DISPLAY values and
server IDs must be captured; the JSON alone cannot identify them.

Extra processes start before the game. SrvSurvey must keep discovering Elite
when its window appears and reconnect when the game restarts, rather than
requiring Elite to exist at SrvSurvey startup
([launch ordering](https://github.com/rfvgyhn/min-ed-launcher/blob/84d080a3b8b4728a50ae84079d5a6656ee7f943d/src/MinEdLauncher/App.fs#L317-L343)).
The launcher documents separate library-path handling for companion processes
on Steam Deck. Investigate that only if the AppImage fails to start or load
native dependencies; it does not supply overlay composition
([launcher runtime guidance](https://github.com/rfvgyhn/min-ed-launcher/blob/84d080a3b8b4728a50ae84079d5a6656ee7f943d/README.md#L257-L264)).

## The display connection is part of the overlay backend

Gamescope runs both as a standalone compositor and nested inside an X11 or
Wayland desktop; the nested game has its own XWayland desktop. A desktop
always-on-top window outside that compositor does not automatically enter its
composition. Steam Deck Gaming Mode and Desktop Mode therefore need separate
capability decisions ([Gamescope README](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/README.md#L1-L17);
[Valve Deck FAQ](https://partner.steamgames.com/doc/steamhardware/steamdeck/faq?l=english)).

Gamescope supplies its child Steam process with `DISPLAY` for XWayland server
**0**. With multiple servers, `STEAM_GAME_DISPLAY_0`, `_1`, etc. name servers
**1**, **2**, etc.; `GAMESCOPE_WAYLAND_DISPLAY` names the compositor's Wayland
socket. With one server, Steam and the game share it. Numeric display names are
runtime assignments, so server 0 must not be hardcoded as `:0`
([environment setup](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/main.cpp#L1074-L1096)).

**Source-level requirement:** global external X11 overlays are selected from
server 0's window list. If mini-ed-launcher gives SrvSurvey Elite's game-server
environment, correctly tagging its window still does not make it the global
overlay. That launch inheritance must be verified. Use a verified server-0
connection for overlay UI, while
tracking and keyboard observation can require Elite's distinct game connection
([older global selection](https://github.com/ValveSoftware/gamescope/blob/b2505fe4dac917aadd1ab05473d863830a7b5859/src/steamcompmgr.cpp#L3728-L3755)).

## Recognition, transparency, and input

An upstream Gamescope collaborator explicitly recommends an ordinary rendered
X11 window carrying `GAMESCOPE_EXTERNAL_OVERLAY`
([maintainer guidance](https://github.com/ValveSoftware/gamescope/issues/288#issuecomment-1709492215)).
Mangoapp provides an actual implementation: a transparent GLFW framebuffer and
a window property with value `1` ([window tagging](https://github.com/flightlessmango/MangoHud/blob/e5d31119b521775de01ec41154c3ddb0a4c8c0cb/src/app/main.cpp#L363-L383);
[transparent framebuffer](https://github.com/flightlessmango/MangoHud/blob/e5d31119b521775de01ec41154c3ddb0a4c8c0cb/src/app/main.cpp#L449-L460)).

`_NET_WM_WINDOW_TYPE_UTILITY`, raising, or ordinary topmost state is not this
overlay role. Gamescope treats role-tagged windows separately from its base
game candidates; external overlays are excluded from those candidates.
`STEAM_OVERLAY` identifies Steam's interactive overlay, which should not be
appropriated merely to make a passive HUD visible
([role definitions](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/steamcompmgr.cpp#L1538-L1543);
[focus filtering](https://github.com/ValveSoftware/gamescope/blob/b2505fe4dac917aadd1ab05473d863830a7b5859/src/steamcompmgr.cpp#L3356-L3370)).

External overlays are drawn with `NoScale`: composition uses output dimensions
rather than the game surface dimensions. Use an output-sized transparent canvas
and map game positions into that space, accounting for scaling, letterboxing,
rotation, docking, and Avalonia DPI. Do not assume Elite's render resolution,
server-0 root size, and physical output always coincide. Mangoapp likewise
resizes its canvas to its screen dimensions
([paint path](https://github.com/ValveSoftware/gamescope/blob/b2505fe4dac917aadd1ab05473d863830a7b5859/src/steamcompmgr.cpp#L1919-L1935);
[Mangoapp canvas](https://github.com/flightlessmango/MangoHud/blob/e5d31119b521775de01ec41154c3ddb0a4c8c0cb/src/app/main.cpp#L431-L436)).

External overlay recognition supplies visibility, not arbitrary interactive
regions. Gamescope's global input selection grants overlay focus to
`STEAM_OVERLAY` with `STEAM_INPUT_FOCUS`; otherwise it targets the game/override.
Mode `2` retains game keyboard focus while redirecting pointer focus. XShape
input regions cannot by themselves make server-0 external overlay controls
reachable while the compositor routes input to a different game server
([input selection](https://github.com/ValveSoftware/gamescope/blob/b2505fe4dac917aadd1ab05473d863830a7b5859/src/steamcompmgr.cpp#L3742-L3784)).

## Detect capabilities and verify the selected server

Environment hints and `/etc/os-release` can help diagnostics, but connecting to
the actual display and inspecting its root distinguishes the working pathway.
Recommended probes are derived from source, not a documented stable public API:

| Probe | Interpretation |
| --- | --- |
| `GAMESCOPE_XWAYLAND_SERVER_ID` | Present CARDINAL/32 identifies the Gamescope server; value `0` is the global overlay server. |
| `_NET_SUPPORTING_WM_CHECK` | Follow and verify its self-reference; the WM window is named `steamcompmgr`, not `gamescope`. |
| `GAMESCOPE_PID` | Optional newer-build corroboration; verify the same-user compositor process. It is absent from the inspected older snapshot. |
| `GAMESCOPE_FOCUSABLE_WINDOWS` | Server-0 list of `[window, appID, pid]` triplets; corroborate game identity. |
| `GAMESCOPE_FOCUSED_APP_GFX` | Underlying displayed game app; distinct from input-focused `GAMESCOPE_FOCUSED_APP`. |
| `GAMESCOPE_FOCUSED_WINDOW` and `GAMESCOPE_FOCUS_DISPLAY` | Global selected window and its display; window IDs alone are not unique across servers. |

Sources: [WM identification](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/steamcompmgr.cpp#L7828-L7860),
[server ID/PID publication](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/steamcompmgr.cpp#L9031-L9041),
[focusable triplets](https://github.com/ValveSoftware/gamescope/blob/b2505fe4dac917aadd1ab05473d863830a7b5859/src/steamcompmgr.cpp#L3690-L3726),
[global focus feedback](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/steamcompmgr.cpp#L5420-L5470).

One implementation trap: the focus-display properties are written as
CARDINAL/32 from NUL-terminated display bytes, rather than ordinary STRING/8.
Read their upstream representation deliberately; an ordinary string-property
reader cannot be assumed correct.

Recommended discovery: obtain candidate Steam session displays from verified
same-user Steam processes, then require Gamescope's server-0 root evidence;
retain Elite's independently verified display. Log only relevant environment
keys and probe results. Select the UI display before Avalonia initializes its
platform connection. Keep ordinary settings/main windows separate in behavior
from the passive canvas so opening one does not accidentally promote it as the
base game. These are application design recommendations, not upstream launch
guarantees.

## Performance HUD conflict and native Wayland limitations

The inspected versions hold one external overlay selection, rather than
compositing every tagged window. Older selection chooses greatest opacity;
3.16.29 first prefers a connector's matching Mangoapp message tag, then falls
back to an untagged external window
([older selection](https://github.com/ValveSoftware/gamescope/blob/b2505fe4dac917aadd1ab05473d863830a7b5859/src/steamcompmgr.cpp#L3408-L3415);
[newer Mangoapp selection](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/steamcompmgr.cpp#L5171-L5196)).
Consequently, one combined SrvSurvey canvas avoids self-competition but can
still compete with the Deck performance HUD. Do not claim coexistence without
testing every HUD level; do not invent or reuse Mangoapp message tags to force
priority.

A native Wayland route also exists: Gamescope advertises
`zwlr_layer_shell_v1` version 4 and marks layer-shell surfaces as external
overlays. Ordinary `xdg_toplevel` surfaces are not automatically external
overlays. However, this route uses the same external selection and is fallback
when an X11 external overlay already exists; it does **not** solve performance
HUD coexistence. Registry-probe the actual Gamescope socket and validate
geometry/input instead of treating protocol availability as complete support
([creation/classification](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/wlserver.cpp#L1997-L2083);
[registry creation](https://github.com/ValveSoftware/gamescope/blob/8f212644c46460549035971ab914421180f61a7c/src/wlserver.cpp#L2272-L2283)).

Layer-shell's protocol specifies keyboard interactivity and pointer/touch input
regions, but those semantics alone do not prove Gamescope implements the desired
interactive behavior. For a passive native surface, commit an **empty** input
region; `set_input_region(NULL)` restores an infinite input region
([layer-shell specification](https://github.com/swaywm/wlr-protocols/blob/master/unstable/wlr-layer-shell-unstable-v1.xml);
[Wayland input-region specification](https://wayland.freedesktop.org/docs/html/apa.html#protocol-spec-wl_surface-request-set_input_region)).

## What Proton proves, and what remains unknown

Proton uses Wine to implement Windows functionality on Linux; its launcher
sets `WINEPREFIX` to `STEAM_COMPAT_DATA_PATH/pfx/`
([Proton introduction](https://github.com/ValveSoftware/Proton/blob/e91ca2be0df2cef4c230cbbc0b86604d73a0bbf6/README.md#L1-L10);
[prefix construction](https://github.com/ValveSoftware/Proton/blob/e91ca2be0df2cef4c230cbbc0b86604d73a0bbf6/proton#L585-L592);
[session setup](https://github.com/ValveSoftware/Proton/blob/e91ca2be0df2cef4c230cbbc0b86604d73a0bbf6/proton#L1600-L1602)).
Wine's X11 driver maps transparent Windows styles to input shapes and topmost
styles to X11 window state. Its inspected generic window path does not
automatically add `GAMESCOPE_EXTERNAL_OVERLAY` for every Windows overlay
([Wine input shapes](https://github.com/ValveSoftware/wine/blob/981e2f0b9007fd70a6e0ed6119cb18f4164e26f9/dlls/winex11.drv/window.c#L590-L632);
[topmost translation](https://github.com/ValveSoftware/wine/blob/981e2f0b9007fd70a6e0ed6119cb18f4164e26f9/dlls/winex11.drv/window.c#L1670-L1694)).
The reported Windows success could involve a shared game display, Wine popup
ownership, common app identity, or specific Proton behavior. Those are hypotheses
until its launch configuration/window properties are captured. A shared prefix
does not itself prove a shared display, and native overlays do not require Proton
if the compositor pathway is correctly implemented.

## Missing contracts in this repository

These are findings from the inspected source, not inferred device logs.

| Area | Current implementation | Missing Gaming Mode behavior |
| --- | --- | --- |
| Backend selection | [OverlayPlatformCapabilities](../../src/SrvSurvey.Desktop/Platform/Overlay/OverlayPlatformCapabilities.cs) classifies X11/XWayland using environment variables and advertises topmost/transparency for both. | Verify which compositor and server actually host the window; distinguish ordinary desktop capabilities from Gamescope overlay composition and input capabilities. |
| Presentation selection | [OverlayPresentationModeSelector](../../src/SrvSurvey.Desktop/Platform/Overlay/OverlayPresentationMode.cs) recognizes Gamescope environment/desktop hints and chooses CombinedWindow. | A combined canvas is useful, but the decision does not verify server 0 or provide an external-overlay backend. |
| Native preparation | [X11OverlayPlatformService](../../src/SrvSurvey.Desktop/Platform/Overlay/X11OverlayPlatformService.cs) opens the current DISPLAY, applies utility/normal window types, and manages XShape input regions. | Set GAMESCOPE_EXTERNAL_OVERLAY=1 on the combined host on a verified primary server. No implementation of that property was found in src or tests. |
| Game tracking | [GameWindowTracker](../../src/SrvSurvey.Desktop/Platform/Overlay/GameWindowTracker.cs) initially searches the current DISPLAY. [GamescopeGameWindowTracker](../../src/SrvSurvey.Desktop/Platform/Overlay/GamescopeGameWindowTracker.cs) falls back to an explicitly published runtime bridge. | Discover Elite's verified game DISPLAY without requiring the desktop wrapper's bridge; pair display and window identity and reconcile local focus with Gamescope's global focus. |
| Visibility | [HostedOverlayWindow](../../src/SrvSurvey.Desktop/Platform/Overlay/HostedOverlayWindow.cs) normally requires an available, visible, foreground game snapshot. | An Elite window absent from SrvSurvey's server, or incorrect foreground evidence, prevents panels from being created even before compositor stacking matters. |
| Geometry | [CombinedOverlayPresentationController](../../src/SrvSurvey.Desktop/Platform/Overlay/CombinedOverlayPresentationController.cs) makes its host equal to Elite's client bounds; [CombinedOverlayProjection](../../src/SrvSurvey.Desktop/Platform/Overlay/CombinedOverlayProjection.cs) subtracts that host origin and applies Avalonia scale. | Supply the output canvas and an explicit game-to-output transform; the existing calculation does not model cross-server scaling or letterboxing. |
| Existing discovery to reuse | [EliteKeyboardDisplayDiscovery](../../src/SrvSurvey.Desktop/Input/EliteKeyboardDisplayDiscovery.cs) already checks same-user Elite processes, executable identity, and local DISPLAY values for keyboard observation. | Extract or reuse this verified evidence for game-window tracking instead of introducing unrestricted display scanning or assuming a display number. |

The existing [selector tests](../../tests/SrvSurvey.Desktop.Tests/Platform/OverlayPresentationModeSelectorTests.cs)
cover environment-based combined selection, and the
[bridge tests](../../tests/SrvSurvey.Desktop.Tests/Platform/GamescopeGameWindowTrackerTests.cs)
cover marker validation and transient X server recovery. They do not establish
server-0 registration, cross-server compositor visibility, or Deck HUD
coexistence.

The desktop [CachyOS workflow](../CACHYOS_GAMESCOPE.md) and
[Wayland bridge](../Overlay_Troubleshooting.md#srvsurvey-on-the-desktop-with-native-wayland-gamescope)
address desktop/nested Gamescope setups. Their same-session wrapper and
monitor bridge should not be presented as verified Steam Deck Gaming Mode
support.

## Proposed implementation sequence

1. **Resolve the session before UI startup.** Add one session detector shared
   by presentation and tracking. Treat environment values as candidate hints.
   Probe the current X display; discover a primary-display candidate through
   verified same-user Steam/session metadata and validate server ID 0 and WM
   identity. Verify that the primary and game displays belong to the intended
   compositor, using PID evidence where available. Retain Elite's display
   separately. If authentication or a container boundary prevents access,
   report that specific limitation instead of claiming overlay support.
   Resolve Avalonia's UI display before its platform connection is created in
   [Program](../../src/SrvSurvey.Desktop/Program.cs). Opening a second Xlib
   connection alone cannot move Avalonia windows to another server.
2. **Register one passive host.** Extend native preparation through a Gamescope
   adapter; publish the external-overlay role on the combined host before its
   first map and synchronize it with Avalonia's separate connection. Preserve
   alpha and passive input regions. Keep source panel windows suppressed.
   Ordinary desktop backends continue to use their existing preparation.
   Give the main window a deliberate show/hide lifecycle so it cannot
   inadvertently replace Elite as the base application.
3. **Track display, identity, focus, and coordinates together.** Open a
   separate game tracker on Elite's verified display, including discovery
   after launch and recovery after restart. Check global graphics/input
   focus alongside the local game snapshot so a game server's stale local
   focus cannot keep panels over another selected app. Define behavior when
   Steam menus cover Elite. Use an output-sized host with a tested mapping
   from game-relative placements to output pixels.
4. **Validate passive support before advertising interactive support.**
   First establish visibility and game input with the performance HUD off.
   Then test HUD coexistence, Steam menus, settings, and editing. External
   overlay visibility does not establish live dragging or controller focus.
   A first implementation can expose passive Gaming Mode overlays and keep
   editing in a separately validated settings surface. Multiple SrvSurvey
   commander processes also compete for the external slot; decide whether
   one presenter must aggregate their panels.

Changing only SRVSURVEY_OVERLAY_HOST to combined, detecting SteamOS by distro
name, setting the role on Elite's non-primary server, or using a portal screen
share does not implement the complete pathway. ScreenCast/PipeWire remains a
separate capture capability for image-based features.

Automated regression coverage should exercise missing/stale environment hints,
server IDs 0 and nonzero, unavailable primary display, game creation/restart,
display-qualified focus, registration restricted to the host, and geometry
transforms. A live two-server Gamescope/Deck check must establish actual
composition, alpha, and input behavior. No such hardware verification was
performed in this research pass.

## Validation checklist

- Record SteamOS build/channel, Gamescope version, launch method, and selected
  environment keys for Steam, Elite, and SrvSurvey.
- Verify UI server ID `0`; verify Elite's separate display, PID, app ID, selected
  window, and global graphics/input focus.
- First prove a transparent passive canvas with the performance HUD disabled;
  confirm alpha and that game mouse, touch, and keyboard input remains usable.
- Exercise several SrvSurvey panels together, then all Deck performance HUD
  levels, Steam overlay/QAM opening, application switching, and game restart.
- Check 1280×800, lower game resolutions/FSR, letterboxing, DPI scaling, docking,
  and output changes; distinguish game coordinates from overlay output pixels.
- Test settings/main windows separately from the canvas, including controller
  navigation, hiding/minimizing, and whether Steam selects the wrong base app.
- Compare the working Windows/Proton setup's DISPLAY, prefix, Wine version,
  X11 styles, transient ownership, and app identity with the native setup.
- Keep Desktop Mode X11, Desktop Mode Wayland, and desktop nested Gamescope
  regression cases separate; a successful desktop wrapper is not a Gaming Mode
  reproduction.

## Implementation and executable validation, 2026-10-10

The `codex/steamos-gamescope-overlays` branch implements the passive external
canvas, primary-display routing before Avalonia initialization, same-user Elite
display discovery, display-qualified graphics/input focus, native-long X11
property encoding, and output/viewport separation. A verified external-overlay
session forces one canvas even when the separate-window override is set.
Live HUD interaction is disabled; the separate position editor remains available.
Normal Steam/Proton launches do not require mini-ed-launcher. Ordinary desktop
sessions retain their native overlay path, and a nested Gamescope X11 outer
window can be mapped automatically through Elite's verified process ancestor.
The existing marker remains necessary when a native Wayland outer window has
no accessible desktop geometry.

The implementation encountered two failures in the real Avalonia smoke test:

- Gamescope configured ordinary hidden source panels to fullscreen. Their
  controls consequently filled the output canvas. Applying override-redirect
  to these suppressed source windows before mapping preserves panel sizing.
  Only the combined host receives the external-overlay property.
- Changing managed `DISPLAY` alone did not relocate Avalonia's native Xlib
  connection. The Unix [.NET environment setter](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Environment.Variables.Unix.cs#L60-L78)
  updates a managed dictionary, while the pinned
  [Avalonia X11 initialization](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.X11/X11Platform.cs#L49-L67)
  calls `XOpenDisplay(NULL)`. Routing now updates both libc and .NET before
  platform initialization. A native/managed-environment regression test and
  the inherited-game-display smoke run cover this boundary.

The reproducible [composition smoke test](../../tests/SrvSurvey.GamescopeSmoke/README.md)
uses an isolated Gamescope 3.16.29 headless session with two XWayland servers,
1280×800 output, and a 960×600 synthetic Elite window. It runs the production
Avalonia combined controller, X11 overlay service, session resolver, and game
tracker. Both the primary-start and inherited-game-display variants passed:
the latter relocated from `:3` to verified server 0 at `:2` through a synthetic
same-user Steam process. No mini-ed-launcher or real Steam client was involved.

The captured composition changed from a red game alone to a 64×64 green Avalonia
panel at (16,16), with a red game pixel outside the panel and confirmed game
graphics/input focus. These are synthetic test screenshots, not Deck captures.

| Before the SrvSurvey host | After the registered Avalonia host |
| --- | --- |
| ![Synthetic game before overlay](../images/steamos-overlays/before.png) | ![Real Avalonia panel over the synthetic game](../images/steamos-overlays/after.png) |

Hardware validation remains outstanding for the actual Steam client and Proton,
Steam/QAM transitions, Deck touch/controller input, performance-HUD conflicts,
docking/DPI changes, and compositor-version differences. The viewport calculation
uses Gamescope's default maximum scale; custom `--max-scale` and per-window
scaling overrides are not discovered. The primary-root output-size contract
applies to the multiple-XWayland SteamOS arrangement. A single-server nested
session whose render and output sizes differ needs separate validation; the
desktop outer-window path avoids depending on that contract.
Primary-root X11 game capture is explicitly unavailable in the external-overlay
session, since it would sample the Steam/overlay display rather than Elite's
composited image. Screen-based detection needs a separately supported capture
backend; capture of SrvSurvey's own controls is unaffected.

The final repository quality gate passed with 3,310 desktop tests and three
platform-specific skips. Changed production code achieved 91.4% coverage
(507 of 555 line and branch points), above the required 80%. CSharpier, all
seven localization catalogs, scoped Sonar checks, and the normal solution
build with analyzer warnings treated as errors also passed.
