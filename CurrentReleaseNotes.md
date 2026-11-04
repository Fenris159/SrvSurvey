# SrvSurvey-XP 2.1.3.0-rc.77.5

RC77.5 adds optional Steam screenshot processing for the Journey photo library
and improves native screenshot conversion with automatic folder detection,
persistent retries, accurate capture-time location labels and faster lossless
PNG encoding. Journey edits and background updates retain the correct commander
through interrupted operations, carrier observations keep personal and squadron
state separate, mining searches restore consistent results, and Colonization
Nexus action menus no longer produce empty-context binding warnings.

RC77.4 adds dedicated Squadron Carrier routes, clearer Canonn Prior Scans,
persistent mining search results and galactic Journey report maps. Personal and
squadron carriers can be tracked together while keeping their progress, fuel
and jump readouts separate. It also improves screenshot conversion and folder
selection, Journey workspace consistency, mining result restoration and Nexus
log clarity, alongside the recovery and reliability improvements below.

RC77.3 fixes older Journey files leaving an empty workspace unable to begin a
new Journey. Older metadata is upgraded with an original-file backup, and saved
history remains available when its original journals can no longer be replayed.
Unreferenced active Journeys can be resumed and completed from the library.

Only a readable Journey marked Active in the selected commander's list blocks
another start. Unavailable active selections now show warnings while keeping
their saved files intact. A completed Journey no longer prevents starting again
from the same visit; the new Journey receives its own saved file.

RC77.2 fixes Journey starts when Steam, Epic, or other Elite installations keep
journals in different folders. It also improves replay when journal sessions
overlap or a journal source is unavailable.

This release brings Journeys into Navigation with expedition history, flexible
starting visits and portable photo reports. Colonization Market Search follows
your project filters, and mining overlay preferences can be edited with the
game closed. It also retains the mining, commander, ship and Fleet Carrier fixes
from RC77.

## What's changed

- **Screenshot retry responsiveness:** Live processing bounds durable Steam
  admissions and conversion attempts, so large libraries do not flood the recovery
  queue in one poll. Ready images keep progressing past unreadable captures even
  after earlier conversions finish. Failed queue saves retry before the source
  arrives; an inaccessible Steam completion archive does not block pending BMP
  recovery. Same-second filename collisions reuse verified matches without
  repeatedly hashing every earlier image. An unreadable occupied PNG no longer
  blocks a new capture from using a distinct suffixed filename.
- **Steam capture ownership:** Consecutive journal parts retain Commander and
  location context, with dependent history recalculated when an earlier file
  changes. External Proton libraries are matched to their Steam client; ambiguous
  installations and unusable account metadata require manual source selection.
  Quoted braces in Steam metadata remain valid, and linked screenshot folders
  keep their index timestamps, including sources with a trailing separator.
  Malformed index paths cannot discard later valid captures. Fresh docking names
  replace old station or star labels while retaining valid planet context.
  Historical captures require covering journal history; a verified, currently
  bound live session can remain quiet without an arbitrary age cutoff. Selected
  commanders follow verified continuation parts on startup and during rotation,
  preserving session context and unread activity. If a later part becomes locked
  or unavailable, already-consumed activity and commands are delivered once and
  reading resumes after repair without replaying the preceding part. Initial
  replay remains in bootstrap mode through partial or failed reads, so historical
  commands stay suppressed. Partial startup reads also retain the current status
  and avoid recounting exploration totals already present in a saved profile.
  Retained status is cleared when polling switches to another journal directory.
  Quiet Steam captures also work when Elite is on a nested Gamescope display
  with no switchable desktop window.
