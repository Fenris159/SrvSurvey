# EDAstro uploader parity audit

Checked: 2026-10-12, America/Chicago. Public GET response timestamps used below
were 18:49–18:52 UTC. Local revision: `9470e482e3dffb97b71430977f1e7b1e58b32a49`.

Scope: audit the existing direct EDAstro uploader against the provider's current
requested events and EDDiscovery's current source. Supplementary EDMC source
was checked to resolve CarrierStats filtering and response semantics. Only
bounded public GET requests and source reads were performed. No journal POST,
actual journal transmission, application code change, commit, push, or message
to an external party was performed.

The user supplied the provider's clarification: there is no application
whitelisting; follow EDDiscovery and the advertised `/api/accepting-js` list;
ignore `accepting-eddn-js` for this integration; `accepting-upload-js` is the
broader website-uploader list. That clarification replaces the earlier
application-approval uncertainty in [the preceding research](edastro-exploration-integration.md#eddiscoverys-separate-journal-upload-protocol).
It is supplied provider communication, distinct from the public GET observations.

## Finding

**The existing uploader covers all four events currently requested by the
direct API and follows EDDiscovery's envelope, journal JSON, event limit, and
pacing.** No missing direct-upload event, game-version field, or coordinate
enrichment was established. The trimmed CarrierStats schema exactly matches
the current EDMC integration. The main follow-up is to remove stale
application-approval wording; future requested event types and delivery
visibility are optional improvements. Evidence and intentional differences
are recorded below.

## Source snapshots

Current EDDiscovery `master` was checked through GitHub's repository API. Use
its pinned dependencies when comparing behavior, rather than independently
using every dependency's latest branch. These are source snapshots, not claims
about every installed client version.

| Source | Checked revision | Purpose |
| --- | --- | --- |
| [EDDiscovery](https://github.com/EDDiscovery/EDDiscovery/tree/d5dd5e88dffbcd65dc49af05c67d901649cf1dd8) | `d5dd5e88dffbcd65dc49af05c67d901649cf1dd8`, 2026-10-12 15:21:25 UTC | Application dispatch and pinned submodules |
| [EliteDangerousCore](https://github.com/EDDiscovery/EliteDangerousCore/tree/bc9263c078cc3051bb4f1414fe71afc57353c1d4) | `bc9263c078cc3051bb4f1414fe71afc57353c1d4` | EDDiscovery's pinned uploader and journal model |
| [BaseUtilities](https://github.com/EDDiscovery/BaseUtilities/tree/de29a8760d5bc6f3ea0324a7ae90421ec5ba51ff) | `de29a8760d5bc6f3ea0324a7ae90421ec5ba51ff` | EDDiscovery's pinned HTTP transport |
| [EDMarketConnector](https://github.com/EDCD/EDMarketConnector/tree/89f410c9c75c16ff564068ab5201ed3d65ba68ce) | `89f410c9c75c16ff564068ab5201ed3d65ba68ce`, 2026-09-02 | Supplementary direct-client filtering and response meanings |
| [Provider backup](https://github.com/Orvidius/edastro/tree/2494c845ac4bba804e1cc1db0e2ba5389428d0f8) | `2494c845ac4bba804e1cc1db0e2ba5389428d0f8`, 2026-02-04 | Limited downstream-field evidence; not the deployed journal handler |

The provider backup explicitly omits webserver services and includes obsolete
or unfinished code. It cannot establish the live HTTP receiver's full contract.
[Provider README](https://github.com/Orvidius/edastro/blob/2494c845ac4bba804e1cc1db0e2ba5389428d0f8/README.md#L5-L17).

## Current live event policy

| Public GET | Observation | Meaning for this integration |
| --- | --- | --- |
| [`/api/accepting-js`](https://edastro.com/api/accepting-js) | HTTP 200, `var accepting = ["CarrierJumpRequest","CarrierStats","Docked","ScanOrganic"];` | Advertised direct-submission list |
| [`/api/accepting`](https://edastro.com/api/accepting) | HTTP 200, JSON array with exactly the same four strings | Machine-readable variant used by EDDiscovery and SrvSurvey |
| [`/api/accepting-upload-js`](https://edastro.com/api/accepting-upload-js) | HTTP 200; 17 strings, 14 distinct event names | Broader website-import list; not the direct-client subscription |
| [`/api/journal`](https://edastro.com/api/journal), GET with no body | HTTP 200, `{"status":"400","message":"No Data"}` | Confirms an application result can differ from HTTP status and be encoded as a string; this was not an upload |

The broader website list contains `CarrierJump`, `CarrierJumpRequest`,
`CarrierStats`, `Docked`, `FSDJump`, `FSSAllBodiesFound`, `FSSDiscoveryScan`,
`FSSSignalDiscovered`, `Jump`, `Location`, `SAASignalsFound`, `Scan`,
`ScanBaryCentre`, and `ScanOrganic`. CarrierStats, Docked, and ScanOrganic appear
twice in that response. The provider's own [browser uploader](https://edastro.com/journals/upload.html)
and [uploader script](https://edastro.com/journals/journal-script.js) use that
broader list for selected historical journal files. Its presence is not evidence
that a background client should send those additional events directly.

`CodexEntry` is absent from both direct-list responses on this date. It remains
in SrvSurvey's disclosed supported set but is filtered out by the fetched list.
That is expected behavior. The provider explains that traditional EDDN/EDSM
feeds provide most system/body discoveries and its direct API fills specific
carrier, Codex, and organic-data gaps; general purpose descriptions are not a
promise that every event is requested at every moment.
[Provider submission overview](https://edastro.com/api-info.html),
[local supported set](../../src/SrvSurvey.Core/EdAstro/EdAstroJournalData.cs),
[local intersection](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs).

The advertised JS endpoint returns executable assignment syntax, while the
plain endpoint returns JSON. SrvSurvey does not need to fetch or execute JS:
using the equivalent JSON list follows EDDiscovery's method. These are dated
observations; the provider can change the list.
[EDDiscovery list request](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroClass.cs#L47-L54).

## Wire format, identity, and context

EDDiscovery sends a JSON array to `https://edastro.com/api/journal`. Object zero
contains `appName`, `appVersion`, and `odyssey`; subsequent objects are cloned
journal JSON. Its default name is `EDDiscovery`, and its version is the entry
assembly's numeric version. No API key, commander name, login, or token is added
to the envelope. SrvSurvey sends the same structure with its own
`SrvSurvey-XP` identity and numeric assembly version, currently `2.1.3.0`.
[EDDiscovery envelope and POST](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroClass.cs#L24-L66),
[local envelope](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs),
[local version construction](../../src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs).

EDDiscovery's inherited transport defaults to JSON UTF-8, a 20-second timeout,
and an entry-assembly name/version User-Agent. SrvSurvey also sends JSON UTF-8
and a `SrvSurvey-XP/version` User-Agent. A different User-Agent spelling is not
an established compatibility gap.
[Transport defaults](https://github.com/EDDiscovery/BaseUtilities/blob/de29a8760d5bc6f3ea0324a7ae90421ec5ba51ff/BaseUtilities/HTTP/HttpCom.cs#L27-L69),
[local request construction](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs).

**EDDiscovery does not add `gameversion`, `gamebuild`, `StarSystem`, `StarPos`,
body names, or surface coordinates in its EDAstro uploader.** It clones cached
or stored journal JSON and removes `EDD*` properties. In particular,
`GetJsonCloned()` is a clone operation, not an enrichment operation. The
session's expansion mode supplies the envelope's `odyssey` value.
[Uploader transformation](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroSync.cs#L94-L109),
[clone implementation](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/JournalStatus/Entry/JournalEntryDB.cs#L203-L220).

EDDiscovery adds `gameversion` and `gamebuild` to its **separate EDDN header**.
That does not create an EDAstro requirement. SrvSurvey's raw-json approach and
session-captured `odyssey` therefore have no demonstrated context-enrichment
gap against the requested reference.
[EDDN header](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDDN/EDDNClass.cs#L59-L66),
[local projection](../../src/SrvSurvey.Desktop/ViewModels/DesktopJournalProjectionAdapter.cs),
[local event preparation](../../src/SrvSurvey.Core/EdAstro/EdAstroJournalData.cs).

## Event payload parity

| Current direct event | SrvSurvey behavior | Assessment |
| --- | --- | --- |
| CarrierJumpRequest | Preserves raw public fields including CarrierID, SystemName, SystemAddress, Body, BodyID, and DepartureTime when present | No established missing destination field |
| CarrierStats | Sends timestamp, event, CarrierID, Callsign, Name, DockingAccess, AllowNotorious, FuelLevel, PendingDecommission, and Crew when present | Deliberate trimmed schema; exactly matches current EDMC's CarrierStats filter |
| Docked | Preserves station/system identity, station type, services, and remaining raw public journal fields | No demonstrated missing field or extra fleet-carrier-only filtering |
| ScanOrganic | Preserves ScanType, genus/species/variant and localized names, SystemAddress, Body, and remaining public journal fields | All presently requested organic evidence remains available |

Local evidence for all four rows is
[EdAstroJournalData](../../src/SrvSurvey.Core/EdAstro/EdAstroJournalData.cs).
EDDiscovery sends the untrimmed cloned journal objects with its `EDD*` additions
removed.
[EDDiscovery worker](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroSync.cs#L97-L105).
The exact CarrierStats public-field precedent is
[current EDMC filtering](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/plugins/edastro_core.py#L44-L79).

SrvSurvey additionally removes top-level Commander, FID, Credits, Loan, and
Balance, and excludes CarrierStats fields outside that public schema, including
Finance, SpaceUsage, jump ranges, ShipPacks, ModulePacks, and CarrierType. These
are conscious disclosure/privacy choices, not accidental serializer loss.
[Local field selection](../../src/SrvSurvey.Core/EdAstro/EdAstroJournalData.cs),
[sharing disclosure](../../src/SrvSurvey.Desktop/EdAstroIntegrationDialog.axaml).

The provider's public backup does read `Finance.TaxRate` and
`Finance.CarrierBalance` in a carrier processing path, but it also warns that
its code is incomplete and may be obsolete. This shows a historical difference
from untrimmed EDDiscovery; it does not show that live submissions require
finance or that SrvSurvey should expand its disclosure. No consumption of
SpaceUsage or jump ranges was found in the inspected CarrierStats processing
branches. Current EDMC omits those fields too.
[Provider carrier branch](https://github.com/Orvidius/edastro/blob/2494c845ac4bba804e1cc1db0e2ba5389428d0f8/EDDN.pm#L913-L929),
[alternate branch](https://github.com/Orvidius/edastro/blob/2494c845ac4bba804e1cc1db0e2ba5389428d0f8/EDDNnew.pm#L751-L768),
[backup limitations](https://github.com/Orvidius/edastro/blob/2494c845ac4bba804e1cc1db0e2ba5389428d0f8/README.md#L5-L17).

## Queue, policy, and delivery behavior

| Aspect | EDDiscovery reference | Existing SrvSurvey | Classification |
| --- | --- | --- | --- |
| Requested-event discovery | GET accepting once per process; filters enqueue and send | GET accepting; refreshes after one day; intersects with five disclosed types | Same current four events; conservative future-event policy |
| Empty/failed list | Empty set permits every event; null lookup can leave that state | Does not broaden into arbitrary events; failed discovery defers delivery | Intentional defensive difference |
| Batch size and pacing | Up to 200 journal objects; 10-second wait between groups | Up to 200 events and 1 MiB; at least 10 seconds between requests | Parity plus explicit byte bound |
| Expansion context | Batch Odyssey flag is OR of included entries | Batch captures one session and one Odyssey mode | Intentional stronger separation |
| Retention | Static memory queue; no explicit size cap in inspected worker | 4,096-event / 4 MiB memory queue; expires after one day; cleared on shutdown | Bounded local retention policy |
| HTTP outcome | Send helper returns HTTP 200 success; worker ignores that result | Checks HTTP plus application JSON status; retries transient failures; rejection cooldown | Stronger handling with corroborated result meanings |
| Rate handling | Inspected EDAstro worker only has its 10-second delay | Shared catalogue/upload attempt budget and provider header cooldowns | Intentional traffic control |
| History | Normal dispatch sends a new processed entry; debug menu can send one selected entry | Bootstrap/replay and Legacy sessions excluded; ambiguous windows pause delivery | Explicit live contribution policy |

Upstream sources for this comparison:
[list/filter/worker/queue](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroSync.cs#L34-L145),
[HTTP-only success helper](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroClass.cs#L57-L66),
[live application dispatch](https://github.com/EDDiscovery/EDDiscovery/blob/d5dd5e88dffbcd65dc49af05c67d901649cf1dd8/EDDiscovery/EDD/EDDiscoveryFormJournalEvents.cs#L141-L144),
[selected-entry debug action](https://github.com/EDDiscovery/EDDiscovery/blob/d5dd5e88dffbcd65dc49af05c67d901649cf1dd8/EDDiscovery/UserControls/History/HistoryGridRightClickMenu.cs#L478-L483).
Local sources:
[publisher](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs),
[shared traffic budget](../../src/SrvSurvey.Core/Exploration/EdAstroTrafficBudget.cs),
[projection policy](../../src/SrvSurvey.Desktop/ViewModels/DesktopJournalProjectionAdapter.cs).

EDDiscovery's switch is per commander and defaults to false. SrvSurvey uses a
separate installation-wide opt-in, also initially disabled. This consent scope
is disclosed; it is not wire incompatibility.
[EDDiscovery default](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/DB/User/EDCommander.cs#L76),
[local disclosure](../../src/SrvSurvey.Desktop/EdAstroIntegrationDialog.axaml).

The provider publicly states a 100-request / 15-minute limit for **data
retrieval**. SrvSurvey conservatively uses the same shared local budget for its
catalogue and uploads. That is client policy, not verified evidence of a
documented upload-specific quota.
[Retrieval rate documentation](https://edastro.com/api-details.html),
[local budget](../../src/SrvSurvey.Core/Exploration/EdAstroTrafficBudget.cs).

### Application response meanings

The current EDMC integration explicitly treats JSON status `200` as at least
one event accepted and `401` as none accepted with no errors. This is a
first-party client protocol precedent corroborating SrvSurvey's choice to
remove both outcomes from its queue; HTTP 401 itself is still a rejection.
[EDMC result handling](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/plugins/edastro_core.py#L181-L200),
[local result handling](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs).

This establishes the client interpretation, while the provider's no-data GET
only confirms HTTP-200/application-error separation and textual status values.
No successful real upload or provider acceptance of a particular SrvSurvey
batch was tested. The provider's browser script submits a form and does not
independently describe 200/401 response meanings.
[No-data endpoint](https://edastro.com/api/journal),
[browser upload script](https://edastro.com/journals/journal-script.js).

A batch-level status `200` does not prove every object was accepted. SrvSurvey's
activity message counts the batch as **submitted** events, which is appropriate;
keep that wording rather than presenting the number as confirmed individual
acceptances. More precise accepted/unwanted counts require a supported detailed
provider result, not an inference from the status code.
[EDMC status comment](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/plugins/edastro_core.py#L186-L190),
[local activity summary](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs).

## Follow-up opportunities

1. **Correct the application-approval language.** The provider's supplied
   clarification removes the need to speculate about sender whitelisting.
   Update [EDASTRO.md](../EDASTRO.md), the identity paragraph in
   [EdAstroIntegrationDialog.axaml](../../src/SrvSurvey.Desktop/EdAstroIntegrationDialog.axaml),
   the rejection diagnostic in
   [EdAstroPublisher](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs), and
   the superseded paragraph in
   [the earlier research](edastro-exploration-integration.md#eddiscoverys-separate-journal-upload-protocol)
   when implementation edits are requested. A service rejection can have many
   causes; the diagnostic should not imply an application-registration remedy.
   No such files were edited during this audit.
2. **Make future provider-list changes visible.** The fixed five-type
   intersection will ignore any newly requested type outside the disclosure
   until an intentional code/disclosure update. One-day list caching also means
   additions/removals are not immediate. A supported-list-change diagnostic or
   a manual refresh could help maintainers react without silently expanding
   what users agreed to send. Current four-event coverage is complete.
   [Local policy and lifetime](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs),
   [supported set](../../src/SrvSurvey.Core/EdAstro/EdAstroJournalData.cs).
3. **Improve delivery visibility if useful.** Expose pending count, last
   successful submission, last problem, and unwanted-result count in sharing
   settings. Existing logs are consolidated, and JSON 401 drains quietly with
   no accepted count. These would be troubleshooting improvements, not protocol
   repairs. Preserve the batch-level meaning described above.
   [Existing outcome and activity behavior](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs).
4. **Optionally distinguish prerelease versions.** Numeric assembly version
   matches EDDiscovery's methodology, but it does not distinguish SrvSurvey RC
   builds sharing that version. A full product version could improve provider
   diagnostics if represented compatibly in the User-Agent. This is an
   observability choice, not a missing required field.
   [Local numeric version](../../src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs),
   [upstream numeric version](https://github.com/EDDiscovery/EliteDangerousCore/blob/bc9263c078cc3051bb4f1414fe71afc57353c1d4/EliteDangerous/3rdPartyInterfaces/EDAstro/EDAstroClass.cs#L30-L42).

No expansion to the broader website-upload list is recommended for this
background uploader. SrvSurvey already has a separate EDDN contribution path
for Codex and other system/body discovery events; the provider explicitly asks
users to enable both channels. Additional CarrierStats fields, richer context,
or historical backfill should be introduced only for a concrete provider need
and an explicit contribution design. None was established as a current gap.
[Provider submission overview](https://edastro.com/api-info.html),
[local EDDN implementation](../../src/SrvSurvey.Core/Network/EddnPublisher.cs),
[provider historical upload](https://edastro.com/journals/upload.html).

## Verification and limits

The parent audit ran 44 focused tests successfully using controlled HTTP and
projection fixtures: 38 Core publisher/traffic-budget tests and six Desktop
sharing/privacy/projection tests. The projection test covers bootstrap versus
new live events and consent for EDAstro alongside EDDN. The relevant sources are
[EdAstroPublisherTests](../../tests/SrvSurvey.Core.Tests/EdAstro/EdAstroPublisherTests.cs),
[EdAstroTrafficBudgetTests](../../tests/SrvSurvey.Core.Tests/Exploration/EdAstroTrafficBudgetTests.cs),
[EdAstroSharingDialogTests](../../tests/SrvSurvey.Desktop.Tests/Controls/EdAstroSharingDialogTests.cs),
[NetworkPrivacySettingsStoreTests](../../tests/SrvSurvey.Desktop.Tests/Configuration/NetworkPrivacySettingsStoreTests.cs),
[EddnSettingsMarkupTests](../../tests/SrvSurvey.Desktop.Tests/Coverage/EddnSettingsMarkupTests.cs),
and [MainWindowViewModelTests](../../tests/SrvSurvey.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs).
These test results validate current local behavior against the fixtures; they
do not validate a successful live upload or every deployed server result.

Only this Markdown report was added. Pre-existing modifications to the
Exploration POI view and its tests, and the untracked Bazzite comparison report,
were preserved. No C# or desktop markup was changed, so no new-code analyzer,
formatter, or coverage result is claimed for this audit.

## Implementation follow-up

After the audit, the user authorized the requested-event notices and wording
updates, while retaining numeric version reporting. The publisher now logs
unsupported requested types once per changed set during an application session;
ordering and duplicates do not produce repeated notices. Unsupported types
remain excluded from uploads. The sharing disclosure and service-rejection
diagnostic no longer suggest an application-registration requirement.
See [publisher](../../src/SrvSurvey.Core/EdAstro/EdAstroPublisher.cs),
[sharing disclosure](../../src/SrvSurvey.Desktop/EdAstroIntegrationDialog.axaml),
and [updated contribution documentation](../EDASTRO.md).

The required changed-code checks passed, including all 2,196 Core tests and
100% changed-production line-and-branch coverage (16/16 coverage points).
New tests exercise unsupported-type reporting, unchanged daily policies,
changed policies, invalid list entries, and preservation of upload filtering.
No live journal POST was performed during implementation validation.
