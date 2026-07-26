# Legacy Data Migration Parity

Last audited: 2026-07-26

The migration boundary is the original SrvSurvey data directory selected by the
player. Import is recursive and content-agnostic: every regular file and empty
directory is inventoried, size- and SHA-256-identified, copied into a permanent
backup, verified, merged with current-only Avalonia data in an isolated staging
directory, rechecked against the unchanged source and destination, and only
then activated. Symbolic links and junctions are rejected rather than followed.
Any collision is recorded in the import manifest; the imported legacy file wins
at that path while the previous Avalonia file remains in the verified backup.

The source is never rewritten or removed. An activation failure restores the
previous destination, a repeat import is refused by the activated manifest, and
malformed legacy files are retained byte-for-byte even when their optional
typed conversion is skipped.

| Data family | Migration behavior | Verification evidence |
|---|---|---|
| Commander profiles and arbitrary future files | Recursive, byte-identical import with current-only merge and collision manifest | Source/backup/activation inventory equality, pre- and post-swap drift refusal, injected rollback tests |
| Application preferences | Known legacy settings translate into `cross-platform-ui.json`; unknown current sections and bindings survive later writes | Complete 133-control audit, idempotence, malformed-source refusal, incremental migration tests |
| Overlay appearance | Legacy `theme.json` remains the independent in-game overlay palette | RGB/ARGB/HTML/reference parsing, unknown-colour retention, named-state tests, end-to-end import/theme-isolation test |
| Overlay placement and VR calibration | Legacy `plotters.json` remains the layout source | All anchors, offsets, opacity, default opacity, unknown entries, and VR suffixes round-trip; verified backups precede edits |
| Retired organic history | Additive conversion into compatible commander/system history | Byte-identical source retention, idempotence, weak-ID repair, reward-overflow and malformed-shape refusal |
| System/body exploration history | Read directly through the lossless legacy system store and merge with journal-authoritative state | Body/parent/ring/organism/geology projections, unknown-field retention, serialized atomic writes |
| Guardian surveys | Imported compatible survey files are read after byte-identical source/backup/activation verification | Legacy POI/obelisk/material forms, anomaly preservation, lossless editor/store tests |
| Local development quests | Legacy definitions and progress remain readable and are updated only through verified atomic saves | Complete known-field loading, unknown-field retention, definition preservation, malformed-file refusal |
| Cached Codex images and local flora | Imported folders move inside the new data root and migrated paths are rewritten to those verified copies | Byte fixtures and path-rebinding tests |

`LegacyProfileThemeImportParityTests` exercises the control-group boundary the
application presents to users: it imports a custom overlay palette and layout,
applies the migrated application theme, changes the application theme again,
and proves that neither operation modifies or reapplies the overlay palette.
Overlay theme presets are stored separately in `overlay-theme-states.json` and
do not participate in application theme selection.

The remaining real-profile restart comparison is a final runtime validation,
not a missing migration implementation: it will compare a user-selected legacy
profile, its verified backup, and the first restarted Avalonia projection before
any live UI or overlay changes are accepted.
