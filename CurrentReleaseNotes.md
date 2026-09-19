# SrvSurvey-XP 2.1.3.0-rc.49.2

RC49.2 is a Linux Wayland screen-capture patch on RC49.1. Surface Mining
rig detection could finish the desktop share picker and then fail immediately
when PipeWire took the portal file descriptor.

## Patch since rc.49.1

### Wayland / Surface Mining rig detection

- PipeWire portal startup no longer uses `DllImport SetLastError`, which
  .NET rejects when runtime marshalling is disabled. That exception paused
  rig detection after a successful monitor or window share.
- Capture logs now record each portal/PipeWire step, and failures include
  the full exception chain plus the SrvSurvey/PipeWire throw site. The next
  capture problem should be visible in `~/.local/share/SrvSurvey/logs`
  without a screenshot.
- A failed PipeWire start after a successful picker is retried after
  backoff instead of sticking until the app is restarted. User cancel and
  desktop deny still stay terminal.

## What's changed since rc.49.0

### Colonization architect and helper access

- Load system succeeds only when the architect is unassigned or matches the
  active commander. Anyone else is refused with a not-the-architect warning.
- Architects see every planned Raven site. Helpers only see orbital planned
  sites that resolve in the Raven build catalog.
- Helpers cannot scratch-create a project; they can only work from a visible
  planned site.
- Build Type is a categorized Raven catalog dropdown (unmatched original
  first). Market ID is digits-only, with a friendly warning when extra
  characters are removed.

### Construction shopping overlay

- The commodities overlay shows the full list on screen by default instead of
  compacting into a ~15-row scroller.
- Colonization overlay settings add **Use compact/scrolling commodities list**
  under **Collapse cargo groups when enough on FCs**. It is off by default;
  turn it on to restore the previous compact scroller.

### Fleet Carrier cargo and workspace

- Seeds RavenColonial Fleet Carrier cargo from Frontier CAPI at most once per
  carrier per session, and never while docked (market/journal is fresher).
  Later updates use journal deltas.
- Queues cargo deltas while a Market.json or server baseline is in flight,
  then replays them so dock-time transfers are not lost.
- Fleet Carrier workspace can switch between personal `/fleetcarrier` and
  nested squadron carrier data from `/squadron`.

### Inara uploads

- Caps commander writes at **2 POSTs per rolling minute** to
  `https://inara.cz/inapi/v1/`. Overflow waits and batches; Shutdown force
  flush and payload splits respect the same budget.
- `MiningRefined` still updates local cargo but no longer emits inventory
  snapshots. Inventory-only queues wait 10 minutes unless a travel or other
  non-inventory event is already going out.
- Header 400 rate-limit / temporary revoke requeues with backoff. Invalid API
  key still drops the batch. Secrets never appear in warnings or detail logs.
- Main app log gets EDSM-style 15-minute success aggregates. Accepted events
  go to `logs/inara-accepted.txt` (event name, timestamp, system/station; 12
  hour rolling retention). A throwing or unwritable log cannot fail or
  duplicate an accepted upload.

## Packaging

- Version: `2.1.3.0-rc.49.2`
- Tag: `xp-v2.1.3.0-rc.49.2`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.49.2-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.49.2-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.49.2-x86_64.AppImage`

Windows and Linux packages are self-contained. Linux packaging tools and the
AppImage runtime use versioned, checksum-verified downloads. AppImages are updated
manually through the selected XP release. Numeric Windows FileVersion remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
