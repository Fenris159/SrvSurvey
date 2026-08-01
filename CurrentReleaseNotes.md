# SrvSurvey Avalonia testing preview

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
- Direct overlay editing with drag positioning, live previews, global opacity,
  and per-panel opacity controls, plus a dedicated Overlay Settings workspace.
- Per-commander Route Manager and FC Routes libraries with import/export,
  favorites, sortable metadata, editable notes, direct activation and
  deactivation, and recoverable deletion. Their focused workspaces support
  progress-only saves and undo/reset controls, while normal jumps and carrier
  jumps advance only their matching route type. The two libraries are stored
  separately under the application profile's `Routes` folder, and only one may
  own next-hop auto-copy at a time. Spansh result URLs can be imported across
  its neutron, valuable-world, trade, tourist, fleet-carrier, galaxy,
  exobiology, and colonisation planner families.
- Expressway to Exomastery imports retain structured per-body biology targets
  separately from route notes. The Route Workspace groups those targets beneath
  each system, while a dedicated current-system route-biology overlay shares
  the same immediately persisted body-completion checkboxes.
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
- Some presentation details and platform-specific edge cases may continue to
  change while testing feedback is incorporated.

## For testers

Install the complete platform package rather than separating the executable
from its companion files. See the
[`Windows installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_WINDOWS.md) or
[`Linux installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_LINUX.md), and report defects or
suggestions through the
[`Fenris159/SrvSurvey` issue tracker](https://github.com/Fenris159/SrvSurvey/issues).
