# Network publication privacy

SrvSurvey Avalonia keeps every optional publication surface off until the user
explicitly enables it in Settings. Local journal parsing, overlays, exploration,
exobiology, colonization, quest, and route tracking do not depend on Inara.

## Inara

Inara upload is disabled by default. Enabling it sends only supported, mapped
live-game events after startup. It does not replay historical journal activity,
and it does not upload Legacy, alpha, beta, or multicrew activity.

Write events use the commander's personal Inara API key. The key is stored in
that commander's local profile and is placed in Inara's `APIkey` request header.
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

When more than one Elite Dangerous game window is detected, SrvSurvey cannot
safely attribute shared `Cargo.json`, `ShipLocker.json`, or `Status.json` data to
a commander. Those shared inputs are suppressed for Inara while journal events
that belong to the instance's selected commander continue to be processed.

## Other publishers

EDDN, Raven Colonial, Canonn settlement geometry, and Green Gas Giant
publication retain their existing independent opt-in controls and payload
rules. Enabling Inara does not enable or modify any of them.
