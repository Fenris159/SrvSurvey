# Overlay vehicle exceptions and Firegroups

Requested behavior: rename Mining firegroups to Firegroups, move it to Status & utilities, remove its title header, show Group/Primary/Secondary vertically and default to the bottom-right. Each overlay settings page provides an Overlay Exceptions dialog with category allow lists grouped as Small, Medium, Large, Vessel / Vehicle, and Check All / Uncheck All controls.

## Behavior

All entries default to allowed. Filters use the current Status.json boarded flags and the shared JournalSessionState ship/SRV identity. On foot never falls back to a parked ship; fighters have one catch-all entry. Other / unknown lets users control unrecognized types or missing boarded information. Category settings persist in the existing UI settings document under OverlayVehicleAllowLists. An empty saved array disallows every entry, while an absent category preserves existing behavior.

The shared registry applies a distinct VehicleExcluded presentation reason, preserving domain intent and user visibility toggles. This affects separate/combined windows and the registry render sources used for stream/VR. Offline position previews do not use that runtime gate. A panel shared by multiple settings categories must be allowed by each. Global settings control the panels assigned to Global (Status & utilities); the Flight warning retains its existing Exploration settings ownership.

Firegroups retains its stable PlotMiningFiregroups identity, shortcut and commander settings. Existing custom placements remain intact; new/default placement is right/bottom with an eight-pixel margin. Firegroup bindings follow Status.json's current group aboard a ship, fighter or SRV, independently of mining-session and supercruise notification preferences. Notifications retain their existing mining-only rules.

## Catalog sources

Catalog verified 2026-09-07: 48 ship types plus Nomad, Scarab SRV, Scorpion, Rhino, Fighters, On foot and Other / unknown. Journal symbols come from [EDCD FDevIDs shipyard.csv](https://github.com/EDCD/FDevIDs/blob/master/shipyard.csv); ship sizes and display names come from [EDCD coriolis-data ship definitions](https://github.com/EDCD/coriolis-data/tree/master/ships). The newer Lynx Highliner is Medium per [Frontier's official update announcement](https://steamcommunity.com/app/359320/announcements/). No runtime download is required; unknown future types use the explicit fallback entry until the catalog is updated.

## Validation

Focused tests cover boarded vehicle versus mothership selection, on-foot/fighter precedence, recent ship symbols, persistence, all/none selection, category isolation, registry visibility restoration and compact vertical presentation. Headless dialog and overlay rendering supplement source checks; live gameplay remains a user validation step.
