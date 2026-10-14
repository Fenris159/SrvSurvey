# SrvSurvey-XP 2.1.3.0-rc.64.4

RC.64.4 makes live overlays match their position-editor previews, improves
column spacing for large colonization cargo counts, and makes panel resizing
follow the mouse consistently. It includes the earlier changes below.

RC.64.3 recovers pending journal uploads after restarts and unexpected shutdowns,
keeps sharing failures from interrupting the session, and limits passive
notifications to three visible messages by default. It includes the earlier
overlay and exploration changes below.

RC.64.2 keeps expanding overlays within the available screen space, adds a
per-panel growth direction choice, and preserves existing saved positions.
It also refines colonization text and checkmark scaling and starts Exploration
POI searches with nearby systems included. It includes the earlier changes below.

RC.64.1 improves EDAstro sharing diagnostics and clears harmless Exploration
POI selection warnings. It includes the RC.64 and RC.63 changes below.

RC.64 patches colonization overlay icon sizing and badge controls, and restores
next-jump distances for individually selected destinations. It also includes
the EDAstro features introduced in RC.63 below.

RC.63 adds EDAstro exploration POIs to the workspace and existing navigation
overlays, with attraction photos, bookmarks and an optional current-system
panel. A separate opt-in lets you contribute supported live journal data to
EDAstro, with disclosure, paced batches and consolidated activity logging.

## What's changed

- **Match your overlay preview in game.** Live panels and position previews
  use the same presentation, typography and measured dimensions. Display scaling
  is taken from the screen actually rendering the panel, keeping its size and
  saved position consistent across separate windows and combined overlays.
- **Resize panels with predictable control.** The resize handle follows the
  pointer at reduced and enlarged panel scales, and manual resizing keeps the
  panel's top-left corner steady. Editor tabs no longer create a dead zone when
  starting or reversing a resize. Sizes and positions remain in place after
  saving and reopening the editor. Colonization backgrounds fill the resized
  frame, and narrowing massacre mission panels keeps the kill counts visible.
- **Keep large cargo counts readable.** Colonization count columns retain
  clear spacing for five- and six-digit values, including carrier deltas and
  combined cargo totals. The position-editor sample includes larger counts so
  you can check the layout before using it in game.
- **Recover pending journal uploads.** EDDN, Inara, EDSM, EDAstro and VoxStellar
  save captured reports locally before sending. Closing the app or an unexpected
  shutdown preserves pending reports for the next session, with their original
  timestamps and contents. Retries respect each provider's limits, and saved
  acknowledgements prevent replaying reports already confirmed as accepted.
- **Keep sharing tied to your commander and consent.** Retained reports stay
  separate by commander and profile, and resume only with the appropriate
  credentials and sharing enabled. Temporarily unavailable Inara or EDSM keys
  preserve pending reports; explicitly removing a key or disabling sharing
  clears the corresponding retained work. Retry files contain no API keys and
  remain on the machine rather than joining Google Drive profile sync.
- **Keep storage failures from interrupting journal processing.** A report that
  cannot be saved is diagnosed in the log without stopping later journal events.
  Recovery keeps valid queued reports alongside damaged entries.
- **Keep passive notifications inside their panel.** Collection messages and
  other notifications show only the newest three messages by default. Adjust
  Visible message limit from 1 to 20 in the passive notification overlay
  settings. The panel stays within its height limit as messages arrive, and
  expiry continues normally.
- **Keep expanding overlays on screen.** Panels grow to the right on the left
  half of a monitor and to the left on the right half. Taller content shifts
  upward when it reaches the bottom edge. Panels retain their chosen corner
  when content shrinks or settings refresh, and combined game overlays stay
  within the game area.
- **Choose each panel's growth direction.** In the position editor, the arrow
  button beside Reset panel size cycles through Automatic, Grow left and Grow
  right. The choice is saved per panel and remains in place after moving it.
- **Keep your existing overlay layout.** Older saved positions load at their
  expected locations and gain the new growth metadata when you save. Growth
  direction changes preserve the panel's typography, spacing and wrapping.
- **Include nearby Exploration POIs by default.** Reference system only now
  starts unchecked, so searches include nearby systems within the chosen
  distance. Enable it whenever you want attractions from the reference system
  alone.
- **See when EDAstro requests a new data type.** The application log
  lists event types requested by EDAstro that this version cannot send. The
  notice appears when that unsupported list changes, without repeating on
  identical daily refreshes. Sharing continues to send only the supported,
  disclosed event types.
- **Understand EDAstro sharing requirements.** Configure EDAstro Sharing now
  explains that no application registration or approval is needed. Rejected
  batches are retained, and the log explains when sharing will retry.
- **Keep Exploration POI selection quiet.** Opening the workspace or filtering
  away every result no longer logs warnings about missing selected-POI details.
  Selecting another attraction continues to show its information normally.
- **Scale colonization text and checkmarks separately.** Completion checkmarks
  start 65% larger and follow the Icons slider. MARKET, LOAD and READY badges
  use Detail; project lists and collapsed-category summaries use Caption.
  Larger checkmarks and badges can extend beyond row edges without increasing
  row height; commodity and cargo-count font sizes determine the height instead.
- **Choose a quieter colonization table.** Carrier badges now read LOAD and
  READY. READY still requires enough cargo on the carrier itself. The new Hide
  row badges option hides status and assignment labels while keeping completion
  checkmarks, cargo counts and loading highlights. Changes apply immediately to
  live overlays and the position preview, and are saved for future sessions.
- **See distances when selecting destinations individually.** Next-jump
  information calculates the distance once matching destination coordinates are
  available from the existing system lookup. Plotted routes also retain distance
  information for their final jump, stale route origins cannot supply misleading
  distances, and arriving does not replace the displayed jump distance with zero.
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
  sharing cancels requests and clears retained reports. Unsent data
  expires after one day and can recover across application restarts. Success logs use
  fifteen-minute activity summaries.

## Update channel and packages

RC.64.4 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64.4`
- Tag: `xp2-v2.1.3.0-rc.64.4`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64.4-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64.4-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64.4-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64.4-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. The EDAstro upload protocol requires no
personal API key or application approval. POI catalogue use and journal sharing
have separate controls; journal sharing remains an opt-in choice.
