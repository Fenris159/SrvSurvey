# SrvSurvey-XP 2.1.3.0-rc.64

RC.64 patches colonization overlay icon sizing and badge controls, and restores
next-jump distances for individually selected destinations. It also includes
the EDAstro features introduced in RC.63 below.

RC.63 adds EDAstro exploration POIs to the workspace and existing navigation
overlays, with attraction photos, bookmarks and an optional current-system
panel. A separate opt-in lets you contribute supported live journal data to
EDAstro, with disclosure, paced batches and consolidated activity logging.

## What's changed

- **Scale colonization checkmarks with the other icons.** Completion checkmarks
  now follow the Icons slider alongside row badges. Larger icons can extend
  beyond row edges without increasing row height; commodity and cargo-count
  font sizes determine the height instead.
- **Choose a quieter colonization table.** Carrier badges now read LOAD and
  READY. READY still requires enough cargo on the carrier itself. The new Hide
  row badges option hides status and assignment labels while keeping completion
  checkmarks, cargo counts and loading highlights. Changes apply immediately to
  live overlays and the position preview, and are saved for future sessions.
- **See distances when selecting destinations individually.** Next-jump
  information calculates the distance once matching destination coordinates are
  available from the existing system lookup. Plotted routes also retain distance
  information for their final jump, and arriving does not replace the displayed
  jump distance with zero.
- **Explore EDAstro attractions.** The Exploration POIs · EDAstro workspace
  searches systems or attractions, with system autocomplete and an editable
  reference system for nearby-radius searches. Category, score, rarity and
  Legacy filters help narrow the catalogue. Results fill the workspace width,
  keep column headings aligned and show supplied photos in the selected POI
  description. Bookmark POI saves an attraction in your navigation bookmarks.
- **See POIs alongside your existing navigation.** The optional exploration
  overlay appears passively in systems with known attractions. Its visibility
  and shortcut use the Exploration Overlay Settings window. Jump information,
  route views and Galaxy Map system intelligence show matching POI context
  without introducing another route planner. POI names appear once and follow
  the title font scale; position previews show a system with multiple POIs.
- **Submit a new attraction.** New POI, beside Refresh catalogue, opens the
  Galactic Exploration Catalog submission form on EDAstro's website.
- **Choose whether to contribute exploration data.** Settings → Privacy & Sharing
  includes Configure EDAstro Sharing. Sharing starts disabled. The disclosure
  lists the supported carrier, docking, biological scan and Codex events and
  explains that carrier identifiers and destinations can reveal activity.
  Reports identify the application as SrvSurvey-XP and omit commander names,
  Frontier IDs, balances, carrier finances and EDDiscovery extension fields.
- **Keep journal sharing paced and quiet.** Only supported new live events
  requested by EDAstro are queued; startup history, diagnostic replay and Legacy
  sessions are excluded. Batches contain at most 200 events, wait at least ten
  seconds between requests and honor provider cooldowns. Transient errors retry
  with backoff, ambiguous multi-window sessions pause sharing, and disabling
  sharing cancels requests and clears the bounded memory queue. Unsent data
  expires after one day or when the application closes. Success logs use
  fifteen-minute activity summaries.

## Update channel and packages

RC.64 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64`
- Tag: `xp2-v2.1.3.0-rc.64`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. The EDAstro upload protocol uses no personal
API key; acceptance of the SrvSurvey-XP application identity depends on EDAstro.
POI catalogue use and journal sharing have separate controls.
