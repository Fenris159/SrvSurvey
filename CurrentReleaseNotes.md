# SrvSurvey-XP 2.1.3.0-rc.55

RC55 expands Mining and Surface Mining with Powerplay planning, planetary
prospecting, clearer sell routes, and cockpit overlays. It also improves market
searches and keeps Ardent price data available across sessions. These changes
follow the published [RC54 release](https://github.com/Fenris159/SrvSurvey/releases/tag/xp2-v2.1.3.0-rc.54).

## Mining in the cockpit

- The Mining activity overlay can keep several recent prospector results visible
  and retires an asteroid when its matching depletion report arrives. A separate
  cargo overlay shows capacity, remaining space, limpets, and cargo, with target
  minerals highlighted.
- Mineral thresholds and announcement presets have separate editors. Prospector
  announcements can use a chime or local speech on Windows and Linux.
- Platinum Spots ranks useful RES, overlap, and multi-hotspot rings, combining
  known ring references with commander discoveries and bookmarks.
- The Surface Mining deposit tracker shows saved rig capacity beside nearby
  material markers when a count is available.

## Powerplay mining

- The Powerplay tab can search ring and planetary mining opportunities for
  Acquire, Reinforce, and Undermine. Results pair mining sources with sell
  systems and stations according to the selected goal.
- Acquire searches work outward from eligible Fortified and Stronghold systems
  to find Unoccupied sell targets within their 20 or 30 ly reach. Reinforce and
  Undermine keep mining and selling in the same eligible system.
- Result tables show station prices and demand, colored material tags, power
  progress, expandable station and mining details, and connections between
  related systems. Fleet carriers are excluded from Powerplay sell stations.
- Searches support mineral, mining type, power, station pad, demand, freshness,
  state, and result-count controls. Saved settings and results return after a
  restart. Wider searches show increasing time warnings.

## Surface Mining and markets

- Surface Mining > Search finds landable body candidates and nearby sell systems
  for a selected material or Any. Body candidates use material-specific ground,
  volcanism, and host-star clues where available; surface reserve levels no
  longer hide otherwise suitable bodies.
- Sell and mining systems are shown in linked, expandable tables. Material tags
  use survey colors, while unrelated tags can be faded or hidden. Search results
  favor shorter mine-to-sell loops and can be restored after a restart.
- Mining > Find > Markets now offers clearer commodity and station-type choices,
  includes planetary materials, filters stations by landing pad and market
  criteria, and shows distance from the reference system in galaxy-wide results.
  Mining > Find > Rings has a ring-mineral chip selector with an Any option.

## Prices and search reliability

- Hotspot List, Surface Hunt, and Mining > Reference use refreshed Ardent
  average and maximum sell prices when available. The catalog is stored on disk
  so it can appear immediately after a restart; older catalog values remain
  available if a quote is missing.
- Station and provider responses are cached across searches where appropriate.
  Search requests use bounded retries, recover from intermittent body-search
  failures, and continue through paged results instead of stopping at an early
  request window.
- Commodity and Powerplay names are normalized across provider responses, and
  the planetary search checks the body's host star so a secondary white dwarf
  can qualify.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge for older
  clients. RC55 uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.55`
- Tag: `xp2-v2.1.3.0-rc.55`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.55-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.55-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.55-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.55-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
