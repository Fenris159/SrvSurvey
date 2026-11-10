# SrvSurvey-XP 2.1.3.0-rc.77.11

RC77.11 expands backup coverage for saved preferences, Colonization planning,
and workspace continuity, keeps screenshot and hardware configuration specific
to each computer, and makes outstanding ring scans clearer in System status.

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

- **Group rings with their body:** Show one body tag with nested, touching
  borders for its unscanned rings instead of separate ring-suffix tags. Count
  each body once and retain its tag until the body and included rings are mapped.
- **Use configured theme colors:** Inner-to-outer borders identify rings A, B,
  and C using General Warning, General Success, and General Success (dim).
  Each border disappears after its ring is DSS-scanned without leaving a gap.
- **Keep existing controls and context:** Respect Skip DSS of rings and body
  filters, preserve navigation destination highlighting and the separate
  biological-body list, and use the same presentation in the overlay editor.

### Backup compatibility and safeguards

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
