# Frontier account linking

The commander card at the top of the navigation rail opens SrvSurvey's Frontier
profile page. Linking is optional. Until it is linked, the page explains the
privacy boundary and provides a **Connect to Frontier** button.

## Registered application

- Client ID: `66818020-d5ee-4c33-b909-b2632506a937`
- Redirect URI: `srvsurvey://frontier-auth`
- Scope: `auth capi`
- Shared client secret: none

SrvSurvey opens Frontier's authorization page in the default browser and uses
OAuth 2 authorization code flow with PKCE. A cryptographically random state
value protects the callback. The callback is accepted only when its scheme,
host, path, and state exactly match the pending request. Windows registers the
custom URI handler for the current user; Linux installs a per-user desktop MIME
association when the user starts the connection.

## Data shown

The linked dashboard reads Frontier's live `/profile`, `/fleetcarrier`,
`/market`, `/shipyard`, and `/communitygoals` resources. Its tabs present:

- **Commander:** credits, debt, estimated net worth, identity, current
  location, major-faction reputation, ranks, capabilities, and the owned fleet;
- **Current Ship:** the active ship's modules, launch bays, engineering,
  condition, value breakdown and livery; the fitted loadout is grouped by slot
  type with class/rating and engineering details, while Ship Locker materials
  are grouped into expandable categories. Current cargo comes from `Cargo.json`
  and Odyssey materials from `ShipLocker.json` when those local files can be
  attributed safely to the active commander;
- **Fleet Carrier:** identity, location, access, jump and finance details,
  capacity, installed services and crew, cargo, storage locker contents,
  market buy/sell orders, service tax, and itinerary;
- **Market:** the last docked market's services, economies, trade policy,
  prices, stock, demand and commodity state;
- **Shipyard:** ships for sale, outfitting modules, stock and station services;
- **Community Goals:** descriptions, objectives, rewards, expiry, overall
  progress and the commander's contribution and standing.

Ranks and major-faction identity use artwork from
[EDAssets](https://edassets.org/); see the repository's
[third-party notices](../THIRD-PARTY-NOTICES.md) for source and licensing.

Frontier currently includes major-faction reputation in the Fleet Carrier
response, but returns HTTP 204 with no response body when a commander owns no
carrier. SrvSurvey therefore stores commander reputation independently from the
optional carrier model and supplements it from Elite's local `Reputation`
journal event. Journal values are accepted only for the same commander and do
not replace a newer CAPI snapshot.

Expandable **All data** sections retain every scalar value returned by each
CAPI response under a labeled path. This provides a compact fallback for
fields Frontier adds or fields that do not yet have a dedicated card. SrvSurvey
stores this normalized snapshot, including detailed ship data, so the page can
open immediately without querying Frontier on every visit. Raw JSON responses
and authentication secrets are never displayed in these sections.

Current cargo and Ship Locker contents are not provided as inventories by the
CAPI `/profile` response. The **Current Ship** tab supplements CAPI data from
Elite's local `Cargo.json` and `ShipLocker.json` files. SrvSurvey hides these
shared-file inventories whenever more than one Elite game process is running,
because it cannot then attribute the files to one commander reliably.

SrvSurvey intentionally does not request `/visitedstars`. The application's
existing local journal pipeline remains the source for travel history; the
dashboard also does not request the remote `/journal` endpoint.

## Storage and request policy

Access and refresh tokens are protected by Windows Data Protection on Windows
and a Secret Service-compatible keyring on Linux. Linux linking requires
`secret-tool`; the application will not save tokens in plaintext when a secure
store is unavailable.

Authorizations and cached snapshots are isolated by the stable Frontier ID
from the active journal. Switching Elite accounts selects that commander's
saved authorization without reusing another commander's token or cache. A
commander that has not been linked remains disconnected until the user presses
**Connect to Frontier**. After authorization, SrvSurvey verifies the `/profile`
identity against the active journal before attaching the token. The original
single-account credential and cache files are migrated only after the cached or
live profile identity can be verified.

The console's commander selector defaults to **Automatic**, which follows the
active journal commander. It also lists every locally linked Frontier account
so the user can inspect another commander's cached dashboard without changing
which commander the rest of SrvSurvey is tracking. While the selection differs
from the active journal, journal-only reputation, `Cargo.json`, and
`ShipLocker.json` data are withheld rather than being shown against the wrong
Frontier profile. Returning the selector to **Automatic** resumes journal
following immediately.

SrvSurvey allows at most one CAPI refresh attempt per commander per minute,
including after a failed request and across concurrently running SrvSurvey
processes that track that commander. It
automatically refreshes only when no cached snapshot exists or the snapshot is at least 15
minutes old, and attempts that automatic refresh only once per application
session. All endpoint calls are serialized and responses are read with
explicit size limits. A successful carrier result, including `204 No Content`
for a commander without a carrier, is cached for 15 minutes before the carrier
endpoint is eligible again. Market, Shipyard, and Community Goals failures keep
their last good section data and display a warning instead of failing the
entire dashboard. Frontier `429` responses are surfaced with their
`Retry-After` delay instead of being retried aggressively.

Selecting **Unlink Frontier** removes only the commander currently selected in
the console and its cached profile from the device; other linked commanders
remain available. If a manually selected account is unlinked, the console
returns to **Automatic**.
Frontier can also revoke an authorization independently; SrvSurvey then returns
that commander to the disconnected state the next time it attempts to refresh.

## References

- [Frontier developer documentation](https://user.frontierstore.net/developer/docs)
- [EDCD notes for Frontier OAuth 2](https://github.com/EDCD/FDevIDs/blob/master/Frontier%20API/FrontierDevelopments-oAuth2-notes.md)
- [EDCD catalog of Frontier CAPI endpoints](https://github.com/EDCD/FDevIDs/blob/master/Frontier%20API/FrontierDevelopments-CAPI-endpoints.md)
- [SrvSurvey privacy statement](PRIVACY.md)
