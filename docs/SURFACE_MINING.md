# Surface mining

Available in **SrvSurvey-XP 2.1.3.0-rc.44**. The Surface mining overlay provides
Rhino rig locations, vehicle guidance, and cargo capacity during surface mining.
The same workflow is covered inside the application under **Guides > Surface mining**.

## Setup

1. Expand **Activities** in the main window and select **Mining**.
2. Use its overlay settings shortcut to enable **Surface mining** and assign
   a show/hide shortcut if desired. The Mining workspace is reserved for future
   tools; current mining guidance appears in the overlay.
3. Set the six rig shortcuts in that settings window or in **Input** settings.
   Both locations edit the same bindings. Defaults are **Ctrl+Alt+F1** through **Ctrl+Alt+F6**.
   Input names these **Tracker/Mining Rig (1)** through **Tracker/Mining Rig (8)**.
   Outside Rhino mining, all eight shortcuts toggle surface trackers; slots 7 and 8
   do not place rigs. Mining settings retain the six **Mining rig** labels.
   Existing custom tracker bindings are preserved. A customized RC43 rig chord is
   retained when its matching tracker still uses the default.
4. Operate a Rhino on a planetary surface with journal/status tracking active.
   The panel appears when its normal overlay display conditions are met.

The panel can be moved and resized through the existing overlay position editor.
It follows the selected overlay theme, including **Monochrome Companion**, which
pairs with the main application's dark **Monochrome** theme.

## Rig locations

While aboard the Rhino, press a rig's shortcut to save its deployment location.
Its numbered circle appears on the radar and its chevron appears below the
vehicle row. Press the same shortcut again to clear that rig before recording
a replacement location. This records a location; it does not deploy a rig in-game.

Rig locations are saved separately from biology bookmarks for the current
Commander and body. Distance and direction update as the player moves.
The placement calculation accounts for the Rhino's cockpit and deployment offsets.

Returning to your own ship on foot or docking the Rhino automatically clears
all six saved rigs, matching the game's destruction of deployed rigs. Re-entering
the Rhino on foot keeps the markers. Taxi and multicrew boarding do not clear them.

| Cue | Meaning |
| --- | --- |
| COLLECT / cyan chevron | Within 5 meters of the saved rig location. |
| TOO CLOSE / red chevron | Within the 78-meter deployment exclusion distance. |
| TRACKED | Outside that exclusion distance. |
| NOT SET | No saved location for this rig slot. |

Colors follow the overlay theme; text labels retain their meaning. The radar
uses 70-meter rig circles and the legacy mining zoom as its default.

## Ground resources

Resource bookmarks appear below the rigs in two columns, filled left to right
with new rows as needed. Every saved location has its own material name,
direction chevron, and distance, including multiple locations for one material.

These locations are **manually saved**, not automatically detected. At a deposit,
send `+helium` or `+thortveitite` in game chat to save your current position under
that name. Repeat elsewhere to add another location. Send `-helium` to remove
the nearest matching bookmark, or `--helium` to remove all matching locations
on the current body. Substitute the relevant material name.

Distance and direction update automatically as you move and turn. Below
150 meters, the tracker uses the theme's near-target color and a single
chevron; farther targets use the normal accent and a double chevron. Distances
switch to kilometers at 1 km. Resource circles on the radar have a 70-meter
radius. Long lists scroll using overlay interaction mode, preserving room for
the radar and cargo row.

These use the existing Commander/body surface bookmarks, separate from the six
rig slots. Boarding your ship clears rigs but preserves resource locations.
Biology bookmarks and numbered quick trackers do not appear in resource rows.

## Vehicles and cargo

The vehicle row is split into **Ship** and **Rhino** columns. On foot, the Rhino
chevron points back to its parked location. While aboard, it shows **X** to
indicate untracked. The cargo row shows occupied capacity out of 72.

Mining guidance stays available when walking back to a parked Rhino. Surface
Survey and its mini tracker stay hidden during that mining activity, even if
the Mining panel is toggled off. Other vehicle activity restores their normal
visibility rules. Rig placement shortcuts require being aboard the Rhino.

## First test

Save a rig location, drive away, and check its distance and bearing. Disembark
and walk a short distance: the Rhino column should point back to the vehicle.
Re-enter it and confirm that the chevron becomes X. Finally, clear the saved
rig with its shortcut and check that its slot returns to NOT SET.
