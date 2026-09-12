# Surface mining

Available in **SrvSurvey-XP 2.1.3.0-rc.46.5**. Surface Mining combines Rhino rig
guidance with reusable maps of planetary mining-location signals and their
deposits. The same workflow is covered inside the application under
**Guides > Surface mining**.

## Setup

1. Expand **Activities** in the main window and select **Surface Mining**.
2. Use its overlay settings shortcut to enable the Surface Mining guidance and
   Overview Map overlays and assign show/hide shortcuts if desired.
3. Set the six rig shortcuts in that settings window or in **Input** settings.
   Both locations edit the same bindings. Defaults are **Ctrl+Alt+F1** through **Ctrl+Alt+F6**.
   Input names the first six **Tracker/Mining Rig (1)** through **Tracker/Mining Rig (6)**,
   followed by regular **Tracker (7)** and **Tracker (8)** entries.
   Outside Rhino mining, all eight shortcuts toggle surface trackers; slots 7 and 8
   do not place rigs. Surface Mining settings retain the six **Mining rig** labels.
   Existing custom tracker bindings are preserved. A customized RC43 rig chord is
   retained when its matching tracker still uses the default.
4. Operate a Rhino on a planetary surface with journal/status tracking active.
   The panel appears when its normal overlay display conditions are met.

The panel can be moved and resized through the existing overlay position editor.
It follows the selected overlay theme, including **Monochrome Companion**, which
pairs with the main application's dark **Monochrome** theme.

## Map a mining location

The **Surface Maps** tab lists saved surface-mining bookmarks. Select any row to
open it in **Survey Map**, or expand its chevron to review each deposit's mineral
amount and density. The Contains and Body Type filters are populated from the
saved maps, and the table can be sorted from its column headings. Select the star
after **Updated** to favorite a map, then enable **Favorites** in the search panel
to show only starred maps.

Drive to the orange border of a mining-location signal and face the marker at its
center. Send this case-insensitive chat command using your current heading, the
measured border radius in kilometers, and the signal's number:

```text
.mining <heading 0-359> <border radius km> <signal number>
```

For example, `.mining 120 6.44 4` saves **Mining Location Signal 4** with a
6.44 km radius. SrvSurvey uses the body's journal radius, your latitude and
longitude, and the supplied location radius to calculate the center. The command
requires live surface coordinates and reports acceptance or a validation error
through Status notifications.

If the calculated center needs correction, drive to the true center and send:

```text
.mining center here
```

This moves the map center to the player's current surface coordinates and realigns
the border and distance rings. The saved radius and every existing deposit marker
remain unchanged.

For a precise distant bearing, send `.alignment` to toggle a thin red vertical
guide at the exact center of the Elite game window. It spans the clear HUD area
below the heading box and above the lower radar. Use the Rhino driving view,
place the distant mining-location circle under the guide, then read the visible
in-game compass heading for the `.mine` command. Turret mode does not show the
required heading. Send `.alignment` again to hide the session-only guide.

From anywhere inside the saved border, add a deposit by heading and distance.
The projection starts at your live position, so you do not need to return to the
map center:

```text
.mine 15 ruby 1.24 high/medium
```

At a deposit, save your current position directly or remove the nearest marker
within 0.5 km:

```text
.mine ruby medium/low here
.mine move haematite here
.mine delete here
.alignment
```

The amount/density pair belongs to that individual deposit, and each value may be
Low, Medium, or High. Commodity names must
match **Hotspot List** and may contain spaces. The command parser rejects unknown
commodities, headings outside 0–359, non-positive border radii, negative deposit
distances, invalid signal numbers, and unrecognized amount or density values.
Bearing-and-distance placement also rejects a same-commodity marker within 100 m
as a likely duplicate. Precise `here` placement remains available for genuinely
overlapping deposits.

To correct an existing marker, stand at its true position and send `.mine move
<commodity> here`. SrvSurvey moves only the nearest marker matching that commodity,
and only when it is within 200 m. Success and failure are reported through Status
notifications.

The **Survey Map** and Overview Map overlay share the selected bookmark, live
player position, 1 km rings extending through the whole-kilometer ring that
encloses the saved boundary, and saved deposit markers. Use the mouse wheel,
slider, or minus and plus controls to zoom;
drag the map to pan after zooming in. Marker visibility can be filtered by
commodity, mineral amount, and density without changing the saved map. The
workspace Survey Map and Overview Map overlay use the same active filters.
Right-click the application map to place or remove a temporary 4.5 km-radius
planning circle. Hold the right button and drag to reposition it; the Overview
Map overlay mirrors the circle. Marker names are shown in the Overview Map by
default; clear **Show Marker Labels in Overview Map** in Surface Mining overlay
settings to show the colored marker dots without labels. This setting does not
remove labels from the application Survey Map.

A saved Surface Mining map is selected automatically when the player's live
surface position enters its border. It is unloaded when the player leaves the
saved radius, so the map and overlay follow the location currently being visited.

