# Journal upload reliability

EDDN, Inara, EDSM, EDAstro, and VoxStellar retain captured live reports locally
before sending them. Failed or interrupted deliveries survive application
restarts. Bootstrap reads rebuild game context without uploading journal history;
they can resume reports already captured during an opted-in live session.

The shared queue records immutable prepared payloads, source record identities,
acknowledgements, and cooldowns. A leased owner prevents two application instances
from delivering the same retained queue simultaneously. Accounts remain separate;
recovered reports keep their original timestamps and context. API keys and
signing keys are supplied at send time and are never written into retry files.
The queues remain machine-local and are excluded from Google Drive profile sync.
Pinned Commander instances use separate settings-profile storage, so a profile's
public-sharing opt-out cannot invalidate another profile's queue.

EDDN retains its existing schema-specific queue, gzip transport, shared consent
controls, and retry rules. It now flushes prepared writes before completing a
journal update, including trailing signal batches, and retains receipts for
source-derived record identities. The other four journal senders use the shared
append-log store with flushed writes, bounded storage, exclusive owner leases,
and recovery of valid records beside damaged entries.

## Provider outcomes

| Provider | Recovery behavior |
| --- | --- |
| EDDN | Retry transient transport/service failures using its persisted due time; retire accepted or permanently rejected schema messages. |
| Inara | Preserve mapped events and retry only deferred events after a complete per-event response; preserve request limits and `Retry-After`. Clearing the profile key clears retained work. |
| EDSM | Refresh the discard list before sending; retain captured transient context and game version/build; retry deferred events independently. Credential rejection pauses publication until credentials change. |
| EDAstro | Refresh the advertised event policy, enforce the reviewed disclosure list, check the JSON result, and retain rejected or interrupted reports with the existing provider cooldowns. |
| VoxStellar | Retain each captured commander/webhook body, generate signatures at send time, and retry HTTP 408/429/5xx or transport failures with bounded backoff and `Retry-After`. |
| Raven Colonial | Keep the existing durable delivery-recovery workflow. Reconcile an uncertain additive contribution before retrying; an acknowledged credit can retry its absolute requirement update without issuing another credit. |
| Canonn settlement geometry | Remains a separate geometry-submission workflow. Its payload and validation contracts are enforced alongside the journal suite; this change does not turn geometry submissions into automatic journal uploads. |

Storage failure prevents an unpersisted report from reaching the network. A
confirmed acknowledgement that cannot immediately be checkpointed is kept as a
completed receipt in memory; subsequent writes wait for storage recovery.
Malformed retained entries are diagnosed without discarding independent valid
entries. Explicit opt-out removes retained work, while ordinary shutdown keeps
it available for recovery. Bounded queues report overflow rather than silently
discarding previously captured durable reports.

No client can guarantee exactly-once delivery when a provider accepts a request
and the process dies before saving its acknowledgement, unless the provider
offers an idempotency or reconciliation mechanism. Recovered retries preserve
the captured payload and timestamp. Raven's additive writes specifically avoid
blind retry in this uncertain window.

## Mandatory scenarios

Run `pwsh ./tools/Test-JournalUploadScenarios.ps1`. The suite uses real journal
files and publishers with captured HTTP requests; it never submits synthetic
events to live providers. Each of the five journal senders must pass:

- Failed live delivery recovered after reopening with its original payload.
- A separate writer process killed without disposal, followed by recovery.
- An acknowledged source record reread after restart without another upload.

The gate also requires provider-specific policy, partial acknowledgement,
credential isolation, consent revocation, unused acknowledgement-body failures,
capture-storage failures, temporary credential loss versus explicit removal,
receipt compaction and eviction order, damaged record recovery, and Raven
uncertain-credit scenarios. It validates executed TRX
results, so deleting, skipping, or failing a mandatory case fails validation.
Both Windows and Linux PR/release builds enforce this gate. The local changed-code
quality check enforces the same matrix alongside Sonar rules and the 80%
new-code coverage requirement.
