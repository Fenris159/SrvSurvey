# Calibrate VR overlay panels

Open **Theme > Overlay setup > VR**, connect your headset route, and select
**Adjust overlays** (or use your configured **Adjust VR overlays** shortcut).
Enable global keyboard input in **Settings > Input** to use VR shortcuts.

The selected panel appears with sample data, even when it is hidden by the
current game state. This preview is VR-only: it does not change desktop panel
visibility or require a jump, landing, or biology scan. Closing calibration
removes the samples and restores normal live VR panels.

## Controls

The desktop editor and **VR overlay calibration** panel in the headset share
the same controls. Enable **Toggle VR overlay interaction**, release held
controller buttons, then point and click. Interaction remains an explicit
toggle: closing calibration does not turn off interaction for other panels.

Select the target overlay and **Default** or a vehicle/game mode. In the headset,
use the left/right buttons to cycle targets and modes; precision choices stay
inline, so no desktop popup needs to be captured. Fields wrap
into additional rows on narrow windows and keep readable numeric boxes.

| Control | Behavior |
| --- | --- |
| Scale | Panel width in 10 cm units; 8 is 80 cm wide. |
| Position X / Y / Z | 10 cm units: positive X is right, Y is up, Z is forward. Normal arrows move 0.5 units, or 5 cm. |
| Pitch / yaw / roll | Degrees. Normal arrows rotate 1°. |
| Fine / Normal / Coarse | Fine uses one tenth of normal steps (5 mm / 0.1°); coarse uses five times normal (25 cm / 5°). |
| Shift / Ctrl | Temporarily use one tenth / five times the selected step size for mouse arrows, keyboard arrows, or wheel input. Shift takes precedence. |
| Mouse wheel | Scroll up/down over a number to increase/decrease it, without typing. Controller thumbstick scrolling uses the selected step size too. |
| Place in front of me | Put only the selected panel at eye level, one metre along the current head heading. Keep its width up to 80 cm, clear pitch/roll, and face the pilot. Other panels and the shared origin stay in place. Requires a fresh tracked pose. Save to keep it. |
| Save calibration | Save the selected panel and mode. Existing files receive verified backups. |
| Reset selected | Restore and save the route's factory placement for Default, or the panel's Default placement for a vehicle/game mode. |
| Close / discard unsaved | Close both editors and discard unsaved changes. Earlier saves remain saved. |

Selecting another target or mode discards unsaved changes to the previous
selection. Save each placement before moving on. The sample identifies the
selected panel; the calibration control panel is a separate surface below it.
If the runtime reaches its panel limit, the controls and selected sample take
priority. Temporarily omitted live panels return when calibration closes.

**Reset VR orientation** recenters the whole arrangement using the selected
backend's existing origin behavior. Use **Place in front of me** to recover a
single panel without moving the others. For VDXR, install the matching native
OpenXR layer and restart Elite to enable current-head placement; older layers
can still render but do not supply the required pose.

## Seated VDXR defaults and existing settings

The Quest 3 report in [#264](https://github.com/Fenris159/SrvSurvey/issues/264)
confirmed that SteamVR's floor-origin defaults are too high and large when
used with VDXR's head-origin placement. VDXR now uses these seated starting
points for unmodified factory calibrations:

| Panel | Scale | Position | Rotation |
| --- | --- | --- | --- |
| Jump information | 8 | 0, 4.5, 11 | -20, 0, 0 |
| Biology status | 5 | 0, 3.5, 10 | -15, 0, 0 |
| System status | 3.5 | -5.5, -3, 9 | 15, -25, 0 |
| Biology system | 5 | -6.5, 0.5, 9 | 0, -35, 0 |
| Other panels | 5 | 0, 0, 10 | 0, 0, 0 |

Existing custom shared placements are retained as a starting point. Use
**Reset selected** in Default mode to adopt a seated factory placement.
VDXR saves go into the profile's `vr/vdxr/Default.json` and vehicle/game-mode
files in the same folder. SteamVR continues to use `plotters.json` and `vr/`
mode overrides. Changing VDXR placement does not rewrite SteamVR or desktop
placement. Backups are kept beside the relevant saved calibration files.

## Controller availability

Quest 3 + EDVR + VDXR **rendering** was reported working; the reporter did not
test controller input. There is now an initial **Experimental EDVR controller
input** option in **Theme > Overlay setup > VR**, off by default. It supplies
controller actions from the frame loop for recognized EDVR sessions that do not
attach or synchronize their own actions.

To try it, install and register the matching complete SrvSurvey package, enable
the experimental option on the **Meta Quest — VDXR** route, and restart Elite.
Then release held controller buttons and use **Toggle VR overlay interaction**.
A successful activation reports **Experimental EDVR controller interaction is
enabled**. The pointer operates this calibration panel and existing live panel
controls. Turning pointer interaction off deactivates its controller actions.

The existing application-driven input path is retained when the game already
attaches actions. If EDVR or another layer later attempts its own attachment
after the experimental path has attached, OpenXR cannot undo the first
attachment. SrvSurvey stops its own capture and forwards the runtime error;
disable the experimental option and restart Elite to recover. Keep the option
off for other game integrations. See [the investigation](research/edvr-controller-input.md)
for the source evidence and API constraints.

This path passes fake-runtime tests, but real Quest 3 controller support and
coexistence with the reporter's OpenKneeboard / COVAS layers remain unverified.
If input is unavailable, record the displayed OpenXR error and exact package,
EDVR, VDXR, Streamer, and other-layer versions. You can still mirror the
SrvSurvey window into the headset, such as an OpenKneeboard window-capture tab,
and operate the same controls with the mouse.

## Completion and live acceptance

[#264](https://github.com/Fenris159/SrvSurvey/issues/264) and
[#265](https://github.com/Fenris159/SrvSurvey/issues/265) are the source of these
acceptance criteria. Automated checks cover defaults, saving and route
isolation, sample lifecycle, control input, narrow layouts, and pose placement.
The implementation addresses both reports. The following real-headset checks
remain for feedback on this release, especially the experimental EDVR input path:

- [ ] Repeat the reported Quest 3 / EDVR / VDXR rendering setup and record the
  exact package, EDVR, Streamer, and VDXR versions.
- [ ] With no matching gameplay state, select each target and place its sample;
  check that switching targets and closing removes samples immediately.
- [ ] Check all seven fields with a 1080 px portrait desktop and navigation
  expanded, then use normal/fine/coarse steps and wheel input in a mirrored view.
- [ ] Enable Experimental EDVR controller input, restart Elite, and try the
  controller-interaction shortcut. Record the exact success/waiting/error state.
  Repeat with OpenKneeboard / COVAS layers present, then with the option off to
  verify rendering remains available and independent input is inactive.
- [ ] On a route with working controller input, complete selection, placement,
  save, and close from inside the headset. Check held-button cancellation.
- [ ] Turn and translate the head, then place a selected panel in front of you;
  check the one-metre starting distance and that other panels do not move.
- [ ] Save, restart, and switch between VDXR and SteamVR; verify independent
  placement and vehicle-mode overrides, plus the seated VDXR reset defaults.

See [VDXR setup and reported compatibility](vr-vdxr.md) for installation and
the original tested configuration.