Surface maps use the shared **Navigation > Bookmarks** catalog. They are assigned
the Surface Mining category automatically and store the Commander, system, body,
body type, distance from Sol, arrival distance, signal number, border radius,
center coordinates, notes, and deposit-specific ratings. Editing or deleting the shared bookmark
updates the Surface Mining workspace.

## Reference tabs

**Hotspot List** shows the supported surface commodities, compatible body types
and community price snapshot. Select commodities in its Overlay column to keep a
compact three-column **Mining Ref** overlay on screen. **Surface Hunt** provides
sortable body, geology, stellar-clue and price guidance for finding promising
surface-mining locations. Both tables support horizontal scrolling at narrow
window sizes.

## Rig locations

While aboard the Rhino, press a rig's shortcut to save its deployment location.
Its numbered circle appears on the radar and its chevron appears below the
vehicle row. Press the same shortcut again to clear that rig before recording
a replacement location. This records a location; it does not deploy a rig in-game.

Rig locations are saved separately from biology bookmarks for the current
Commander and body. Distance and direction update as the player moves.
The placement calculation accounts for the Rhino's cockpit and deployment offsets.

By default, returning to your own ship on foot or docking the Rhino automatically
clears all six saved rigs, matching the game's destruction of deployed rigs.
Disable **Clear rigs automatically when boarding your ship** in Mining overlay
settings to retain your saved markers instead. This preference persists between
sessions. Re-entering the Rhino on foot keeps the markers. Taxi and multicrew
boarding do not clear them.

To clear everything on the current body manually, send `---` in game chat.
This clears all six rigs, named resource and biology bookmarks, and regular
trackers even when automatic rig clearing is disabled. It does not erase scan
history or bookmarks on other bodies.

| Cue | Meaning |
| --- | --- |
| COLLECT / cyan chevron | Within 5 meters of the saved rig location. |
| TOO CLOSE / red chevron | Within the 78-meter deployment exclusion distance. |
| TRACKED | Outside that exclusion distance. |
| NOT SET | No saved location for this rig slot. |

Colors follow the overlay theme; text labels retain their meaning. The radar
uses 78-meter-radius rig circles and the legacy mining zoom as its default.

## Experimental automatic rig tracking (Windows)

In Surface Mining overlay settings, enable rig bar detection and select the deployment
bar color. Bright green is the default; update it if your HUD mod changes the
bars. Gray, white, and black are ignored. Manual rig shortcuts remain available.

1. While aboard the Rhino, open **Edit overlay positions** and choose **Mining**.
2. Move and resize the capture frame around all six HUD circles. Drag the red
   centres into place: slots 1–3 run left to right on the top row, then 4–6 on
   the bottom row. Adjust **Size**, **Height**, and **R** for their shape and rotation.
3. Use **Gap** to align the cyan curves with the middle of the segmented deployment
   bars. **Search** adjusts the movement allowance independently of circle size.
4. Stop, look forward with cockpit panels closed, and start with rig 1 deployed
   first to establish the anchor. Select **Test** to hide the guides and check
   the slot readings. Calibration Test does not change saved trackers.
5. Save the editor layout and close it to use automatic tracking. Recheck
   calibration after changing aspect ratio, field of view, or HUD layout.

Once tracking is steady, **BAR** immediately saves a missing rig tracker at the
Rhino deployment position. Repeated readings preserve that location. Three
continuous seconds without a bar remove that specific tracker. **?** means
uncertain: no tracker changes occur, and any pending removal delay restarts.
An ellipsis indicates confirmation is pending.

Automatic changes pause while your surface position or heading changes and resume
after one second of stillness. HUD movement or reacquisition also needs one steady
second. Stop before deploying so the recorded location is useful. Detection only
runs with the game active, aboard the Rhino and looking forward with no cockpit
panel open. Incomplete or ambiguous views can remain uncertain; use the manual
shortcut when necessary. This remains an experimental feature, particularly under
changing lighting or HUD movement.

Only calibration and the selected color are saved. Captures are processed in
memory and discarded; audio is not captured. Resource deposits remain manually
bookmarked, as described below.

## Rig range warning

Surface Mining overlay settings includes **Mining rig range warning**, with its own
show/hide toggle and optional shortcut. Its initial placement copies your saved
Flight Warning placement; move it independently in the overlay editor afterward.

While aboard the Rhino, moving more than **4 km from any tracked rig** displays
**TOO FAR FROM RIGS**, with the reminder **Moving beyond 4.5Km will Destroy Rigs**.
The warning uses the farthest saved rig, even when another rig is nearby. It
clears when every tracked rig is back within 4 km or when you leave the Rhino.
It depends on saved rig bookmarks; it does not detect untracked rigs in the game.

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
rig slots. Automatic ship-boarding cleanup, when enabled, clears rigs but
preserves resource locations. The `---` chat command clears both.
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