- **Journey recovery and shared writes:** A saved note's pending visit count can finish even
  if another window edits the note before retry. The external text is retained;
  additional unsaved edits still receive conflict protection and can be discarded.
  Journey edits read and write under one shared file lease so independent windows
  retain each other's changes. Failed live saves retain consumed Journey progress
  through later metadata and note edits, while preserving another window's saved
  metadata and note counts. Staggered readers reconcile exact records at the saved
  watermark, avoiding double counts while retaining distinct same-second events.
  Catch-up persists new activity even when it shares the saved timestamp. Older
  saves retain their unknown replay boundary until a later timestamp, and copied
  journals with shifted lines do not duplicate saved captures. Reconciliation
  retains individual record identities throughout the tracked history and merges
  pending observations against the latest saved Journey, including different
  captures with equal totals. Late observations update their original visits
  without reopening departures or losing newer timestamps. Refresh and conclude
  flush pending records first. Catch-up receipts also survive failed saves and
  later metadata edits. Earlier byte-position receipts remain compatible, and
  known live records stay deduplicated even at an incomplete legacy boundary.
  Late body-count reports retain the newer observation, including same-time final
  scan results. Older snapshots without reliable coverage keep ambiguous pending
  activity available for recovery.
  Live/history matching handles UTF-8 and mixed line endings. Readouts identify
  note-save counts explicitly.

- **Guides:** Refresh in-app instructions and the shipped chat reference for
  Journeys, portable photo reports, Commander readouts, FC/SC Routes, Discord,
  Stealth Mode, Canonn radar, mining controls and Colonization project filters.
  Correct Theme control labels, vehicle defaults and Linux overlay guidance;
  distinguish tested Gamescope versions from current-version claims.
- **Screenshot preferences in backups:** Keep BMP sources, manual Steam folders
  and PNG destinations local during portable sync and cross-computer restores.
  Processing options still follow the user; explicit hardware restore can recover
  paths from that computer's own backup.

- **Screenshot conversion:** Late or locked BMPs stay pending and are retried
  during live and idle journal polling, including after restarting SrvSurvey.
  Each capture retains its original system, commander, Guardian details and
  conversion preferences through travel or settings changes. Repeated journal
  records do not create duplicate PNGs, and turning conversion off pauses pending
  work. Failure messages explain the file error while retaining the original BMP. Image reads use managed streams
  to support long Windows paths during decoding and PNG verification. Unchanged
  corrupt files stay pending without hot-poll decoding; a file change or periodic
  retry detects repairs even when size and modification time stay unchanged. Recovery verifies published images before
  cleaning up an original BMP and avoids creating duplicate copies. Concurrent
  captures that choose the same filename retain both images before originals
  can be removed. Live conversion reports each finished capture without waiting
  for the remaining queue or a Steam history scan. Faster lossless PNG encoding
  reduces high-resolution processing time while retaining full image quality;
  PNG files may be slightly larger.
- **Screenshot folders:** Automatic detection uses the configured Wine/Proton
  journal profile's Pictures folder, with the platform Pictures location as a
  fallback. Windows keeps its known-folder support, including redirected
  Pictures folders. The BMP source can still be chosen manually. The shared
  PNG destination used by Journeys is displayed read-only with an **Open folder**
  button that creates the folder when needed. This destination stays fixed
  through source-folder changes, journal-profile changes and restarts; existing
  custom destinations are retained. A failed destination save retries the same
  frozen folder rather than deriving a new location from another profile. Older automatically saved source defaults
  gain Proton detection while explicit manual choices remain intact.
  Bootstrap selects the commander's configured profile before fixing the first
  output destination, including linked Wine journals. Configured journal alias
  changes are applied even when no new journal events arrive. Choosing the displayed
  source folder explicitly keeps that choice through later profile changes.
  Preference edits made before a journal is selected keep that first destination
  automatic until the correct profile is known.
  Native absolute screenshot paths are honored, with a configured-folder
  fallback for moved or guest files.
- **Steam screenshots in Journeys:** An optional **Process Steam screenshots**
  setting detects Elite's capture folder for the Steam account on Windows,
  Linux and Flatpak, with a manual folder choice when needed. JPEG captures are
  matched to commander locations using their capture times and journal history,
  then saved as verified PNGs in the same fixed Journey photo library. Shared
  label and original-file removal settings apply; unmatched captures stay
  untouched, and completed imports are remembered across restarts. Processed
  images appear in Journey galleries and portable HTML reports. Capture-time
  body labels include stations and other named locations as well as planets;
  departures clear stale names, and later observations cannot relabel an earlier
  capture. Partially written images remain pending until a complete decode
  succeeds, protecting originals when automatic removal is enabled.
- **Quieter Colonization Nexus:** Reused or closing project action menus no
  longer produce empty-context binding warnings in the application log.
