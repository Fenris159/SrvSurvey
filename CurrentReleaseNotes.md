# SrvSurvey-XP 2.1.3.0-rc.69.5

RC69.5 improves surface-navigation responsiveness, expands ShoppingList planning,
and makes Raven architect systems easier to manage. Surface maps move more
smoothly during flight, shopping plans can stay active across searches and
sessions, and System Editor remembers the commander's architect systems.

## What's changed

- **Receive surface updates sooner.** Game-file change notifications wake the
  existing journal monitor without waiting for its next polling interval.
  Polling remains available when notifications are missed or unavailable.
- **Smooth map movement during flight.** The four surface overlays draw
  intermediate positions using recent movement, with prediction limited to one
  second. Fresh reports correct the drawing, and incompatible or stale data
  resets prediction. SRV and on-foot positions continue using direct updates.
- **Respond to reported facing.** Short heading corrections ease over 80 ms
  using the shortest turn; changes greater than 90 degrees apply immediately.
  Facing continues to use the game's reported heading.
- **Keep survey decisions accurate.** Smoothing affects overlay drawings.
  Distance labels, proximity decisions, recorded survey points, and stored
  coordinates continue using the game's reported position.
- **Spot ShoppingList selections in market results.** Commodity tags under each
  location start with the secondary style and become highlighted when added to
  ShoppingList. Short-supply tags retain their warning outline and use the
  warning fill when selected.
- **Start market planning with tracked projects.** On first opening Market
  Search, Select Projects defaults to the focused project, or all projects
  checked under Show when there is no focus. Planned projects are included.
  Existing market selections remain saved independently, including clearing all
  project tags.
- **Choose architect systems in System Editor.** A searchable system picker
  lists the active commander's Raven architect systems, with six rows visible
  before scrolling. Type a name prefix to filter the list. The catalog survives
  restarts and refreshes at most once a day automatically; **Refresh Systems**
  updates it on demand. **Use Current** selects your live location, including
  systems outside the catalog. A selected remote system stays selected while
  you travel, and failed catalog refreshes retain the cached list.
- **Exclude navigation beacons from detected build sites.** System Editor
  ignores navigation-beacon signals and selected destinations, including
  plain-text and compromised beacon names, so they cannot be accidentally
  added to site changes published to Raven Colonial.
- **Estimate ShoppingList trips for each destination.** In both the ShoppingList
  window and overlay, each station shows estimated trips below its commodities.
  A footer totals those station estimates using the current ship's cargo
  capacity. Changing ships,
  removing selections, and buying commodities update both ShoppingList views.
  Market purchases reduce the smallest matching commodity allocation first,
  regardless of where the purchase was made, and completed entries disappear.
  Saved purchase tracking prevents repeated deductions when journal events
  are replayed; project delivery progress is unchanged by shopping purchases.
- **Keep a ShoppingList while searching again.** **Lock List** preserves its
  quantities and station details across searches, changed project selections,
  canceled requests, and restarts. Items remain editable and purchases still
  reduce the list. **Clear List** explicitly empties it, including while locked.
  Both buttons sit beside **Open ShoppingList**, and an enabled lock uses the
  secondary highlight.

## Update channel and packages

RC69.5 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.69.5`
- Tag: `xp2-v2.1.3.0-rc.69.5`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.69.5-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.69.5-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.69.5-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.69.5-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Prediction cannot reveal turns or
acceleration that the game has not reported, and corrections can still produce
visible position changes. Post-change live flight validation remains pending.
