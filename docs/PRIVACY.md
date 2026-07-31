# Network publication privacy

SrvSurvey Avalonia keeps every optional publication surface off until the user
explicitly enables it in Settings. Local journal parsing, overlays, exploration,
exobiology, colonization, quest, and route tracking do not depend on Inara.
The Community Goals dashboard can make a separate read-only Inara request for
public global-goal details as described below.

## Inara

Inara upload is disabled by default. Enabling it sends only supported, mapped
live-game events after startup. It does not replay historical journal activity,
and it does not upload Legacy, alpha, beta, or multicrew activity.

Write events use the commander's personal Inara API key. The key is stored in
that commander's local profile and is placed in Inara's `APIkey` message header.
SrvSurvey identifies itself with `appName: SrvSurvey`; it does not embed or send
an application access token. A queued event is discarded if the active
commander's key changes before transmission.

Events are buffered for about 35 seconds. Commander credit changes are derived
from journal balance snapshots and transaction deltas, then coalesced to an
hourly cadence or a session boundary instead of uploading every transaction.
Transient HTTP failures retain the batch for a later retry.

Developer test mode is a separate setting and is disabled by default. During
initial live verification it sets `isBeingDeveloped: true`. Once Artie confirms
the application is approved for production on Inara, users should leave this
setting off so requests use `isBeingDeveloped: false`.

The Community Goals dashboard uses Inara's generic application API key to read
the public `getCommunityGoalsRecent` feed. This request is independent of the
commander upload setting and does not include a commander name, Frontier ID, or
personal Inara API key. It supplements missing global fields such as the current
tier, contributor count, total contribution, description, reward, and Inara
page URL. Personal contribution and standing remain local Frontier/journal
data. Responses are cached in the local SrvSurvey data directory for at least
15 minutes; a stale cached response is retained when Inara is unavailable.

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

EDDN, Raven Colonial, Canonn settlement geometry, and Green Gas Giant
publication retain their existing independent opt-in controls and payload
rules. Enabling Inara does not enable or modify any of them.
