# Network publication privacy

SrvSurvey Avalonia keeps every optional publication surface off until the user
explicitly enables it in Settings. Local journal parsing, overlays, exploration,
exobiology, colonization, quest, and route tracking do not depend on Inara or
direct EDSM synchronization.
The Community Goals dashboard can make a separate read-only Inara request for
public global-goal details as described below.

Diagnostic replay support keeps a rolling 24-hour local history of validated
changes from `Status.json`, `Cargo.json`, `ShipLocker.json`, `NavRoute.json`, and
`Market.json`. It is stored under SrvSurvey's application-data directory and is
never uploaded automatically. The data leaves that directory only when the user
explicitly exports a replay package. Redacted exports remove companion location
names, identifiers, and coordinates; cargo and material names and quantities are
retained so inventory-dependent behavior can be reproduced. Raw exports preserve
the validated companion snapshots.

## EDDN

EDDN upload is disabled by default. **Configure EDDN Sharing** opens a dedicated
disclosure and opt-in window immediately above the Inara settings. When enabled,
SrvSurvey sends supported new live journal events plus validated Market,
Outfitting, Shipyard, Fleet Carrier Materials, and NavRoute companion-file data.
Startup history, multicrew activity, localized fields, and Commander-specific
fields prohibited by the EDDN schemas are not uploaded.

EDDN needs no account, API key, OAuth token, or Authorization header. Each
journal session uses that session's Commander name as EDDN's required uploader
identifier, which EDDN obfuscates before redistribution. Journal series,
Commander name, and Frontier ID changes replace the session. Queued messages
retain their captured Commander header, and unfinished companion reads are
cancelled, so mutable state cannot cross between Commanders.

Accepted messages are stored as individual records in a bounded durable local
retry queue under SrvSurvey's application-data directory. They are removed
after delivery, permanent rejection, or opt-out. Only one SrvSurvey process can
own the queue. Queueing and delivery pause while multiple Elite windows make
shared state ambiguous; already-pending records remain local until attribution
is safe.

This release sends to EDDN's Live gateway using production schema references
fixed internally. There is no user-selectable production/test toggle. Enable
EDDN uploads in only one application at a time, such as SrvSurvey or EDMC,
because multiple uploaders can create duplicate submissions.

## Inara

Inara upload is disabled by default. Saving a personal API key opts in only the
displayed commander and sends only supported, mapped live-game events after
startup. Removing that commander's key disables their uploads. It does not
replay historical journal activity, and it does not upload Legacy, alpha, beta,
or multicrew activity.

Write events use the commander's personal Inara API key. The key is stored in
that commander's local profile and is placed in Inara's `APIkey` message header.
SrvSurvey identifies itself with `appName: SrvSurvey`; it does not embed or send
an application access token. Commander name, Frontier ID, journal path, and
live/beta eligibility are bound for the journal session. A queued event is
discarded if that commander's key changes before transmission, so credentials
and events cannot cross between commander sessions.

Events are buffered for about 35 seconds. Commander credit changes are derived
from journal balance snapshots and transaction deltas, then coalesced to an
hourly cadence or a session boundary instead of uploading every transaction.
Transient HTTP failures retain the batch for a later retry.

SrvSurvey currently sends `isBeingDeveloped: true` on commander write requests
while the integration is being validated. This is an application-owned protocol
setting, not a user preference.

Enable Inara uploads in only one application at a time to avoid duplicate
commander events.

The Community Goals dashboard uses Inara's generic application API key to read
the public `getCommunityGoalsRecent` feed. This request is independent of the
commander upload setting and does not include a commander name, Frontier ID, or
personal Inara API key. It supplements missing global fields such as the current
tier, contributor count, total contribution, description, reward, and Inara
page URL. Personal contribution and standing remain local Frontier/journal
data. Inara does not provide a documented API event for reading a commander's
Community Goal contribution history, even when a personal API key is present.
SrvSurvey instead recovers matching contribution, percentile, and reward data
from that commander's local journal history without uploading it. Responses
from the public Inara feed are cached in the local SrvSurvey data directory for
at least 15 minutes; a stale cached response is retained when Inara is
unavailable.

Release builds receive the generic application key from the repository's
`INARA_APPLICATION_API_KEY` secret. The value is added to assembly metadata at
publish time rather than committed to source. It is an application identifier,
not a commander credential, and should not be treated as protection against a
determined inspection of the distributed binary. Local developer builds can use
the `SRVSURVEY_INARA_APPLICATION_API_KEY` environment variable instead.

When more than one Elite Dangerous game window is detected, SrvSurvey cannot
safely attribute shared `Cargo.json`, `ShipLocker.json`, or `Status.json` data to
a commander. Those shared inputs are suppressed for Inara while journal events
that belong to the instance's selected commander continue to be processed.

## EDSM

Direct EDSM synchronization is disabled by default. Saving an EDSM personal API
key opts in only the displayed local Commander profile. SrvSurvey uses the
current Elite journal Commander name as the EDSM account name, so the names must
match. Clearing the stored key disables synchronization,
cancels active delivery, and removes pending in-memory events for that profile.
This setting is separate from EDDN and does not enable EDDN sharing.

