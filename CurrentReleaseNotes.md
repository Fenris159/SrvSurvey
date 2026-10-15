# SrvSurvey-XP 2.1.3.0-rc.64.7

RC.64.7 brings Spansh route planning into SrvSurvey. Search from the ship or
Fleet Carrier Route Workspace, load the result straight into the itinerary,
and keep following it across closes and restarts. Updated EDSY module data
also lets the Galaxy Plotter use the ship you are currently piloting.

## What's changed

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
  unfunded jump. Turning the option off restores the original Spansh values.
- **See estimated carrier travel time.** The main window, FC Route Workspace,
  overlay panel and overlay preview show **ETR** at 20 minutes per remaining
  jump. The estimate refreshes as jumps are completed, omits zero-valued time
  units and disappears when no jumps remain.

## Update channel and packages

RC.64.7 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64.7`
- Tag: `xp2-v2.1.3.0-rc.64.7`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64.7-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64.7-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64.7-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64.7-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Galaxy Plotter needs a complete, confirmed
Loadout for the current ship; incomplete or unverifiable ship data requires
a fresh Loadout or a manually supplied build. FC fuel reinterpretation uses
Spansh's reported jump costs and does not recalculate mass-dependent fuel costs
or assume future mining. Carrier ETR is a jump-based estimate rather than a
live countdown.
