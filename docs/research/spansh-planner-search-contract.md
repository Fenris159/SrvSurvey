# Spansh planner search contract

Checked: 2026-10-14, America/Chicago. Primary sources: Spansh's current live
forms, shipped application JavaScript, English translations and public GET
responses. Scope: Neutron, Road to Riches, Tourist, Galaxy, Expressway to
Exomastery and Colonisation. Fleet Carrier Router belongs exclusively in the
Fleet Carrier workspace. Ammonia/Earth-like/Rocky-HMC and trade planners are
excluded from the requested feature.

## Source snapshot

The live app reports version `0.1.903+d0f304f7`. Its application bundle is
[`elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js`](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
1,113,640 bytes, SHA-256
`982fb3d8fa7ad478c0fb1b3b506d411adfc9dc50f15a87b445651970012f79f2`.
English form/help text comes from
[`vendor-e211a712d23dc02baf30b3b446e25890.js`](https://spansh.co.uk/assets/vendor-e211a712d23dc02baf30b3b446e25890.js),
SHA-256 `ad9148e8e07059fba0bea58bc83bfb6cb6d9c2464d440a6bb2e35538ea9d8c94`.
The current [official OpenAPI docs](https://docs.spansh.co.uk/) are version
3.1.1 and describe dump/system/body/station GETs, not planner submission
endpoints. The served frontend is therefore the primary contract evidence
for planner POSTs and current form defaults. Hashed assets may later be removed;
their retrieval date and hashes identify this observation.

Temporary extracted, formatted AMD modules are in `/tmp/spansh-planner-source`.
Relevant module names are `services/api`, `objects/ship`,
`controllers/{planner}`, `templates/{planner}` and
`routes/base/plotter/new-results`, all under `elite-dangerous-gui/`.

## Shared submission, lookup and polling

The frontend sends all scoped planner submissions as HTTP POST with ordinary
form parameters through jQuery `ajax({method: "POST", data, traditional: true})`.
Use `application/x-www-form-urlencoded`, invariant numeric formatting,
checkboxes encoded as `1`/`0`, and array values as **repeated unbracketed keys**,
for example `destination=A&destination=B`. Neither JSON arrays nor `key[]`
matches the website's serialization. The frontend enables credentials; that
does not establish that anonymous public routes require authentication.
The separate Engineer planner uses JSON POST, but is outside the six requested
ship planner forms verified in the menu.
[Primary app bundle, `services/api`](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

| Planner | Submit endpoint | Public result page | `result` shape consumed by the frontend |
| --- | --- | --- | --- |
| Neutron | `/api/route` | `/plotter/results/{job}` | Object with `system_jumps` |
| Road to Riches | `/api/riches/route` | `/riches/results/{job}` | Array of system/body records |
| Tourist | `/api/tourist/route` | `/tourist/results/{job}` | Object with `system_jumps` |
| Galaxy | `/api/generic/route` | `/exact-plotter/results/{job}` | Object with `jumps` |
| Expressway to Exomastery | `/api/exobiology/route` | `/exobiology/results/{job}` | Array of system/body/landmark records |
| Colonisation | `/api/colonisation/route` | `/colonisation/results/{job}` | Object with `jumps` |
| Fleet Carrier | `/api/fleetcarrier/route` | `/fleet-carrier/results/{job}` | Object with `jumps` |

Successful submission is recognized as `{"status":"queued","job":"..."}`.
The frontend also recognizes `{"status":"error","error":"..."}` and checks
HTTP failure response JSON for `error`. Poll **GET `/api/results/{job}`** for
all scoped public planners. It considers `status: "ok"` complete. Responses
also carry `state`, `created_at`, `updated_at` and `parameters` for status/form
restoration. Polling starts with 250 ms state, doubles before each request
and caps at 16,000 ms. A 404 causes the website to re-submit the calculation;
SrvSurvey should surface expired/missing jobs explicitly instead of silently
creating duplicate work. Use cancellation, bounded responses and a deadline.
[Primary app bundle, `routes/base/plotter/new-results`](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

Names and ID64s differ by planner. The current forms use:

- Neutron autocomplete: GET [`/api/systems?q=Sol`](https://spansh.co.uk/api/systems?q=Sol),
  returning a string array. Neutron POSTs names.
- Other name-based forms and Galaxy/FC autocomplete: GET
  [`/api/systems/field_values/system_names?q=Sol`](https://spansh.co.uk/api/systems/field_values/system_names?q=Sol).
  Response includes `values` (names) and `min_max` (objects with `id64`,
  `name`, `x`, `y`, `z`). Galaxy/FC use a result converter selecting `min_max`
  records, then POST ID64s; the other scoped forms POST names.
- The frontend also has `/api/search/systems?q=...` for resolving name-based
  imported waypoints. It consumes `results[0]`. Do not confuse that API with
  the field-values response.

These are exact frontend choices. Acceptance of additional name/ID aliases by
the backend was not established. Match the observed contract and retain ID64
as an integer/string without lossy floating-point conversion.

## Neutron Route Plotter

Form: [Neutron Route Plotter](https://spansh.co.uk/plotter).

| POST key | Input/default | Form bounds/options |
| --- | --- | --- |
| `from` | Source name | Required for a meaningful calculation |
| `to` | Destination name | Required |
| `via` | Ordered intermediate system names, default empty | Repeated key; add/delete/reverse controls |
| `range` | Ship range, default blank | Number, 10–100 LY, fractional allowed |
| `efficiency` | 60 | Slider 0–100 percent |
| `supercharge_multiplier` | 4 | Visible regular/overcharge radios: 4 or 6 |

The calculator forwards the fields directly. Its form bounds are client
attributes, not a claim that the backend accepts exactly or only those limits.
`result.system_jumps` includes `system`, coordinates, `distance_jumped`,
`distance_left` and `jumps` as consumed by the result/map/export code.
[App modules `controllers/plotter`, `templates/plotter`, `controllers/plotter/results`](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

## Road to Riches and Expressway to Exomastery

Forms: [Road to Riches](https://spansh.co.uk/riches),
[Expressway to Exomastery](https://spansh.co.uk/exobiology).

| POST key | Riches default | Exomastery default | Form constraints |
| --- | --- | --- | --- |
| `from` | Source name | Source name | Required |
| `to` | Omitted when blank | Omitted when blank | Optional name |
| `range` | Blank | Blank | 10–100 LY, fractional allowed |
| `radius` | 25 | 25 | 1–1,000 LY |
| `max_results` | 100 | 100 | 1–2,000 systems |
| `max_distance` | 50,000 | 50,000 | Maximum distance to arrival, dynamic slider bounds |
| `min_value` | 100,000 | 10,000,000 | Minimum scan/landmark value, dynamic bounds |
| `avoid_thargoids` | 1 | 1 | Checkbox |
| `loop` | 1 | 1 | Checkbox |
| `use_mapping_value` | 0 | Not sent | Riches-only checkbox |

Riches help describes blank destination as a circular tour; Exomastery help
describes blank destination as an unrestricted finishing location. Loop
explicitly requests returning to the starting system. The form describes
radius as the search corridor radius, suggesting larger radii for circular
tours. `use_mapping_value` changes Riches ranking to DSS mapping rewards.
The Riches template loads scan, mapping and distance field bounds, although
its current minimum-value slider is wired to `scanValueLoad`.
[App controllers/templates and vendor English help](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[English translations](https://spansh.co.uk/assets/vendor-e211a712d23dc02baf30b3b446e25890.js).

Current public field-value observations:

| GET | Returned `min_max` entry |
| --- | --- |
| [`/api/bodies/field_values/distance`](https://spansh.co.uk/api/bodies/field_values/distance) | `distance`: min 0, max 139691.320211751 |
| [`/api/bodies/field_values/estimated_scan_value`](https://spansh.co.uk/api/bodies/field_values/estimated_scan_value) | min 0, max 343608 |
| [`/api/bodies/field_values/estimated_mapping_value`](https://spansh.co.uk/api/bodies/field_values/estimated_mapping_value) | min 555, max 1248441 |
| [`/api/bodies/field_values/landmark_value`](https://spansh.co.uk/api/bodies/field_values/landmark_value) | min 0, max 90407300 |

These are current dataset-derived ranges, not stable API validation maxima.
The distance endpoint also returns coordinate bounds. Keep the submitted
maximum-arrival-distance semantics separate from generic system-distance
metadata; do not infer units just from this endpoint's key.

Riches result arrays contain systems with `name`, coordinates, `jumps`, and
`bodies`; its result exporter uses body name, subtype, arrival distance and
estimated scan/mapping values. Exomastery additionally uses body landmarks,
types/subtypes/variants and their values. Preserve full result metadata for
the existing route-following/body workflows.
[Result controllers in the app bundle](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

## Tourist Router

Form: [Tourist Router](https://spansh.co.uk/tourist).

POST `source` (starting name), `final_destination` (optional ending name),
`destination` (repeated waypoint names), `range` (blank initially, UI 10–100 LY),
and `loop` (default 1). Waypoint input supports add/delete and text-file import;
help describes one system name per line. The purpose is to optimize the visiting
order. `result.system_jumps` is consumed by the result controller. Preserve
the destinations as a list rather than one comma-separated string.
[App controller/template/result modules](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[Tourist help in vendor translations](https://spansh.co.uk/assets/vendor-e211a712d23dc02baf30b3b446e25890.js).

## Galaxy Plotter: raw SLEF plus derived drive parameters

Form: [Galaxy Plotter](https://spansh.co.uk/exact-plotter).

**A valid raw SLEF export is accepted in the ship-build box. No conversion to
Coriolis detailed JSON is needed.** The `objects/ship` constructor JSON-parses
the pasted string, selects the first entry of an array, and accepts either
the Coriolis v4 `$schema` or a SLEF object with `header` and `data`. A bare
Journal Loadout must first be wrapped as SLEF. The website calculates drive
parameters locally, then submits them **alongside the original ship-build
JSON string**; sending only `ship_build` does not match its request contract.
[App bundle `objects/ship` and `controllers/exact-plotter`](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

| POST key | Default/derivation |
| --- | --- |
| `source`, `destination` | Selected system ID64s |
| `ship_build` | Original pasted/exported JSON string |
| `fuel_power` | Base FSD fuel-power coefficient |
| `fuel_multiplier` | Base FSD fuel multiplier |
| `optimal_mass` | Base FSD optimal mass, overridden by actual `FSDOptimalMass` engineering modifier |
| `max_fuel_per_jump` | Base FSD maximum fuel, overridden by actual `MaxFuelPerJump` modifier |
| `tank_size` | `data.FuelCapacity.Main` |
| `internal_tank_size` | `data.FuelCapacity.Reserve` |
| `base_mass` | `data.UnladenMass + data.FuelCapacity.Reserve` |
| `range_boost` | Fitted Guardian FSD booster bonus, otherwise zero |
| `reserve_size` | User's extra fuel to reserve from jumping, default 0 |
| `cargo` | User's cargo mass, default 0 |
| `max_time` | Calculation time in seconds, default 60 |
| `algorithm` | Default `optimistic` |
| `supercharge_multiplier` | Default 4; current 8A Mk II special SCO override is 6 |
| `injection_multiplier` | Default 2; only replaced if matched drive data supplies a value |
| `is_supercharged` | 0 |
| `use_supercharge` | 1 |
| `use_injections` | 0 |
| `use_injections_when_required` | 0 |
| `exclude_secondary` | 0 |
| `refuel_every_scoopable` | 1 |

Visible controls are source/destination, build text, cargo (0–1,000), reserve
fuel (0–32), all six checkboxes and algorithm. Algorithms are `fuel`,
`fuel_jumps`, `guided`, `optimistic`, `pessimistic`. Calculation-time slider
appears only when authenticated: minimum 60, maximum 120 normally or 240 for
a patron. Anonymous forms retain the default 60 without displaying the slider.
The ship-profile load button is also authenticated-only. These are frontend
visibility/defaults, not proof of broader backend entitlement.
[Form/controller modules](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[time and checkbox descriptions](https://spansh.co.uk/assets/vendor-e211a712d23dc02baf30b3b446e25890.js).

SLEF validation/derivation implemented by the website:

1. Build text must be present and JSON-parseable; first SLEF array entry must
   have `header` and `data.Modules`.
2. Find the FSD by `Slot` equal to `FrameShiftDrive` case-insensitively.
3. Match its actual `Item` symbol to the bundled FSD table or explicit fallback
   mapping. Unknown FSD produces `missing_fsd`.
4. Use actual `Engineering.Modifiers` values for `FSDOptimalMass` and
   `MaxFuelPerJump`; labels are compared case-insensitively.
5. Read main/reserve fuel and unladen mass. The constructor does not provide a
   complete numeric validation layer; SrvSurvey must reject missing/nonfinite
   values instead of submitting `NaN` or fabricated zeros.
6. Find a Guardian booster by actual `Item` prefix and match its exact symbol
   to its bundled data. The source prefix test is case-sensitive; SrvSurvey's
   own case-insensitive catalogue lookup is safer for Journal casing.

For the EDSY factual snapshot already being added, corresponding source fields
are `fuelpower`, `fuelmul`, `fsdoptmass`, `maxfuel` and Guardian `jumpbst`.
Guardian sizes 1–5 provide 4, 6, 7.75, 9.25, 10.5 LY. The explicit special SCO
fallback `int_hyperdrive_overcharge_size8_class5_overchargebooster_mkii` uses
optimal mass 4670, max fuel 6.8, multiplier 0.011, power 2.5025 and neutron
multiplier 6. Current website fallback mapping has no injection override;
default 2 is therefore the observed injection setting.
[Spansh shipped `objects/ship`](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[EDSY Guardian data](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3479-L3483),
[EDSY special SCO record](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3186).

Galaxy results use `result.jumps`; the frontend exports jump name, coordinates,
`distance`, `distance_to_destination`, `fuel_in_tank`, `fuel_used`, `must_refuel`,
neutron and injection indicators. Preserve those decision fields when
translating results into route hops.
[Galaxy result controller/template in app bundle](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

## Colonisation Plotter

Form: [Colonisation Plotter](https://spansh.co.uk/colonisation).
POST only `source_system` and `destination_system`, both system names.
**Source is optional:** first-party help says blank source requests the
shortest route to the chosen target without choosing its controlling faction.
Destination is the required target. The current form exposes no range, radius,
fuel or ship-build parameters. Results use `result.jumps` with names,
coordinates, `distance` and `distance_to_destination` in the frontend.
[App controller/template/result modules](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[Colonisation description in vendor translations](https://spansh.co.uk/assets/vendor-e211a712d23dc02baf30b3b446e25890.js).

## Fleet Carrier Router: separate workspace only

Form: [Fleet Carrier Router](https://spansh.co.uk/fleet-carrier).

| POST key | Default/options |
| --- | --- |
| `source` | Selected system ID64 |
| `destinations` | Repeated selected waypoint ID64s, preserving order |
| `capacity` | Fleet/player 25,000; squadron 60,000 |
| `mass` | Fleet/player 25,000; squadron 15,000 |
| `capacity_used` | User's used-capacity value, default 0 |
| `calculate_starting_fuel` | Default 1; checkbox |
| `refuel_destinations` | Optional repeated waypoint ID64s when calculating starting fuel |
| `fuel_loaded` | Only when checkbox off; tank Tritium, default 1,000 |
| `tritium_stored` | Only when checkbox off; market Tritium, default 0 |

The current visible form has player/squadron radios, a used-capacity input,
starting system, ordered destinations, import/reorder/delete controls,
per-destination refuelling toggles, and the Tritium-requirements checkbox.
Tank and market Tritium inputs appear only when automatic requirements are
disabled. Tank UI bounds are 0–1,000; used capacity and stored Tritium have
min 0 with max bound to controller `capacity`. The controller explicitly owns
the two carrier capacities but has no `capacity` getter in this snapshot;
use the selected carrier's known capacity for native validation.

`market_capacity` and `module_capacity` persist in older query restoration and
controller support but are **not current visible inputs**. `carrier_type` is
a result-page query parameter, not sent to the plotting endpoint; submitted
`mass`/`capacity` distinguish the type. Selected refuel destinations are
filtered to waypoints actually in the destination list. The controller
integer-parses capacity/fuel inputs and replaces `NaN` with 0; native UI should
validate explicitly instead. Fuel requirements are calculated when checkbox
on; manual fuel fields are omitted in that mode.
[Fleet Carrier controller/template/result modules in app bundle](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).

FC API results use `result.jumps` with `distance`, `distance_to_destination`,
`fuel_in_tank`, `tritium_in_market`, `fuel_used`, `must_restock`, and
`restock_amount` alongside system identity/coordinates. The live 46-stop
Sol → Colonia response reports `must_restock` as numeric `0`/`1`, with 953 t
and 937 t restock recommendations. The frontend CSV exporter names its fuel
column `fuel_remaining`, which differs from this API response: retain that
legacy import alias, but prefer `fuel_in_tank` when parsing API results. Zero
restock amounts represent no recommendation; zero tank fuel is a real balance.
[Captured live API result](https://spansh.co.uk/api/results/41B4D994-C830-11F1-B393-A7063E1886B9).

Additional result totals and refuel metadata should be preserved for the
existing carrier workflow. Website estimates describe
20-minute jumps and 150 tonnes/hour mining; those are UI assumptions rather
than game-state observations.
[FC result modules](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[FC descriptions](https://spansh.co.uk/assets/vendor-e211a712d23dc02baf30b3b446e25890.js).

## Verification limits

Implementation verification subsequently submitted two short live jobs using
the C# planner client and synthetic Sidewinder SLEF: Galaxy job
`d6f47080-c7bf-11f1-b9c5-90f3fa4a62a1` and carrier job
`d9a348d8-c7bf-11f1-9e1b-907d6fbac83f`. Both completed with Sol → Alpha Centauri
and were imported by the existing C# result client. This verifies anonymous
submission and the selected contracts; it does not establish all backend limits.

The subsequent 46-stop carrier regression above is captured in
`tests/Fixtures/Spansh/fleet-carrier-41b4.json`. Core parsing tests and Desktop
clipboard/search integration tests replay it, checking tank balances, numeric
restock flags, positive recommendations, and blank non-restock cells.

This note distinguishes live GET observations, current first-party frontend
behavior and recommended native validation. No backend planner source or
planner OpenAPI schema was available. Research itself did not submit jobs;
extra accepted aliases, backend numeric maxima and exact
failure-state payloads beyond frontend handling remain unverified. Existing
bounded/cancellable route-result polling can be reused after submission;
controlled request fixtures should verify exact field names, repeated-array
encoding, raw SLEF preservation and drive-parameter derivation.
