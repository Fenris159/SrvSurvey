# SrvSurvey-XP 2.1.3.0-rc.77.1

This release brings Journeys into Navigation, streamlines colonization project
selection, and lets mining overlay preferences be edited before the game starts.
It also includes the RC77 mining, commander, ship and Fleet Carrier improvements.

## What's changed

- **Journeys workspace:** Browse expedition history directly under Navigation
  instead of opening a separate window. The Travel shortcut opens this workspace.
  Archive completed journeys to tuck them away, restore them when needed, or
  delete their saved records while keeping journals, system notes and screenshots.
- **Journey starting visits:** Find all recorded arrivals at the chosen system
  and select the exact visit by its UTC date and time. Ship and carrier arrivals
  appear newest first. Starting from an earlier visit includes later returns,
  and copied journal file dates no longer truncate that history.
- **Journey reports:** Export a completed journey as a portable report folder
  with expedition statistics, a route overview, searchable system history and
  saved notes. Each system includes tiled screenshot previews that enlarge in
  the browser. The folder contains index.html, full-size images and smaller
  previews linked with relative paths, so it can be moved or shared together.
  Expand visit details or print the report to PDF without
  an internet connection. Large biology and exploration reward totals are retained.
  Each export creates a new folder, keeping earlier exports intact. The folder
  appears only after all its files are ready.
- **Journey readouts:** The redesigned layout keeps the journey library, route
  summary, visited systems, statistics and editable details together. Visit rows
  show notes, screenshots and landings, including notes and photos from earlier
  visits to the same system. Unsaved edits are protected when switching lists
  or commanders. Keep the application on top while viewing Journeys using the
  option in journey details.
  Screenshot lists use the current PNG converter destination and refresh after
  conversion or a destination change without discarding unsaved notes.
- **Colonization Market Search:** Select Projects now follows the system, build
  status and ordering selected under Projects. Changing those filters keeps the
  list manageable without clearing previously selected shopping projects.
  A compact “Selected outside filters” section lets you remove hidden selections,
  and refreshing projects preserves the selected system filter.
- **Mining overlay preferences:** Configure overlays with the game closed or
  before a journal loads. Changes survive restarts, remain separate for each
  commander, and follow portable backup/restore and sync. If saving fails, edits
  stay available through journal connection and commander changes for retry.
  Saving one preference preserves unrelated changes made in another window.
- **Recent Prospects:** Mining notifications now show asteroid engineering
  material content as Low, Medium or High instead of a fixed “100% remaining”
  reading. Mineral percentages remain visible, and missing material content
  stays hidden, including in recovered sessions.
- **Mining session overlay:** Choose whether prospecting yields are sorted by
  average mineral percentage or number of finds. Five minerals appear by
  default; the result count can be changed with single-step up/down controls
  in mining overlay settings. Preferences are saved per commander and apply
  immediately, while session history and reports retain every result. These
  controls are also available before commander journal data loads.
- **Owned fleet:** Ship changes produce one consistent CURRENT marker.
  Journal ship selection and Frontier fleet data now use persistent ship
  identities, including when an older cache contains conflicting markers.
  Newly purchased ships also appear while awaiting their Frontier ship ID.
- **Current Ship:** Journal loadouts update the selected ship and its module
  readout without borrowing another ship's details, and ship totals retain
  cargo value when journal hull and module values update. Complete engineering
  updates preserve modules, and removed hangars no longer restore stale launch
  bays. Switching commanders clears the previous ship's cached readouts,
  including when the selected account is offline or has no cached profile.
- **Commander data freshness:** Reputation, Community Goals and ship data use
  observation timestamps to reconcile journal and Frontier updates. A later
  Frontier fetch takes precedence, while an older request that finishes late
  cannot overwrite newer journal data. Mixed journal batches stay attributed
  to the correct commander, and an interrupted multicrew session no longer
  blocks own-ship updates after restarting the game.
- **Fleet Carrier jumps:** Personal and squadron carriers keep separate jump
  requests, cancellations and arrival state. Each carrier's readout uses its
  own Frontier refresh time so activity on one cannot replace the other.
  Records without an observation timestamp cannot replace a tracked jump.
