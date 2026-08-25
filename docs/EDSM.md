# EDSM Journal API upload research

Research snapshot: 2026-08-25

This note records the upload contract needed for an optional SrvSurvey
integration with the Elite Dangerous Star Map (EDSM). It uses EDSM's official
Journal API documentation and the current EDMarketConnector (EDMC) EDSM core
plugin as primary sources. The EDSM page says it was last revised on
2022-11-30, so the live discard endpoint and EDMC source were also checked to
confirm current operational behavior.

## EDSM API contract

EDSM exposes two relevant endpoints:

- `POST https://www.edsm.net/api-journal-v1` accepts journal submissions.
- `GET https://www.edsm.net/api-journal-v1/discard` returns event names EDSM
  does not currently process.

The upload endpoint accepts a JSON body or form/multipart parameters. EDSM
explicitly declines upload requests made with `GET`, so credentials and journal
content must never be placed in a URL or query string. EDMC uses an HTTPS form
POST and places the serialized journal batch in the `message` field, which is a
proven compatible encoding. See the [official Journal API parameter
contract](https://www.edsm.net/es/api-journal-v1) and EDMC's [request
construction](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L869-L909).

The required upload fields are:

| Field | Required content |
|---|---|
| `commanderName` | The Commander name registered with EDSM. SrvSurvey uses the current journal Commander name, so that name must match the EDSM registration. |
| `apiKey` | The personal API key associated with that EDSM Commander. EDSM directs users to `https://www.edsm.net/settings/api` to create or retrieve it. |
| `fromSoftware` | The submitting application's name. |
| `fromSoftwareVersion` | The submitting application's version. |
| `fromGameVersion` | The source Elite game version. |
| `fromGameBuild` | The source Elite game build. |
| `message` | One unmodified journal message or an ordered list of journal messages. Each message needs `timestamp` and `event`. |

