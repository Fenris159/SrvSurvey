# EDDN synchronization

SrvSurvey Avalonia can publish supported live Elite Dangerous data to the
Elite Dangerous Data Network (EDDN). This feature is disabled by default and is
independent of Spansh, Inara, Raven Colonial, Canonn, and every local journal
consumer.

## Consent and identity

Open **Configure EDDN Sharing** under Settings > Privacy to review the
disclosure and opt in. The button appears immediately above the Inara section.
EDDN does not require an account, application token, or personal API key. The
standard EDDN header contains the Commander name as its `uploaderID`; EDDN
obfuscates that value before distributing messages to listeners.

Each journal series, Commander name, and Frontier ID identifies one isolated
publishing session. The session captures its own immutable EDDN header and owns
location, crew, batching, companion-file, and deduplication state. Changing
Commander or journal series disposes the old session and cancels its unfinished
companion-file reads. Messages already in the application-owned outbox keep the
header captured when they were created, so a later Commander cannot relabel
them.

Disabling sharing immediately stops new queue entries and removes the local
pending-upload records. Startup journal history and multicrew activity are not
published. When multiple Elite windows are detected, all EDDN queueing and
delivery pauses because shared companion files and active-window state cannot
be attributed safely. Pending uploads are preserved and resume in order after
only one Elite window remains.

Enable EDDN uploads in only one application at a time, such as SrvSurvey or
EDMC. Running multiple EDDN uploaders can create duplicate submissions.

## Published schema families

The publisher maps source journal data to the current EDDN schema families:

- `journal/1`: Docked, FSDJump, CarrierJump, Scan, Location, and
  SAASignalsFound, including system, body, and scan data allowed by the schema.
- Dedicated journal schemas: CodexEntry, ApproachSettlement, DockingGranted,
  DockingDenied, FSSAllBodiesFound, FSSBodySignals, FSSDiscoveryScan,
  NavBeaconScan, and ScanBaryCentre.
- `fsssignaldiscovered/1`: filtered, system-scoped signal batches.
- Companion-file schemas: commodity, outfitting, shipyard,
  `fcmaterials_journal`, and NavRoute.

Localized strings and commander-specific fields are removed recursively.
Companion files must match the triggering event, timestamp, and MarketID before
they can be queued. Repeated station snapshots are deduplicated.

## Delivery and failure isolation

Every accepted payload is persisted as one atomic record under
`eddn-outbox-v1.json.d` before its first network attempt. The previous
single-array outbox is migrated when found. An exclusive cross-process lease
ensures that only one SrvSurvey process can read or write the outbox. A single
background sender uses gzip and exact HTTP/1.1 and spaces attempts by at least
400 milliseconds. First attempts retain durable creation order. A transiently
failing message waits at least one minute and backs off to thirty minutes while
other due messages remain eligible, so one bad payload cannot block the queue.
Invalid and permanently rejected payloads are removed independently. The
persisted backlog is capped at 4,096 messages and 64 MiB so a prolonged outage
cannot grow it without bound.

The journal reducer, overlays, local persistence, Raven Colonial, Canonn,
Spansh, and Inara integrations do not wait for an EDDN network request. Queue
logging occurs after synchronization locks are released, and shutdown cancels
workers without disposing synchronization primitives underneath them.

This release always uses EDDN's Live upload gateway with production schema
references. The mode is fixed internally and is not a user preference. Existing
queued records are normalized to remove any legacy `/test` suffix before
delivery.

Spansh remains a separate, read-only lookup integration. Its nearby setting
only chooses the source of system last-updated timestamps and never sends
journal data to Spansh or EDDN.
