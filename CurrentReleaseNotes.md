# SrvSurvey-XP 2.1.3.0-rc.65

RC65 makes routes easier to build, edit and repeat. Fleet Carrier routes now
follow your selected personal or squadron carrier, use available fuel snapshots,
and warn when more tritium is needed for the remaining journey. Linux permission
dialogs and auxiliary tools now keep their application window relationships.
The Wayland capture freeze fixes from RC64.9 are retained.

## What's changed

- **Build a route across multiple clipboard imports.** Copying more system names
  into the Route Workspace appends waypoints to the current itinerary, keeping
  the systems and progress already there.
- **Remove individual waypoints.** Every itinerary row has a red trash button
  in the last column. Remove an accidental addition directly from any route
  loaded into the Route or Fleet Carrier Route Workspace.
- **Keep a route repeating.** Both workspaces offer **Repeat from start** and
  **Repeat and reverse**. Choose one at a time. Reverse alternates the complete
  waypoint order, such as A, B, C followed by C, B, A, until the option is turned
  off. The turnaround waypoint is already reached, so the next jump is to B.
- **Repeat closed carrier loops safely.** Fleet Carrier **Repeat from start**
  is available only when the first and last waypoints are the same system.
  **Repeat and reverse** follows the existing jumps back along the route.
- **Choose which carrier a route uses.** A selector at the top of the Fleet
  Carrier Route Workspace switches between your personal fleet carrier and
  squadron carrier. Spansh Search fills its starting system, depot fuel, stored
  tritium and used capacity from the available data for that carrier. The choice
  is retained with the route.
- **Recalculate fuel for the next pass.** Carrier fuel forecasts account for
  personal and squadron carrier mass, the current load and tritium transfers.
  A repeat or reversal captures the latest available journal and Frontier
  snapshots for the new pass. Ordinary arrivals retain the current forecast.
- **See how much tritium to restock.** If recalculation finds a shortage, a
  warning shows the estimated fuel needed for the remaining route and how much
  additional tritium to mine or buy. If it exceeds free cargo space, the warning
  advises planning resupply stops.
- **Resume after resupplying.** **Refresh** updates available carrier data and
  recalculates fuel from the current waypoint. Reached waypoints, route order
  and unsaved edits are preserved.

- **Associate the Wayland source picker with SrvSurvey.** Screen sharing now
  identifies the application window that opened the picker, allowing the
  desktop to keep it above that window.
- **Associate shortcut permission and configuration dialogs with their window.**
  The desktop receives the current SrvSurvey window when these dialogs open.
- **Keep auxiliary tools attached to their owners.** Mining calibration belongs
  to the overlay editor, and map zoom controls retain their relationship to
  the map when overlays are hidden and shown again.

## Features retained from recent previews

- **Use Quest overlays directly with VDXR.** On Windows x64, select
  **Meta Quest — VDXR** in **Settings > Global overlays > VR overlays**.
  Register the bundled OpenXR layer and restart Elite using your existing
  VDXR setup. SteamVR is no longer required for this route.
- **Point and click with Quest controllers.** The existing **Toggle VR overlay
  interaction** shortcut enables a visible controller cursor. Trigger clicks,
  grip right-clicks, thumbstick press middle-clicks and thumbstick tilt scrolls.
  Either controller can operate the pointer. Turning interaction off returns
  those controls to Elite.
- **Keep VR panels positioned as intended.** Existing calibration controls
  move, rotate and scale the same styled overlay panels. Recenter captures
  headset position and heading without tilting the whole arrangement when you
  look down or tilt your head. Unchanged panel images reuse their textures instead of
  being uploaded again on every publish.
- **See which VR routes your system supports.** All routes remain visible;
  unavailable choices are grayed out with an explanation. Linux offers
  SteamVR, ALVR and custom OpenVR. VDXR requires Windows x64, and Windows Mixed
  Reality is unavailable on Windows 11 24H2 or newer.
- **Plan routes without leaving the workspace.** The new **Spansh Search**
  window offers Neutron Plotter, Road to Riches, Tourist Planner, Galaxy
  Plotter, Expressway to Exomastery and Colonisation Plotter. Fleet Carrier
  Router is available in the FC Route Workspace. Successful searches load
  directly into the appropriate itinerary.
- **Build destination lists with familiar system lookup.** System fields use
  the existing name autocomplete. Planners with multiple destinations share
  a reusable **+ Add** entry and an ordered list with edit, remove and reorder
  controls. Carrier searches also support refuelling destinations.
- **Use your current ship in Galaxy Plotter.** Updated EDSY module data and
  fitting rules cover mercenary equipment and hangar changes. SrvSurvey can
  export the equipped ship in EDSY-compatible SLEF, including its confirmed
  engineering, and automatically supply it to Galaxy Plotter. Eligible
  mercenary weapons and utilities also receive updated firegroup labels.
- **Keep working routes until you replace or clear them.** Imported and
  searched routes retain their itinerary, progress, notes and tracking
  settings across repeated workspace opens and application restarts, even
  when their selected saved file has been removed. Ship and carrier routes
  stay separate for each commander. **Save As** adds a named library copy.
- **Choose when your carrier restocks.** FC imports and searches now retain
  tank fuel, restock flags and recommended transfer amounts. The optional
  **Restock only when low** behavior transfers market tritium only when the
  tank cannot fund the next jump, respecting the 1,000 t tank limit and
  remaining stock. Rows show balances after each transfer and identify an
  unfunded jump. RC65 applies this choice to the fuel forecast for each pass.
- **See estimated carrier travel time.** The main window, FC Route Workspace,
  overlay panel and overlay preview show **ETR** at 20 minutes per remaining
  jump. The estimate refreshes as jumps are completed, omits zero-valued time
  units and disappears when no jumps remain.

## Update channel and packages

RC65 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.65`
- Tag: `xp2-v2.1.3.0-rc.65`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.65-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.65-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.65-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.65-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Fuel calculations use the latest available
observations; journal data does not provide a complete carrier hold inventory.
Enter fuel values that could not be autofilled manually in Spansh Search. Fuel
inferred between measured snapshots is marked as estimated. After buying,
mining or transferring tritium, select **Refresh** to recalculate the remaining
route.

Galaxy Plotter needs a complete, confirmed Loadout for the current ship;
incomplete or unverifiable ship data requires a fresh Loadout or a manually
supplied build. Carrier ETR remains a jump-based estimate rather than a live
countdown.

Direct VDXR requires Windows x64, Direct3D 11 and an Elite installation already
running through VDXR, such as an existing OpenComposite setup. Physical Quest
headset validation remains pending. Linux VR continues to use SteamVR/OpenVR.

Isolated tests check desktop permission requests and child-window ownership.
The reported CachyOS/Plasma stacking behavior still needs confirmation on that
desktop.
