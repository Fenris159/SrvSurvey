# EDDN synchronization

SrvSurvey Avalonia can publish supported live Elite Dangerous data to the
Elite Dangerous Data Network (EDDN). This feature is disabled by default and is
independent of Spansh, Inara, Raven Colonial, Canonn, and every local journal
consumer.

## Consent and identity

Enable **Share supported live data with EDDN** under Settings > Privacy only if
you want to contribute. EDDN does not require an account, application token, or
personal API key. The standard EDDN header contains the commander name as its
`uploaderID`; EDDN obfuscates that value before distributing messages to
listeners.

Disabling sharing immediately stops new queue entries and removes the local
pending-upload file. Startup journal history, multicrew activity, and companion
files that cannot be attributed while multiple Elite instances are running are
not published.

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

Every accepted payload is persisted to `eddn-outbox-v1.json` before its first
network attempt. A single background sender uses gzip and exact HTTP/1.1,
spaces successful messages by at least 400 milliseconds, and preserves order.
Transient failures wait at least one minute and back off to thirty minutes;
invalid and permanently rejected payloads are removed without blocking later
messages.

The journal reducer, overlays, local persistence, Raven Colonial, Canonn,
Spansh, and Inara integrations do not wait for an EDDN network request. Queue
logging occurs after synchronization locks are released, and shutdown cancels
workers without disposing synchronization primitives underneath them.

The live gateway uses production schemas. Beta and dev gateways automatically
append `/test` to schema references so validation traffic cannot enter the
production stream.
