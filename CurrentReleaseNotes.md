# SrvSurvey-XP 2.1.3.0-rc.69.7

RC69.7 improves Colonization market searches with local-stock exclusions,
selectable data sources, and clearer ShoppingList information. Colonization
overlays now default to hiding outside recognized ships through their existing
vessel and vehicle exceptions.

## What's changed

- **Exclude commodities available locally.** The new **Exclude Local Commodities**
  button after **Select None** checks markets in the selected projects' systems
  and deselects commodities whose remaining needs are covered by recorded local
  stock. The existing loading indicator shows progress, and the button uses a
  secondary highlight while enabled. Turning it off restores your manual choices.
- **Keep local exclusions consistent.** Local-stock checks include surface ports
  even when **Exclude surface ports** hides them from search results, and ignore
  market age. Coverage is recalculated as needs, ShoppingList allocations,
  selected projects, carrier cargo, or applicable filters change. The toggle and
  cached local stock survive sessions. ShoppingList allocations are deducted from
  the matching market stock before coverage is calculated. Refreshed local
  coverage stays current even if the wider search fails.
- **Choose market data sources.** **Data Source:** checkboxes beside the arrival
  filter let you use Spansh, Ardent, or both. Both start enabled, your selection
  is saved, and searches and local-stock checks only contact selected providers.
- **Show markets in the reference system.** Searches include origin-system
  markets at **0 ly**. **Include outdated local markets** can show older local
  stock with an **Outdated** label, observation date, and warning. The market-age
  limit still applies to results in other systems. Ardent local estimates have
  no additional one-year cutoff when older local markets are enabled.
- **Recover oversized Spansh search pages.** Searches retry with smaller pages
  when a large station response exceeds the response-size limit, retaining
  results already fetched instead of immediately stopping the provider search.
- **Clarify ShoppingList quantities.** Commodity tooltips separately show
  **Still to plan** and **On ShoppingList here**, so zero unplanned tonnes cannot
  be mistaken for a completed need when cargo is still on the list. Each tooltip
  line translates independently while retaining its layout and theme colors.
- **Keep provider warnings concise.** Repeated local checks no longer duplicate
  warnings, including after restoring the shopping workspace.
- **Fix the Auto Refresh animation warning.** The Colonization refresh indicator
  now animates without repeatedly logging a missing rotation-transform warning.
- **Hide Colonization overlays outside ships by default.** Every entry under
  **Colonization → Overlay Exceptions → Vessel / Vehicle** starts unchecked:
  Nomad, Scarab SRV, Scorpion, Rhino, Fighters, On foot, and Other / unknown.
  Small, Medium, and Large ship defaults stay enabled. This applies to both
  Colonization commodities and ShoppingList overlays; saved choices take
  precedence and can still be changed through the existing exceptions window.

## Update channel and packages

RC69.7 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.69.7`
- Tag: `xp2-v2.1.3.0-rc.69.7`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.69.7-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.69.7-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.69.7-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.69.7-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Market stock is an estimate from the
selected providers. Local exclusions intentionally accept old stock; verify
availability before travelling. Existing saved overlay exceptions are preserved.