- **Journey workspace consistency:** Switching commanders or disconnecting
  retires pending workspace operations so late history, start, save and export
  results cannot replace another commander's readout. Unsaved Journey drafts
  survive an interrupted return, and delayed photo conversion refreshes the
  original system's gallery and portable report images. Metadata saves preserve
  visits received while saving. System notes wait for their stored baseline
  before editing and update the selected visit's count, including completed
  Journeys. An export destination picker cannot hand one commander's report to
  another commander's session, and recovery keeps history-load errors visible.
  New edits to a readable Journey take priority over a retained draft whose
  original Journey has become unavailable.
  A saved system note can retry a failed Journey visit-count update without
  falsely conflicting with its own text, including after a commander switch.
- **Mining search restoration:** Restoring settings reconciles cached results,
  reference systems and status messages. An unfinished or incomplete search
  cannot replace a newer accepted result or cache entry. Filter edits made during
  a search reconcile after it finishes, restored results retain their reference
  through travel, and Reset resumes location following even before the first
  location is known. Empty Powerplay results no longer show an old result count.
- **Carrier observation consistency:** Fuel, position and next-jump tracking
  share identity and timestamp decisions, keeping personal and squadron carrier
  observations separate while preserving legacy carrier inference. Unsupported
  carrier types cannot replace a known carrier identity or advance its route.
- **Squadron Carrier routes:** SC Routes sits beside FC Routes with its own
  workspace, overlay, panel shortcut and next-jump settings. Both carriers share
  saved itinerary bookmarks while keeping their selected routes, progress and
  preferences separate. Both overlays can track scheduled jumps, cancellations
  and arrivals even without an active itinerary.
- **Carrier route auto-copy:** Enable auto-copy for both FC Routes and SC Routes.
  Opening the Galaxy Map copies one destination from the selected active
  carrier, falling back to the other when the selected route is paused or
  complete. Selecting auto-copy in Route Manager or Boxel Search disables both
  carrier selections, so competing tools cannot replace the destination.
- **Carrier tracking and bookmark safety:** Carrier identity checks keep jump
  progress, countdowns, fuel and location updates on the correct carrier.
  Ambiguous arrivals do not update both carriers just because their destinations
  match. Tracking changes preserve shared bookmark notes, favorites and
  waypoints. Notes saved from a route workspace update the other carrier's
  readout without discarding its unsaved edits.
- **Mining search persistence:** Surface Mining and Powerplay merit search
  results retain their chosen reference when you jump to another system.
  Force include if viable no longer causes an arrival to reset the results,
  including when a search is still running. Reset or search again to replace
  the results.
- **Canonn Prior Scans:** The live panel keeps its compact preview layout and
  wraps long species names instead of expanding across the screen. Existing
  custom sizes remain available. The radar stays visible whenever the panel is
  shown. Show bio signals from Canonn on the radar now controls the whole panel
  and stays synchronized with panel availability and its shortcut.
- **Galactic Journey maps:** HTML exports plot every recorded visit on an
  equal-scale X/Z map, including negative coordinates, with Sol and Sagittarius
  A* as fixed position references. Reference markers do not add travel legs.
  Switch between Galactic context and Route detail, and select any visit point
  to open its history entry. Return visits at the same map position offer a
  dated visit chooser, and reference labels remain readable near the map's edges.
- **Journey journal reliability:** Starting a Journey uses the selected
  commander's journals across all available folders, regardless of folder order.
  Current starts use the latest matching location. Catch-up and reprocessing
  include later activity from overlapping sessions, respect the Journey's start
  time, and count copied journals once. Journal read failures show warnings;
  incomplete reprocessing preserves saved history. Some inaccessible folders
  may be excluded during discovery. Saved system notes finish loading before
  the history workspace becomes ready.
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
  form scrolls. Save or discard edits to an existing journey before beginning
  another expedition.
- **Journey screenshots:** Image lists use the same output folder as the PNG
  converter and refresh after conversion, including captures that finish late,
  without discarding unsaved notes. Existing converted images remain available
  across repeat visits to the same system.
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
  settings window. An explicit edit for a remembered commander takes precedence
  over the same field in an older unassigned draft when the journal connects.
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
