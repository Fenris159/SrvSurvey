# SrvSurvey-XP 2.1.3.0-rc.77.11

RC77.11 expands backup coverage for saved preferences, Colonization planning,
and workspace continuity, keeps screenshot and hardware configuration specific
to each computer, makes outstanding ring scans clearer in System status, and
improves shared journal data quality while preserving each provider's format.

## What's changed

### Portable preferences and saved work

- **Restore Colonization planning:** Backups now include plans, shopping and
  wish lists, retained project scope, checked carriers, project focus, and
  hidden plans for shared and Commander-specific settings.
- **Keep Exploration choices:** Exploration POI preferences, automatic value-reset
  preference, active Journey reference, and active Boxel/sphere configuration
  and progress are included alongside existing saved searches and Journey data.
- **Preserve recorded progress:** Include empty-boxel records, Ram Tah log
  progress, and local quest development data without duplicating quest recovery
  archives.
- **Resume workspace tabs:** Exploration, Colonization, and Diagnostics now retain
  their selected tabs across restarts and restores, alongside the workspaces
  already covered.

### Configuration for the original machine

- **Keep screenshot settings local to each computer:** Back up all screenshot
  processing options and folders in the machine section. They restore only when
  explicitly restoring hardware settings on the originating machine and operating
  system. Screenshot images and conversion queues are not backed up.
- **Recover local setup:** Include the preferred startup Commander, journal-folder
  configuration, and VR mode/VDXR calibration overrides. Portable restores leave
  the destination computer's configuration unchanged.
- **Read VR layouts consistently:** Capture and restore calibration overrides
  under their owning layout lock without following linked folders outside the
  profile.

### System status overlay panel

- **Group rings with their body:** Show one body tag with nested borders
  separated by 1 px gaps for its unscanned rings instead of separate ring-suffix
  tags. Count each body once and retain its tag until the body and included rings
  are mapped.
- **Use configured theme colors:** Inner-to-outer borders identify rings A, B,
  and C using General Warning, General Success, and General Success (dim).
  Each border and its adjoining spacing disappear after its ring is DSS-scanned.
- **Keep existing controls and context:** Respect Skip DSS of rings and body
  filters, preserve navigation destination highlighting and the separate
  biological-body list, and use the same presentation in the overlay editor.

### Shared journal data quality

- **Avoid duplicate survey reports:** EDDN, EDSM, and VoxStellar retain
  bounded content receipts for eligible observations across restarts. New
  timestamps, changed observations, and different Commanders remain distinct.
  Personal transactions and EDAstro docking records retain their source identity.
  EDAstro suppresses duplicate pending survey reports; its aggregate response
  cannot confirm each item. Rejected reports do not become successful content
  receipts, including EDSM per-event processing failures.
- **Keep location context accurate:** Clear EDDN's tracked body after leaving it,
  retain known Codex body names without inventing body IDs, and prevent EDSM
  events from inheriting conflicting system, station, or coordinate metadata.
- **Expand supported EDDN contributions:** Send organic Log/Sample scans with
  coordinates only from a matching event-time Status snapshot, and detailed
  outfitting catalogs with module IDs and prices when available. Keep live
  Fleet Carrier Materials sharing; no
  FCMaterials CAPI collection is added.
- **Respect provider rules:** Exclude temporary Operations stations from EDDN,
  honor each other service's accepted event types, and identify new uploads with
  the full RC version. Preserve actual market prices, names, physical values,
  event timestamps, retry pacing, and Stealth Mode delays.

### Backup compatibility and safeguards

- **Preserve reopened shopping searches:** Keep each saved search together when
  project tags are cleared and a search is reopened across successive backups,
  retaining its latest carrier choices and wishlist. Upgrade older flattened
  search backups and sync baselines before capture or restore, preserving their
  field-change history and resolved wishlist choices. Reject contradictory object
  records or oversized upgraded backups before changing files, and avoid repeated
  work on long legacy paths.
- **Recover missing exploration totals:** A preferences-only Commander restore
  allows the available journal bootstrap to rebuild missing exploration counters.
  Existing local totals, including saved zero totals, keep their replay guard;
  other restored progress stays protected.
- **Handle older screenshot backups:** Move legacy portable screenshot settings
  into their original machine section when reading backups. Existing machine
  values take precedence. Restores staged by an older release skip formerly
  portable screenshot settings; reselect the backup for an explicit same-machine
  restore that includes them.
- **Keep private and live data excluded:** Select only approved fields from
  Commander profiles. Credentials, raw journals, live cargo and mission snapshots,
  exploration history indexes, provider caches, and pending API uploads stay local.
- **Catch future coverage gaps:** A settings inventory check requires new stores
  to declare whether their configuration is portable, machine-specific, or excluded.

Update every linked computer before synchronizing backups with the additional
settings. Older releases do not recognize all of the new backup sections.