EDSM requires the game version and build specifically to distinguish Live from
Legacy and accepts only the Live galaxy. Legacy/console data is unsupported.
This is stated by the [official API documentation](https://www.edsm.net/es/api-journal-v1)
and enforced by EDMC before it queues an event ([live, beta, credential, and
multicrew gates](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L557-L606)).

### Transient context

Journal events often omit the location, station, or ship context needed to
interpret them. EDSM explicitly permits these underscore-prefixed transient
properties on each event:

- `_systemAddress`
- `_systemName`
- `_systemCoordinates`
- `_marketId`
- `_stationName`
- `_shipId`

EDSM's example state machine resets context on `LoadGame`, `Undocked`, and crew
transitions; updates system/station context from `Location`, `FSDJump`, and
`Docked`; and updates ship context from `SetUserShipName`, `ShipyardBuy`,
`ShipyardSwap`, and `Loadout`. It also suppresses a non-captain crew member's
events. See [EDSM's transient-state definition and example](https://www.edsm.net/es/api-journal-v1).

Current EDMC adds `_systemName`, `_systemCoordinates`, `_stationName`, and
`_shipId` to queued events. On `LoadGame` it synthesizes a `Materials` snapshot
from its current reducer state, and for `BackPack` it substitutes the populated
backpack snapshot for the otherwise empty journal notification. See [EDMC's
event preparation](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L557-L627).

SrvSurvey should derive all six sanctioned transient fields from its isolated
journal-session reducer when known. Shared companion-file state must only be
added when one active Elite process and the existing timestamp/identity checks
make attribution safe. No other event mutation is part of the EDSM contract.

## Accepted and discarded events

The discard endpoint is the canonical, dynamic filter. EDSM says clients
should refresh it when the software starts and should not submit events named
there. On 2026-08-25 the live endpoint returned 141 discarded event names. The
list included `Fileheader`, `Commander`, `Status`, `Market`, `Outfitting`,
`Shipyard`, `NavRoute`, `ReceiveText`, `SendText`, and `Screenshot`, among many
others. Therefore current EDSM uploads do not include chat messages, screenshot
paths, `Status.json` events, or those companion-file snapshots. See the [live
discard endpoint](https://www.edsm.net/api-journal-v1/discard) and its [official
usage guidance](https://www.edsm.net/es/api-journal-v1).

The accepted-event table is broader than public exploration data. EDSM uses
journal submissions to maintain personal account data including flight logs and
location, credits, ships and loadouts, cargo, materials, backpack and ship
locker inventories, missions, engineering, community goals, statistics,
friends status, crime/death/interdiction history, and exploration discoveries.
The UI disclosure must describe this as personal EDSM account synchronization,
not as another EDDN-style public galaxy submission. See EDSM's [accepted event
catalog and described effects](https://www.edsm.net/es/api-journal-v1).

EDMC waits for a non-empty discard list before consuming its upload queue and
retries that lookup every ten seconds while startup continues. It removes
`Docked` from the downloaded discard set if present because its own state/flush
logic needs that event. See [EDMC discard-list startup](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L673-L708).

For SrvSurvey, failing closed until a valid discard list is available is safer
than guessing that an unknown event is acceptable. The lookup should happen in
the background and use bounded exponential backoff so it never blocks the
journal reducer or repeatedly hammers EDSM.

## Result and retry semantics

EDSM documents these result families:

- `100`-`104` are terminal successful or intentional no-op results: accepted,
  already stored, older than stored data, duplicate within EDSM's roughly
  300-second cache, or suppressed because the Commander is a non-captain crew
  member.
- `201`-`208` are request-level configuration/authentication failures: missing
  or invalid Commander/API key, missing or blacklisted software identity,
  invalid JSON, missing game/build information, or an unsupported old game.
- `301`-`304` are event/payload failures: missing message, invalid message JSON,
  missing `timestamp`/`event`, or a discarded event.
- `401`, `402`, and `451`-`453` are domain-specific processing outcomes. `402`
  explicitly says an unknown item may be retried later; the others should not
  be blindly retried.
- `500` and `501` represent unexpected processing failures.

See the [official EDSM result-code descriptions](https://www.edsm.net/es/api-journal-v1).

Current EDMC expects a JSON response with top-level `msgnum`, `msg`, and an
`events` array aligned with the submitted batch. It treats a top-level 1xx as
successful, a top-level 2xx as fatal, and a top-level 5xx as accepted by EDSM
for later server-side processing. It logs every event result outside the 1xx
family. See [EDMC response handling](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L711-L775).

EDSM does not publish a numeric request-per-minute or batch-size limit in the
Journal API documentation. Current EDMC likewise has no fixed periodic upload
cadence or explicit maximum event count/byte size. Its worker sends when its
event-sensitive `should_send` predicate allows it, including deliberate
coalescing for station transactions and Nav Beacon scan bursts. See [EDMC's
queue worker](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L778-L929)
and [flush predicate](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L932-L975).

EDMC uses a 20-second HTTP timeout. When EDSM supplies
`X-Rate-Limit-Remaining: 0` and an `X-Rate-Limit-Reset` Unix timestamp, EDMC
waits until that reset time. Transport/parsing failures are attempted up to
three times, but those attempts have no delay between them. See [EDMC's timeout
and retry constants](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L59-L63),
[rate-limit handling](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L711-L741),
and [three-attempt loop](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py#L811-L925).

The immediate retry loop is useful interoperability evidence, but it should
not be copied into SrvSurvey. A safer policy is:

1. Allow only one in-flight EDSM request.
2. Bound batches by both event count and encoded byte size. Values such as 128
   events and 1 MiB are defensible internal safety limits, not EDSM-published
   limits, and should be covered by tests.
3. Preserve source order and never mix Commander, journal series, game
   version, or game build in one batch.
4. Retry transport failures, HTTP 408/429/5xx responses without a valid EDSM
   receipt, and narrowly retryable per-event results with exponential backoff
   and jitter. Honor `Retry-After`, `X-Rate-Limit-Remaining`, and
   `X-Rate-Limit-Reset` when present.
5. Treat a valid top-level 5xx EDSM receipt as server-accepted-for-later, as
   EDMC does, rather than resending the whole batch.
6. Pause on 2xx credential/software/game failures until credentials, software
   version, or session context changes. Surface the reason in Settings instead
   of repeatedly retrying a fatal request.
7. Treat 1xx outcomes and terminal 3xx/4xx event outcomes as complete. If `402`
   is retried, isolate only that failed event so accepted siblings are not
   duplicated.
8. Use response-header-only completion, a bounded response body, a fixed HTTPS
   endpoint, and no automatic cross-origin redirect of credentials.

## Safe SrvSurvey module boundary

The existing [`InaraPublisher`](../src/SrvSurvey.Core/Inara/InaraPublisher.cs)
and EDDN session publisher provide the appropriate ownership pattern, but an
EDSM publisher needs its own queue, state reducer, payload builder, HTTP client,
and result model. It must not make Inara or EDDN wait on EDSM and must not mutate
the journal object those integrations consume.

Recommended invariants:

- Upload is off unless the active Commander profile has a personal EDSM API key
  and a current journal Commander name.
- Session identity includes the journal series/path, journal Commander and FID,
  EDSM Commander name, credential generation, game version, and game build.
- Only newly observed live events from an active non-replay session are
  eligible. Do not upload startup history merely because credentials were
  saved.
- Suppress Legacy, alpha/beta, and multicrew activity. When multiple Elite
  processes exist, suppress any synthetic or shared-file snapshots that cannot
  be attributed to one Commander.
- Fetch and validate the discard list before the first event can leave the
  application. Filtering remains independent of the UI thread and journal
  reducer.
- Keep the private raw-event backlog bounded and memory-only. Do not persist the
  API key with queued events. If a durable outbox is ever added, it needs a
  separate privacy and credential-at-rest design review.
- Clearing credentials, switching Commander/journal series, disabling upload,
  entering diagnostic replay, or disposing the application cancels the active
  request and invalidates queued events from the old authorization generation.
- Flush safely on shutdown and session change with a bounded wait; never hold an
  application lock while awaiting EDSM.
- Redact the API key, raw request body, and journal content from normal logs.
  Diagnostic messages may include event names, counts, byte sizes, EDSM result
  codes, and retry deadlines.
- Warn users to enable EDSM upload in only one application. EDSM deduplicates an
  event for about 300 seconds, but running EDMC and SrvSurvey uploaders together
  still wastes requests and can produce confusing status.

## Settings panel and disclosure requirements

The panel belongs immediately below the existing Inara card and should follow
the same visual language: provider title, `OPT-IN` badge, active Commander,
masked credential input, Save and Clear actions with confirmation, status text,
and a button opening `https://www.edsm.net/settings/api`.

The only editable value is the **Personal EDSM API key**, masked by default and
stored in the active Commander profile. SrvSurvey supplies the current journal
Commander name as EDSM's authenticated `commanderName`; the panel must disclose
that it needs to match the Commander registered with EDSM.

A tailored disclosure should make these points visible before opt-in:

- Saving the API key enables direct synchronization with that Commander's
  EDSM account; it is separate from EDDN sharing and does not enable EDDN.
- Supported Live journal events can update EDSM flight history, location,
  credits, ships/loadouts, cargo, materials, backpack/locker, missions,
  engineering, community goals, statistics, social status, and discoveries.
- EDSM's current discard policy excludes chat, screenshot paths, `Status.json`,
  and several companion-file events; SrvSurvey checks that policy at startup.
- Only future attributable live-session activity is uploaded. Legacy,
  alpha/beta, diagnostic replay, and multicrew activity are excluded.
- The API key is stored only in the selected local Commander profile,
  never written to logs or placed in queued payload records. Clearing it stops
  new uploads, cancels any active request, and removes pending in-memory events.
- Only one EDSM uploader should be enabled at a time.

## Implementation verification checklist

- Required-field and form-encoding contract tests.
- Single-event and ordered-batch response tests.
- All six transient fields and reset-transition tests.
- Live/Legacy, beta, replay, multicrew, multi-process, startup-history, and
  Commander/FID/session-switch gating tests.
- Dynamic discard-list success, invalid/empty response, cancellation, and
  backoff tests.
- Bounded event-count, payload, response, queue, timeout, and shutdown tests.
- 1xx/2xx/3xx/4xx/5xx, HTTP 408/429/5xx, rate-header, malformed-response, and
  partial-batch tests.
- Credential-generation tests proving that save, clear, Commander switch, and
  in-flight cancellation cannot relabel or leak a batch.
- Logging tests proving the API key and raw journal content never appear.
- Settings, profile persistence, confirmation, link, disclosure, status, and
  search-index tests.
- Network coverage and privacy documentation updates.

## Primary sources

- [EDSM Journal API v1 documentation](https://www.edsm.net/es/api-journal-v1)
- [EDSM live discarded-event endpoint](https://www.edsm.net/api-journal-v1/discard)
- [EDMarketConnector EDSM plugin at commit 2b6a0ce1](https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/edsm.py)