SrvSurvey sends supported, new Live journal events to EDSM's authenticated
Journal API. EDSM documents that accepted events can update account data such as
flight history and location, credits, ships and loadouts, cargo, materials,
backpack and locker inventory, missions, engineering, community goals,
statistics, social status, and discoveries. Before uploading, SrvSurvey fetches
EDSM's current discarded-event list and fails closed until a valid list is
available. EDSM's current policy excludes chat, screenshot paths, `Status.json`,
Market, Outfitting, Shipyard, NavRoute, and other unsupported event types.

Each request includes the current Commander name and EDSM personal API key, SrvSurvey's
name and version, the Elite game version and build, and a bounded ordered batch
of journal messages. When available, SrvSurvey adds only the transient context
fields sanctioned by EDSM: system address, system name, system coordinates,
market ID, station name, and ship ID. Credentials are stored only in the local
Commander profile. Raw messages are held only in a bounded memory queue and are
not written to SrvSurvey's normal logs or a durable retry file.

Events are batched for about 30 seconds. Requests have bounded payloads and
responses, one batch is in flight at a time, and transient failures use delayed
retry while honoring EDSM's rate-limit and retry headers. Credential rejection
pauses publication until the saved credentials change. Startup history, Legacy,
alpha/beta, diagnostic replay, multicrew, and sessions with multiple Elite
windows are not uploaded. Enable direct EDSM synchronization in only one
application at a time, such as SrvSurvey or EDMC, to avoid duplicate requests.

## VoxStellar

VoxStellar journal upload is disabled by default and is controlled separately
at the top of the Boxel workspace. When enabled, SrvSurvey sends the active
commander name and the complete JSON data from new live `Scan`, `FSDTarget`,
`FSDJump`, `FSSDiscoveryScan`, `SAASignalsFound`, `ScanOrganic`,
`ScanBaryCentre`, and `CodexEntry` events. Startup history, replayed journal
events, and unrelated events are not uploaded. New publication is also
suppressed while multiple Elite windows make commander attribution ambiguous.

Each request is sent directly to VoxStellar as a `commander` + `data` JSON
object and signed with HMAC-SHA256. The ordered queue is memory-only. Turning
the option off advances its consent generation so entries that have not begun
uploading cannot be sent under the former consent state. Logs record only event
names and transport outcomes; they do not include journal payloads, commander
names, request signatures, or the shared integration key.

VoxStellar says it stores system and coordinate data, stars, planets and moons,
exobiology discoveries, boxel metadata, timestamps, and commander attribution
in its own boxel-focused database and does not forward the submissions to EDDN.
Its current privacy policy and terms govern the external service. In particular,
its terms grant VoxStellar a worldwide, non-exclusive, royalty-free license to
use, modify, display, reproduce, and distribute submitted user content through
the service. The Boxel workspace information button links to those current
documents before the opt-in control.

The webhook protocol is adapted from the MIT-licensed EDMC-VoxStellar plugin.
Release packages receive its distributed application signing key from the
repository's `VOXSTELLAR_SHARED_KEY` secret at publish time; the value is not
committed or logged. It is a shared application credential embedded in the
published binary and must not be treated as a private commander credential.
Local developer builds can use the `SRVSURVEY_VOXSTELLAR_SHARED_KEY`
environment variable instead.

## Frontier commander profile

Linking a Frontier account is optional. SrvSurvey uses OAuth 2 authorization
code flow with PKCE, requests only the `auth capi` scope, and never receives or
stores the commander's Frontier password. The registered application has no
shared client secret.

After authorization, SrvSurvey reads the live `/profile`, `/fleetcarrier`,
`/market`, `/shipyard`, and `/communitygoals` endpoints to populate the
commander dashboard. It does not request Frontier's visited-stars or journal
resources. This information is displayed locally and is not uploaded to
SrvSurvey, another SrvSurvey user, or a third party. Authentication tokens are
protected with Windows Data Protection on Windows or a Secret Service-compatible
keyring on Linux; there is no plaintext fallback. A normalized dashboard cache,
including detailed ship, carrier, market, shipyard, and Community Goal fields,
is kept separately for each journal Frontier ID in the commander's local
SrvSurvey data directory. Tokens are selected only for the active journal
commander unless the user explicitly chooses another locally linked account in
the console's manual selector. A newly authorized `/profile` identity must
match the journal commander for which linking began before the account is
attached. Raw API responses
are not persisted.

The **Current Ship** tab may also display the active ship's cargo and Odyssey
materials from Elite's local `Cargo.json` and `ShipLocker.json` files. This
local inventory is not sent to Frontier or any other service, and it is hidden
when multiple Elite game processes make commander attribution ambiguous or
when the console is manually displaying a different Frontier commander.

A manual refresh attempt is limited to one per minute, including after a failed
request. Automatic refresh is attempted only when there is no cached profile or
the cache is at least 15
minutes old, and only once per application session. **Unlink Frontier** removes
only the console's currently selected commander's stored authorization and
local profile cache. See
[Frontier account linking](FRONTIER.md) for implementation and troubleshooting
details.

## Other publishers

VoxStellar, Raven Colonial, Canonn settlement geometry, and Green Gas Giant
publication retain their existing independent opt-in controls and payload
rules. Enabling EDDN, Inara, or EDSM does not enable or modify any of them.
