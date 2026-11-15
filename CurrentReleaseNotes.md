# SrvSurvey-XP 2.1.3.0-rc.77.18

RC77.18 adds direct Raven Colonial body catalog updates and recovery from
Spansh and EDSM in the System Editor.

## What's changed

- **Upload complete local scans:** The System Editor follows upstream's scan
  completion and body-count checks. Relevant discovery, FSS-completion, and
  nav-beacon events can update Raven when its catalog has fewer bodies than
  expected. Individual body scans do not each trigger an upload, and startup
  history and diagnostic replay do not publish automatically.
- **Refresh scanned bodies manually:** Use **Upload scanned bodies** to replace
  Raven's catalog from complete local scan records, including when Raven already
  has the same number of bodies. Local-scan uploads follow upstream behavior
  without the rediscovery removal warning.
- **Rediscover Absent Bodies:** The red button beside **Guide** queries both
  Spansh and EDSM for the system currently loaded in the editor. You can load
  another system and recover its bodies without travelling there or having
  local scans. Results combine by journal body ID and include barycentres and
  asteroid belts.
- **Review proposed removals:** If web rediscovery would remove bodies already
  stored in Raven, a pop-up lists their names and asks **Yes** or **No**.
  Choosing **No** leaves Raven unchanged. This warning applies to web
  rediscovery only.
- **Keep recovered data consistent:** Rediscovery preserves populated Raven
  properties and feature flags for matching bodies, fills missing or invalid physical
  values, and normalizes provider radius and gravity units. Invalid identities,
  unavailable providers, and obsolete editor selections stop the update.
- **Preserve site edits:** Body updates refresh the editor's body choices without
  publishing or discarding your unsaved site changes.
- **Recover after import errors:** A failed Raven body import keeps the loaded
  system available for web rediscovery or complete local scans. Recovery also
  clears an obsolete import prompt so site changes can still be reviewed.
- **Use the correct journal history:** Local backfill follows the commander's
  actual monitored folder, keeps each record's commander and Live/Legacy galaxy
  separate during journal rotations, and orders
  older and newer journal filename formats correctly. Verified startup history
  still enriches local sites when the game has already closed or the next
  journal identity is pending. Body counts use the same ten-jump window for
  cached and on-disk records; older details can enrich a newly scanned body.

- **Handle journal handoffs:** Backfill no longer counts a jump twice when live
  monitoring catches up. Jump and scan events retain their journal order even
  when timestamps tie or the clock moves backward. A backfilled FSS event still
  starts its normal check when delivered
  live.
