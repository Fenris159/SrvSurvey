# SrvSurvey-XP 2.1.3.0-rc.77.1

This release brings Journeys into Navigation with expedition history, flexible
starting visits and portable photo reports. Colonization Market Search follows
your project filters, and mining overlay preferences can be edited with the
game closed. It also retains the mining, commander, ship and Fleet Carrier fixes
from RC77.

## What's changed

- **Journeys workspace:** Manage expeditions directly under Navigation instead
  of opening a separate window. The Travel shortcut opens Journeys. Archive
  completed journeys to tuck them away, restore them when needed, or delete
  their saved records after confirmation. Journals, system notes and screenshots
  stay intact.
- **Starting a journey:** Choose Use current system to use your current visit
  automatically, including a docked login. Use previous visit opens system-name
  autocomplete and a scrolling list of past ship and carrier arrivals with UTC
  dates and times. Choose the exact visit, then enter the journey name and
  optional description. Starting from an earlier visit includes later returns
  to the same system. Manual system-name searches also work offline.
- **Journey date filters:** Narrow historical searches with an optional calendar
  date. The selected UTC day is included, with no time entry required. All dates
  clears the cutoff. Older journals are skipped where possible while files
  spanning the selected date remain searchable. Copied journal modification
  dates no longer truncate replayed history. Changing the search, date or
  commander clears stale visit choices; moving systems during current-start
  preparation requires beginning again with the new location.
- **Journey readouts and notes:** The redesigned workspace keeps the library,
  route summary, visited systems, statistics and editable details together.
  Visit rows show notes, photos and landings, including activity from earlier
  visits to the same system. Unsaved edits survive commander changes and are
  protected when switching history lists. The on-top preference applies while
  Journeys is selected. Begin and Cancel remain reachable while the creation
  form scrolls.
- **Journey screenshots:** Image lists use the PNG converter's current output
  folder and refresh after conversion or a destination change without discarding
  unsaved notes. Existing converted images remain available across repeat visits
  to the same system.
- **Portable Journey reports:** Export a completed journey into its own folder
  containing index.html and an images subfolder. Reports include expedition
  statistics, a route overview, searchable visit history, saved notes and tiled
  photo previews. Select a tile to enlarge it, browse with arrow keys, close
  with Escape, or open the original image. Relative links keep the report usable
  when the whole folder is moved or shared. Full-size images and smaller previews
  are bundled together; source screenshots stay intact.
- **Journey report reliability:** Browse reports and print expanded visit details
  to PDF without an internet connection. Large exploration and biology reward
  totals are retained. Each export creates a new folder, preserving earlier
  exports, and publishes it only after its files are ready. Completing or
  reprocessing a journey keeps its original commander context, so late results
  cannot restore an old readout or overwrite the new commander's status after a
  commander change or disconnect.
- **Colonization Market Search:** Select Projects follows the system, build
  status and ordering selected under Projects. Filter changes keep the list
  manageable without clearing chosen shopping projects. A compact Selected
  outside filters section lets you remove hidden selections, and refreshing
  projects preserves the selected system filter.
- **Mining overlay preferences:** Configure overlays before a journal loads or
  with the game closed. All seven overlay preferences survive restarts, remain
  separate for each commander, and follow portable backup/restore and sync.
  Failed edits stay available through journal connection and commander changes
  for retry. Saving one preference preserves unrelated changes made in another
  settings window.
- **Recent Prospects:** Mining notifications show asteroid engineering material
  content as Low, Medium or High instead of a fixed 100% remaining reading.
  Mineral percentages remain visible, and missing material content stays hidden,
  including in recovered sessions.
- **Mining session overlay:** Sort prospecting yields by average mineral
  percentage or number of finds. Five minerals appear by default; change the
  result count with single-step up/down controls in mining overlay settings.
  Preferences are saved per commander and apply immediately, while session
  history and reports retain every result. These controls are also available
  before commander journal data loads.
- **Owned fleet:** Ship changes produce one consistent CURRENT marker. Journal
  ship selection and Frontier fleet data use persistent ship identities,
  including when an older cache contains conflicting markers. Newly purchased
  ships also appear while awaiting their Frontier ship ID.
- **Current Ship:** Journal loadouts update the selected ship and its module
  readout without borrowing another ship's details. Ship totals retain cargo
  value when journal hull and module values update. Complete engineering updates
  preserve modules, and removed hangars no longer restore stale launch bays.
  Switching commanders clears the previous ship's cached readouts, including
  when the selected account is offline or has no cached profile.
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
