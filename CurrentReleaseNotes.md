# SrvSurvey-XP 2.1.3.0-rc.65

RC65 makes routes easier to build, edit and repeat. Fleet Carrier routes now
follow your selected personal or squadron carrier, use available fuel snapshots,
and warn when more tritium is needed for the remaining journey.

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
