# Steam Deck Gaming Mode

This guide targets **native Linux SrvSurvey-XP with Elite Dangerous running
through Steam/Proton in Gaming Mode**. Use a build from PR #207, or a release
that explicitly includes its SteamOS changes. An older Linux release does not
gain external-overlay support by following these steps.

The implementation has passed private Gamescope compositor tests. Actual Deck
Steam-client behavior, touch/trackpad/controller routing, docking, and SteamOS
version differences still need the device checks below.

## Install and configure in Desktop Mode

1. Follow [Linux installation](INSTALL_LINUX.md) to put the Linux x86-64
   AppImage in a writable folder under your home directory. Keep its stable
   name, such as `~/Applications/SrvSurvey/SrvSurvey.AppImage`. If FUSE is
   unavailable, use the documented extract-and-run option or portable archive.
   Neither package needs a separate .NET installation.
2. Start SrvSurvey in Desktop Mode. Confirm that it finds your Elite journal
   folder and commander. Configure the overlay panels and positions there.
   Desktop configuration does not prove Gaming Mode composition.
3. In **Settings → Input**, assign **Toggle live overlay interaction** to a
   keyboard shortcut that does not conflict with Elite. Its default is
   **Alt+Shift+O**. Record the shortcut for your Elite Steam Input layout.
4. Add the AppImage as a non-Steam game in the desktop Steam client. Set the
   shortcut's working directory to the folder containing the AppImage. For a
   portable installation, target `SrvSurvey.Desktop` in the extracted folder.
   Run SrvSurvey as a native Linux application: leave forced Proton compatibility
   off for this shortcut. Elite keeps its own Proton configuration.

Valve documents adding applications from Desktop Mode and exposes keyboard,
mouse, and controller bindings through Steam Input in its
[Steam Deck FAQ](https://partner.steamgames.com/doc/steamhardware/steamdeck/faq).
The native shortcut above is the SrvSurvey test configuration; no custom
Gamescope build or nested Gamescope launch option is needed for Gaming Mode.

## Start the two applications in Gaming Mode

1. Return to Gaming Mode and turn the Deck **performance overlay off**. It
   competes with SrvSurvey for Gamescope's single external-overlay slot.
2. Launch the native SrvSurvey shortcut and leave it running.
3. Open Steam, select Elite Dangerous, and start or return to the game. Keep
   SrvSurvey running while switching back to Elite. If you already use
   mini-ed-launcher's additional-process support, it can start the native
   AppImage instead; that launcher is optional.
4. Once Elite is in a playable scene, verify a journal-driven panel. Passive
   panels must leave normal game input available.

SrvSurvey verifies the primary Gamescope XWayland display before rendering and
discovers Elite's game display separately. Do not hard-code `DISPLAY=:0`, copy a
Desktop Mode display into the shortcut, or add another nested Gamescope around
the pair. A display number alone does not establish compositor ownership.

For initial input testing, use a keyboard and mouse. Then map a spare button in
**Elite's** Steam Input layout to the configured SrvSurvey keyboard shortcut.
Test a trackpad configured as a mouse with a left-click binding separately.
These mappings require Deck validation; controller text entry and touch behavior
are not established by the desktop smoke tests.

The live-interaction shortcut reserves pointer input across the whole HUD
canvas, including blank areas. Click or drag a panel, then press the shortcut
again to save placement and return pointer input to Elite. Keyboard input stays
with Elite. Close Steam menus before first enabling live interaction. Steam
and Quick Access Menu input requests should temporarily take priority and live
interaction should resume after they close.

## Capture a device snapshot

Keep the Deck in Gaming Mode with both applications running. Run the collector
from an existing same-user terminal or SSH session on the Deck; switching to
Desktop Mode first would collect the wrong compositor. The script discovers
local displays from same-user Steam, Gamescope, Elite, and SrvSurvey processes,
so the SSH session does not need an inherited `DISPLAY`.

Copy [Collect-SteamOSDiagnostics.py](../scripts/Collect-SteamOSDiagnostics.py)
from this branch to the Deck, then run it with Python 3:

```sh
python3 Collect-SteamOSDiagnostics.py --output steamos-passive.json
```

The collector records OS/kernel versions, selected session variables, relevant
process IDs, and Gamescope root properties and geometry. It runs `gamescope
--version`, `xprop`, and `xwininfo` with five-second command timeouts. The version
is for `gamescope` on `PATH`; report separately if a custom compositor is used.
Unavailable tools or inaccessible displays appear as errors in the JSON; the
collector does not install packages or require unlocking SteamOS. It creates
the output file with owner-only access and refuses to overwrite an earlier
snapshot.

It does not capture screenshots, input, journals, application logs, full launch
arguments, or full process environments, and uploads nothing. Run it again
with a new output filename in each state being investigated. An explicit
`--display :7` can add a **known local** display if process discovery is restricted;
it does not bypass X11 authentication.

Pair snapshots with the relevant SrvSurvey log excerpts. The normal Linux log
folder is `~/.local/share/SrvSurvey/logs` (`$XDG_DATA_HOME/SrvSurvey/logs` when
overridden). Look for `Gamescope overlay display:`, `Overlay presentation:`,
registration errors, and `Gamescope scaling state is unknown`. Review log
excerpts before sharing because application logs can identify your commander.

## Device acceptance checks

Record the tested SrvSurvey commit, SteamOS version, Proton version, Deck model,
resolution, scaling mode, and whether an external display is connected. A
diagnostic JSON file is evidence of state, not a test pass by itself.

| Check | Expected result |
| --- | --- |
| Start SrvSurvey before Elite, then reverse the order | Panels appear after the game starts and returns to focus. |
| Passive HUD | Gameplay keyboard and pointer input remain available. |
| Enable live interaction; click and drag a panel | The HUD receives clicks; Elite keeps keyboard input. |
| Click a blank area in live mode | The game does not receive the click. |
| Disable live interaction | Placement persists and pointer input returns to Elite. |
| Open and close Steam and Quick Access menus in live mode | Menus receive input; the HUD resumes after they close. |
| Repeat using Deck trackpad, touchscreen, and controller shortcut | Record each input device separately, including any stuck press or lost release. |
| Change resolution and scaling; dock and undock | Panels follow the supported viewport without covering the wrong area. |
| Exit and restart Elite while SrvSurvey stays open | Panels hide and reconnect to the new game process. |
| Suspend and resume | Record recovery, input routing, and any need to restart SrvSurvey. |

Custom maximum/per-window scaling remains unsupported. If the log reports
unknown scaling state, select the scaling mode again in Steam and collect a
second snapshot. Image-based features such as FSS tuning cannot capture the
composited game from the primary X11 root; use only a separately supported
screen-sharing path. Automatic Galaxy Map text entry is unavailable in this
mode because its text injector cannot target the separate game display. Use
Elite's own text entry instead. Keep the performance HUD off during acceptance
testing.

See [overlay troubleshooting](Overlay_Troubleshooting.md#steamos-gaming-mode)
for the rendering/input contract and
[Gamescope smoke tests](../tests/SrvSurvey.GamescopeSmoke/README.md) for the
reproducible compositor checks that precede device testing.
