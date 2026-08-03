# SrvSurvey-XP 2.1.3.0-rc.1

This is the first release candidate using the isolated SrvSurvey-XP release
identity. It is intended for active testing against Elite Dangerous before the
cross-platform branch is promoted to a stable XP release.

## Highlights in this release candidate

- Restores compact, borderless legacy presentation across the live overlays,
  keeps editor and live-drag positions synchronized, persists per-panel scale
  and opacity, and adds snap-to-center recovery for off-screen panels.
- Completes the dedicated Guardian renderer parity pass with legacy site-map
  geometry, POI colors, rotated glyphs, relic headings, active-obelisk effects,
  survey markers, a single legacy-exact map legend, Canonn biology indicators,
  and matching glossary previews.
- Restores the legacy Guardian survey workflow around that renderer: the live
  status panel exposes fire-group site and POI choices, interaction is limited
  to nearby points, published site metadata hydrates new surveys, Codex and
  material events record relics, obelisks, and beacons automatically, local-only
  discoveries remain reopenable, and surface proximity controls site lifetime.
  Aerial-origin alignment, on-foot relic-heading guidance, active-obelisk
  actions, and configured confirmation gestures now remain in sync with that
  dedicated status panel and their Guardian settings. Mission 2 now recognizes
  the legacy Guardian/Thargoid cargo pairs for all five log groups, upgrades
  old active-obelisk records with their published requirements, and removes
  stale known component rows when their material count reaches zero.
- Restores Guardian sharing conveniences with a clipboard-ready ZIP attachment
  and direct Discord channel launch with an invite fallback, while aerial
  screenshots use the commander-resolved Guardian layout. Batched screenshots
  retain the site active at each individual journal event and restore the
  legacy ruins/structure identity in filenames and banners. The Guardian site
  browser can also copy body names and commander notes or open that system's
  converted-image folder directly.
- Completes the Guardian parity audit with the original seven heading-guide
  images and structure-specific alignment geometry, encoded-material capacity
  warnings, blueprint and needed Ram Tah details in the system overlay, and
  current/target/missing-artifact styling in the Ram Tah overlay. Raw relic
  heading edits now persist, mission events are processed in journal order,
  and the site browser restores all twelve sortable legacy columns plus its
  converted-image indicator. Screenshot naming also restores the `(HighRes)`
  suffix and recent live-status fallback used by legacy data banners.
- Recovers the active Guardian site from the nearest published or commander
  record while within the legacy 4,000 m altitude gate, even when Elite does
  not repeat `ApproachSettlement`, and switches cleanly between sites on the
  same body. Legacy `.p`, `.m`, and `.e` point-state commands are restored, and
  an explicit `.map` command can open a valid map whose recorded heading is 0°.
- Keeps Guardian map markers proportional to the background at every scale and
  adds a bottom 1x-10x zoom bar, contained mouse-wheel zoom, bounded click-drag
  panning, and automatic fitted-center recovery when returned to 1x.
- Keeps Galaxy Map overlays context-sensitive, preserves neutron and refuel
  badge colors, and closes all overlay windows when the main application exits.
- Restores legacy overlay appearance rules across ship, SRV, on-foot, and menu
  modes. Galaxy Map uses the journal-music fallback, lets explicit system
  selections replace route details, keeps notifications and legacy forced
  panels eligible, and returns automatic next-hop guidance to supercruise;
  unrelated overlays temporarily hide. Main-menu,
  shutdown, missing-session, suit, and optional active-build-project gates are
  applied consistently.
- Restores the legacy overlay transition state machine across biology, body,
  FSS, route, search, station, surface, settlement, combat, quest, and Guardian
  panels. System biology now changes to body detail only while actually near a
  biological body (including the post-DSS grace interval), temporary map-body
  previews survive journal refreshes for their original timed duration, and
  stale or foreign body targets are rejected. The Guardian site map and
  Guardian status/glide guidance are again independent panels with their own
  saved positions, and jump guidance suppresses only the status panel.
- Contains mouse-wheel input inside the hovered panel, prevents live Diagnostics
  logging from changing the page position, and removes repeated nullable
  binding failures seen in runtime logs.
- Adds SrvSurvey-XP development and stable update channels. Development is
  enabled by default from `Fenris159/SrvSurvey`; opting out selects future
  stable XP releases from `njthomson/SrvSurvey` and reports N/A until one exists.
- Adds bottom-edge update notifications, checksum-verified portable Windows
  replacement, rollback protection, and direct manual AppImage instructions.

## Packaging

