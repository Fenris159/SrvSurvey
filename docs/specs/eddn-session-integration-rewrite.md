# EDDN session integration rewrite

## Goal

Keep Avalonia's EDDN behavior aligned with the accepted legacy design without
allowing uploader identity or mutable journal state to cross Commander
sessions. The integration must remain safe across Commander changes, multiple
SrvSurvey processes, multiple Elite clients, network failures, and application
restarts.

## Lifetime model

- `MainWindowViewModel` owns one application-lifetime `EddnPublisher`. It owns
  consent, runtime suspension, transport, the exclusive outbox lease, ordered
  persistence, and durable retry delivery.
- `EddnPublisher` creates one `EddnSessionPublisher` for the current journal
  session. The session captures an immutable Commander/version header and owns
  location, crew, signal-batch, companion-read, and deduplication state.
- The session identity includes the journal series, Commander name, and
  Frontier ID. A change disposes the old session before processing the new
  session. A mismatched `LoadGame` stops the captured session safely.
- Disposing a session cancels its companion reads. Already queued messages
  remain owned by the application publisher and retain their captured header.
- No queued message consults the current Commander or journal session during
  delivery.

## Consent and user interface

- EDDN sharing defaults off and is configured from a dedicated **Configure
  EDDN Sharing** button in Settings immediately above the Inara section.
- The modal window provides the single opt-in checkbox and explains the
  journal and companion data sent, Commander uploader identifier and EDDN
  obfuscation, lack of an EDDN account or API key, local durable retry queue,
  deletion on opt-out, global scope, multi-client pause, and duplicate-upload
  risk.
- The warning says: **Enable EDDN uploads in only one application at a
  time—for example, SrvSurvey or EDMC—to avoid duplicate submissions.**
- A cancelled window does not change consent. Disabling sharing cancels active
  delivery and deletes pending uploads.

## EDDN policy

- Uploads use EDDN's live upload endpoint.
- Every upload uses a production schema reference. This is an internal release
  policy, not a persisted setting or user choice.
- Existing persisted environment/test-mode settings are removed when network
  privacy settings are next saved. Existing durable messages are normalized to
  remove any legacy `/test` suffix before delivery.
- Companion files are revalidated against the triggering journal event and
  session generation immediately before enqueueing.

## Delivery and batching

- Each immutable payload, including its captured session header, is durable
  before enqueue succeeds.
- Persistence uses one atomic record per message rather than rewriting the
  complete backlog for each journal event. The previous array store is migrated
  on first ownership.
- New messages receive their first attempt in creation order. A retryable
  failure uses per-message bounded exponential backoff and cannot block other
  due messages.
- Invalid durable entries are quarantined individually without preventing valid
  entries from loading or sending.
- `FSSSignalDiscovered` batches capture system, expansion flags, header, and
  safety generation with the first signal. A later jump flushes the batch with
  that captured origin context.
- Journal processing does not hold a session lock across disk or network I/O.

## Verification

- Regressions cover Commander A-to-B isolation, mismatched `LoadGame`, stale
  companion-read cancellation, captured signal-batch location, retry fairness,
  fixed production schemas, legacy queue migration, corruption isolation, consent
  deletion, exclusive ownership, and the dedicated disclosure window.
- Core and Desktop tests, formatting, Windows/Linux builds, and the reopened PR
  checks must pass before the branch is handed off for review.
