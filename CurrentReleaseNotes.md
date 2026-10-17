# SrvSurvey-XP 2.1.3.0-rc.66

RC66 adds a colonization shopping workflow: choose the projects you are working
on, find nearby markets, and allocate purchases into a persistent wish list.
A matching in-game overlay keeps that list within reach. Fleet Carrier manifest
editing, carrier stock coverage, and optional live project refresh help teams
coordinate their construction supplies.

## What's new

- **Choose projects for shopping.** The colonization workspace lists linked
  planned sites and active builds. Use the **Shop** checkboxes and **Search Markets**
  to combine their remaining commodity needs in a separate window. The existing
  **Show** selection continues to control the cargo report independently.
- **Search Spansh and Ardent together.** Start near your current system or choose
  another system with the shared autocomplete. Set the radius, market age,
  minimum pad size and arrival distance, and exclude carriers or surface ports.
  Compact station rows show the system, distance, arrival distance and matching
  goods, with green/red stock comparisons. Expand a row to inspect its goods;
  click a system name to copy it.
- **Filter the goods you want to buy.** Toggle individual need tags, or use
  **Select All** and **Select None**. Hide **Short supply** offers and sort by
  **Most commodities available** to compare useful stops. The controls and tags
  wrap to fit smaller windows, including readable monochrome highlights.
- **Account for goods already loaded.** **Use Carrier** defaults to **ALL** and
  includes every carrier linked to the commander. **LOADED** estimates the share
  of each project's remaining needs covered by that stock, with surplus capped
  for each commodity. The choice is retained per commander and sets the starting
  carrier for the manifest editor and the search's **FC Diff** selector. FC Diff
  starts enabled and can use all linked carriers or one carrier.
- **Allocate a wish list without overbuying.** Click a station name to allocate
  its offered goods, or click individual commodities to choose your stops.
  Purchases are capped by station stock and remaining need; allocated quantities
  reduce the needs and offers still shown. **Wish List** opens a compact station
  and system tree with quantities to buy. Remove individual goods or whole stops
  with the red remove buttons to return their quantities to the search.
- **Keep the plan between sessions.** **Reopen Search** restores saved results,
  filters and the wish list for the same selected projects. **Refresh** updates
  project needs from Raven Colonial before fetching market stock. Starting or
  refreshing a market search, changing the selected projects, or using
  **Clear Search** starts a fresh wish list. Project-only refreshes reconcile
  the existing list against the updated requirements.
- **Follow the wish list in-game.** A nonempty list appears in a themed passive
  overlay alongside the existing colonization panels. Its default position is
  the left-center of the monitor, its width follows the text, and its contents
  scroll after reaching half the screen height. Overlay alignment controls work
  as usual. Interactive mode enables removing goods or stops and copying system
  names directly from the panel.
- **Edit linked Fleet Carrier manifests.** **Edit Carrier Manifest** opens a
  compact, category-grouped editor for all commander-linked carriers, including
  teammates' carriers. Select the carrier, update its system using autocomplete,
  edit numeric quantities, add goods, remove them with the mini trash buttons,
  or switch between table and text entry. Save updates its Raven Colonial
  location and manifest.
- **Compare with local carrier observations.** The read-only **Journal** column
  shows available CAPI totals plus later journal transactions; unavailable data
  displays **N/A**. **Hide Non-Colony Commodities** starts enabled, preserves
  hidden cargo when saving, and shows other goods in their proper categories
  when turned off. Frontier refresh enforces five minutes between attempts,
  including after reopening the editor or restarting SrvSurvey.
- **Follow team progress automatically.** The optional **Auto Refresh** control
  beside **Refresh Projects** fetches project data every 60 seconds and animates
  while active. It updates the needs, carrier coverage and wish-list quantities
  without replacing saved market results. It is session-only, stops on commander
  changes, and turns itself off after one hour; hover help explains these limits.

## Reliability

- Spansh searches combine the required commodities and reuse saved search pages.
  Ardent allows up to three nearby commodity requests at once. Partial provider
  results remain usable, repeated limit warnings collapse into a short summary,
  and a complete provider outage retains the previous market results.
- Carrier response errors stay in the editor instead of escaping its event
  handlers. Journal comparisons accept newer CAPI totals after docking at another
  port, and older saved FC Diff preferences behave consistently before and after
  reopening Search.
- Search tables retain their columns after resizing. Numeric manifest entry uses
  normal caret placement and rejects non-numeric typing and pasted text.

## Update channel and packages

RC66 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.66`
- Tag: `xp2-v2.1.3.0-rc.66`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.66-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.66-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.66-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.66-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Market stock and carrier comparisons use
available observations and can lag in-game changes. Broad market searches can
still take a long time; refresh or narrow the search when needed. Carrier edits
update Raven Colonial's records and do not move an in-game carrier or transfer
physical cargo.
