# Raven body catalog upload and recovery

Researched 2026-11-14 against upstream SrvSurvey commit
`ba86777989765ccf07bbdd9d38f5aee4e883bf6b`, RavenColonialWeb commit
`9d1709d86561f360961a8e793eb1bd61006e4ac7`, and the live public provider APIs.
Only anonymous GET requests were made. No Raven data was changed. Public response
snapshots are available locally under `/tmp/raven-body-recovery/`.

## Upstream automatic upload contract

The user opens the Raven updater manually. Its constructor fetches the current
system through `GET /api/v2/system/{id64}`. If `bodies` is null, the updater
automatically calls `POST /api/v2/system/{id64}/import/bodies`. That import asks
Raven to obtain data itself; it does not upload local journal bodies.
[Updater initialization](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/forms/FormRavenUpdater.cs#L104-L119).

`nextPhase()` makes these checks, in order:

1. If no `FSSAllBodiesFound` record exists, a `FSSDiscoveryScan` must exist and
   its `Progress` must be at least 1. Otherwise the updater asks for a discovery
   scan or completion of the FSS.
2. Expected count is `lastAllBodies.Count ?? lastHonk.BodyCount`.
3. Local journal body count must be at least the expected count. Otherwise it
   asks the user to scan the nav beacon.
4. If Raven's current `bodies.Count` is smaller than that expected count and
   local `systemData` exists, call `ColonyData.updateSysBodies(systemData)`.
5. On success, replace the updater's body list with the returned list, rebuild
   the body choices, and continue its phase checks.

These are count checks, not field comparisons or server/body matching. Equal or
higher Raven counts do not trigger automatic replacement even if properties or
individual identities are wrong. Counts are not normalized to exclude Raven's
asteroid-belt or barycentre records.
[Checks and successful response handling](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/forms/FormRavenUpdater.cs#L556-L606).

The local count is the union of unique `Scan.BodyID` and `ScanBaryCentre.BodyID`
for that system address. The updater walks journal entries backward across about
ten FSD jumps, retaining the latest scan for each ID. It stops earlier after a
previous jump if its scan count equals the last honk's count.
[Journal collection](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/forms/FormRavenUpdater.cs#L663-L718).

Rechecks are driven by `FSSDiscoveryScan`, `FSSAllBodiesFound`, and `NavBeaconScan`
events, the Next button, Reload, and phase progression. Individual `Scan` events
have no specialized updater handler and do not directly initiate PUT requests.
Initial loading starts in the preamble phase and does not itself run
`nextPhase()`; Next or a relevant incoming event starts that check. Reload
explicitly runs the check after loading.
[Event handlers](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/forms/FormRavenUpdater.cs#L944-L966),
[Next and Reload](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/forms/FormRavenUpdater.cs#L529-L542).

The separate manual system update menu calls the same local builder without
the updater's Raven-count comparison. The menu is shown only when local
`fssAllBodies` is true, and the builder independently requires that flag and
available game journals.
[Menu visibility](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/Main.cs#L1695-L1697),
[manual upload](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/Main.cs#L1827-L1844),
[builder guard](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/game/ColonyData.cs#L569-L575).

## Local conversion and replacement

The builder constructs a fresh `Bod[]` from local system bodies. It copies body
IDs, full names, distance in light-seconds, ancestor IDs, class/subtype, radius,
temperature, and gravity; maps body types and feature flags; and skips existing
planetary-ring/asteroid objects. Barycentres become type `bc`.
It does not merge Raven's existing catalog into this payload.
[Conversion](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/game/ColonyData.cs#L577-L651).

Star belts are synthesized as type `ac` with IDs
`100000 + parentBodyId * 100 + beltIndex`. Their parents start with the star's
ID followed by the star's ancestor IDs. Their distance is the star's arrival
distance plus the belt's inner radius in meters divided by 299792458. The list
is sorted by body number, then belts are repositioned by arrival distance among
the associated star's children. Planet rings contribute the `rings` feature;
they are not separate bodies. Missing tidal-lock flags and star-belt details
can be recovered by a backward search through at most 100 journal files.
[Backfill and upload](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/game/ColonyData.cs#L654-L704),
[belt conversion/order](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/game/ColonyData.cs#L707-L764).

The client describes this upload as replacing server body data and issues
`PUT /api/v2/system/{id64}/bodies` with a JSON array. No authentication headers
are added to this particular request. The live OpenAPI declares GET and PUT
for the bodies route, and a feature-only PUT for
`/api/v2/system/{nameOrNum}/{bodyNum}/features`; it declares no body PATCH route,
security schemes, or body-operation authentication requirement. This is evidence
of the published contract and client behavior, not proof that anonymous writes
are accepted: no write was attempted during research.
[Upload client](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/net/RavenColonial.cs#L423-L443),
[live API schema](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/openapi/v1.json).

Required `Bod` fields are `name`, `num`, `distLS`, `parents`, `type`, and
`features`; optional properties are `subType`, `radius`, `temp`, and `gravity`.
Feature values are `bio`, `geo`, `rings`, `volcanism`, `terraformable`, `tidal`,
`landable`, and `atmosphere`. The upstream C# class initializes missing physical
values to -1. The web model additionally knows `hostBC`, so round-tripping known
and unknown existing properties is preferable to reconstructing Raven records
from a limited DTO.
[Bod DTO](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/net/RavenColonial.cs#L860-L872),
[web body model](https://github.com/njthomson/RavenColonialWeb/blob/9d1709d86561f360961a8e793eb1bd61006e4ac7/src/types2.ts#L50-L64),
[live API schema](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/openapi/v1.json).

## Live example: Synuefai CX-V c18-6

System ID64: **1732784263842**. Each provider returned that exact name and system
address. Observations describe the captured responses, not future completeness.

| Provider | Captured catalog | Identity caveats |
|---|---|---|
| Raven | 37 entries: 1 star, 31 planets/moons, 3 barycentres, 2 belts | Body numbers present on every entry. All 37 `radius` values are -1. |
| Spansh | 35 body entries: 1 star, 31 planets/moons, 3 barycentres; two belts nested under the star | `bodyCount` is 32, despite 35 body records. All records have `bodyId`. |
| EDSM | 32 entries: 1 star, 31 planets/moons; two belts nested under the star | Main star `bodyId` and parents are null. EDSM's database `id` is not the game's BodyID. No barycentre records. |

The 32 EDSM bodies all match names in Spansh. The three extra Spansh records are
barycentres 32, 43, and 59. Expanding Spansh's two star belts produces the same
37 names/identities already present in Raven. Thus this captured example exposes
missing physical detail, rather than a missing catalog entry. For example, body
13 has radius 2636.74625 km in both external sources, while Raven has -1.
[Raven system](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/api/v2/system/1732784263842),
[Raven body list](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/api/v2/system/1732784263842/bodies),
[Spansh dump](https://spansh.co.uk/api/dump/1732784263842),
[EDSM body list](https://www.edsm.net/api-system-v1/bodies?systemId64=1732784263842).

### Units and observed differences

- Arrival distances are light-seconds. Spansh includes fractional distances;
  EDSM returns rounded whole light-seconds in this sample.
- Planet radii are kilometers in the provider responses. Upstream's local
  upload copies its journal radius in meters, so multiplying provider planet
  radii by 1000 matches that existing client convention. Stars use solar radii;
  upstream converts those to meters with 695700000 when importing EDSM data.
  The sample star is approximately 602389367.037 meters in radius.
- Spansh star-belt inner/outer radii are meters; EDSM's corresponding values are
  kilometers. Sample A Belt inner radii are 960780000 and 960780 respectively.
- Temperatures are kelvin. EDSM's values are less precise in this sample.
- **Gravity needs deliberate treatment.** Raven's website labels `gravity` as
  **g**, and the captured Raven values exactly match Spansh's gravity in g
  (body 13: 0.794244417252983). EDSM returns a slightly different value
  (0.7949678005762778). Upstream's local upload copies journal SI surface gravity
  directly, although the web UI labels it g. Matching upload triggers does not
  require reproducing that apparent unit inconsistency. Prefer provider g for
  external recovery and convert journal SI gravity to g explicitly.

Sources: the [captured provider routes above](https://spansh.co.uk/api/dump/1732784263842),
[upstream EDSM conversion](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/game/SystemData.cs#L1498-L1506),
[local upload properties](https://github.com/njthomson/SrvSurvey/blob/ba86777989765ccf07bbdd9d38f5aee4e883bf6b/SrvSurvey/game/ColonyData.cs#L583-L597),
[Raven gravity label](https://github.com/njthomson/RavenColonialWeb/blob/9d1709d86561f360961a8e793eb1bd61006e4ac7/src/views/SystemView2/BodyCard.tsx#L182-L185).

## Recommended manual recovery behavior

The following is a proposed client policy, not a server merge guarantee:

1. Validate the selected loaded system ID64 against both providers' returned
   root identity. Fetch the current Raven list immediately before preparing the
   update. Use the ID64 in all requests so similarly named systems cannot be
   confused.
2. Target the **loaded editor system's ID64**, even while the commander is in a
   different live system. This operation requires no local journal scan or
   completed-FSS record. Use game BodyID as canonical identity. Where a provider lacks BodyID (the
   sample EDSM main star), resolve only through an unambiguous exact full-name
   match in a validated system. Reject conflicting identities or ambiguous
   names instead of inventing an ID from EDSM's database key.
3. Build the proposed body identities from the **union of Spansh and EDSM**, not
   the union with Raven. Preserve current properties and unknown fields for
   matching Raven records, and add absent external bodies. Union positive
   feature flags rather than deleting Raven observations that another provider
   omits. Use Spansh first and EDSM to fill gaps, making precedence deterministic.
   Preserve existing populated Raven physical values; fill missing/-1/nonfinite
   values from external data.
4. Expand star belts once, following upstream's synthesized ID and distance
   convention. Keep barycentres even when EDSM does not list them. Do not compare
   raw record count directly with a provider `bodyCount` to establish completeness.
5. Identify every Raven-only body absent from the proposed provider union. Before
   a PUT that removes any such body, show a **Yes/No confirmation listing all
   removed body names/IDs**. A No response leaves Raven untouched. Raw count
   comparison alone is insufficient: equal-sized lists can have different
   identities. The captured sample produces the same 37 names and no removals.
6. Submit the confirmed full unified list with PUT, then use the returned list in the
   editor. Do not call the sites update endpoint or discard unsaved site edits.
7. If provider recovery produces no usable records, do not send an empty PUT.
   Report missing/unavailable providers accurately. A partial provider result
   cannot silently remove existing Raven bodies; every proposed removal requires
   the explicit confirmation above.

Read/merge/write is not atomic: the public contract has no documented body PATCH,
ETag precondition, or fill-blanks option. A final fresh Raven read reduces the
window for overwriting concurrent changes but cannot eliminate it.
[Published body routes and models](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/openapi/v1.json).


## Implementation validation

The local Kilo CLI reviewed the full branch with the authenticated Kilo Gateway
OpenAI subscription connection, `kilo/openai/gpt-6.1-sol`, variant `xhigh`.
Follow-up passes reproduced and fixed scan filtering, continuation attribution,
commander buffer/cancellation boundaries, catalog hydration, missing-name and
malformed-belt failures. The final pass reported **No findings**.

The required local gate passed a Sonar-profile build with all warnings treated
as errors, 3,820 Core tests, 140 selected Desktop/scenario tests, all 66 mandatory
journal-upload scenarios, and 89.1% changed production line/branch coverage
(1,301/1,460 points). The final pre-commit gate repeats validation with the last
diagnostic-replay regression included. Eleven deliberately broken copied binary
variants were each caught by behavioral tests; original assemblies were unchanged.

An authorized live check of **Synuefai CX-V c18-6**, ID64 **1732784263842**,
used the production recovery/conversion and Raven clients. Both providers were
read, a fresh Raven catalog was merged, and no removals were proposed. The PUT
returned 37 bodies; a separate GET confirmed the same 37 IDs and 32 populated
star/planet radii (previously none). All 13 site records were byte-equivalent as
serialized before/after. No site-update endpoint was called. Public response
snapshots and the submitted catalog were retained locally under
`/tmp/raven-body-recovery/` for verification.