- Version: `2.1.3.0-rc.1`
- Tag: `xp-v2.1.3.0-rc.1`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.1-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.1-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.1-x86_64.AppImage`

The Windows and Linux packages are self-contained. AppImages must be updated
manually; the application links directly to the selected XP release.

## Testing notice

> [!IMPORTANT]
> This is a work-in-progress preview for testing. It is not yet presented as a
> stable replacement for the established SrvSurvey application. Keep a backup
> of your existing SrvSurvey data and report unexpected behavior through the
> project issue tracker.

## About this port

SrvSurvey has been rebuilt as a cross-platform desktop application on .NET 10
and Avalonia UI 12.1. Portable game, journal, storage, migration, network, and
domain behavior lives in `SrvSurvey.Core`; the Avalonia desktop project supplies
the Windows and Linux interface, overlay rendering, input, theming, and native
platform adapters. Release automation produces self-contained Windows x64 and
Linux x64 packages, a Linux AppImage, package manifests, checksums, and SPDX
software bills of materials.

The goal is feature parity with the established application while replacing
Windows-only presentation and integration code with tested cross-platform
implementations. The conversion has reached a functional testing-preview stage:
the primary exploration, exobiology, travel, Guardian, settlement, combat,
quest, colonisation, notification, journal, and overlay workflows are present,
and automated inventories cover the supported journal and overlay contracts.
Native runtime validation with Elite Dangerous on clean Windows, X11, and
XWayland systems is still in progress.

Detailed engineering and validation status is maintained in
[`docs/DEVELOPMENT.md`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/DEVELOPMENT.md).

## Additional features introduced by this port

- A Frontier Commander Console with PKCE account linking, protected per-account
  credentials, automatic journal identity matching, manual linked-commander
  selection, and compact Commander, Current Ship, Fleet Carrier, Market,
  Shipyard, and Community Goals views.
- Community Goal enrichment from Inara's public application feed, combined
  conservatively with Frontier CAPI data and commander-isolated local journal
  history so recent personal contribution and reward information can be
  restored without uploading it.
- Direct overlay editing with drag positioning, production-faithful live
  previews, global opacity and scale, and per-panel opacity and scale controls,
  plus a dedicated Overlay Settings workspace.
- Per-commander Route Manager and FC Routes libraries with import, native
  backup export, portable Spansh JSON and universal CSV exports, in-place route
  renaming, favorites, sortable metadata, editable notes, direct activation
  and deactivation, and recoverable deletion. Their focused workspaces support
  progress-only saves and undo/reset controls, while normal jumps and carrier
  jumps advance only their matching route type. The two libraries are stored
  separately under the application profile's `Routes` folder, and only one may
  own next-hop auto-copy at a time. Spansh result URLs can be imported across
  its neutron, valuable-world, trade, tourist, fleet-carrier, galaxy,
  exobiology, and colonisation planner families.
- Expressway to Exomastery imports retain structured per-body biology targets
  separately from route notes. The Route Workspace groups those targets beneath
  each system, while a dedicated current-system route-biology overlay shares
  the same immediately persisted body-completion state. The compact live and
  editor views share body artwork, themed completion controls, wrapped details,
  and a three-body scrolling viewport. The icon glossary documents every body,
  refuel, and neutron-route asset used by these workflows.
- Fleet Carrier routes retain Spansh logistics for distance remaining, jumps,
  fuel, market tritium, jump usage, icy-ring conditions, and restock amounts.
  A dedicated compact overlay advances from carrier journal events and reports
  the active jump-sequence phase and countdown beside the next route hop.
- High-frequency Commander, survey, search, colonisation, station, and site
  projections retain stable UI identities between unchanged updates. This
  reduces polling overhead and prevents bound checkboxes, expanders, and other
  interactive controls from being recreated while they are in use.
- Application shutdown now cancels owned journal reconstruction and boxel-audit
  work. Network, localization, profile, and reference-data paths also receive
  bounded regular-expression handling, culture-independent timestamp parsing,
  and HTTPS-only download redirects where applicable.
- Cross-platform overlay hosting and Linux-specific X11/XWayland integration,
  including KDE-aware window hints while preserving the standard fallback.
- Hardened opt-in EDDN delivery with a durable outbox, schema validation, and a
  test-schema option that continues to use EDDN's live gateway.
- Backup-first import of existing SrvSurvey profiles, transactional reference
  data and application updates, health confirmation, and rollback safeguards.

## Current testing boundaries

- Pure native Wayland is not yet a full-functionality target. Linux overlays
  currently require native X11 or an XWayland `DISPLAY` for game-window
  tracking, click-through behavior, capture, and global input.
- Automated builds and regression suites do not replace live testing with Elite
  Dangerous. Overlay behavior, game attachment, account switching, updates,
  rollback, and imported profiles should be exercised on representative clean
  systems before this port is promoted as stable.
- Windows and Linux continuous-integration builds now publish test coverage to
  SonarCloud so new-code reliability, security, maintainability, duplication,
  and coverage gates are evaluated together before release changes are merged.
- Some presentation details and platform-specific edge cases may continue to
  change while testing feedback is incorporated.

## For testers

Install the complete platform package rather than separating the executable
from its companion files. See the
[`Windows installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_WINDOWS.md) or
[`Linux installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_LINUX.md), and report defects or
suggestions through the
[`Fenris159/SrvSurvey` issue tracker](https://github.com/Fenris159/SrvSurvey/issues).
