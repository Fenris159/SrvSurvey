# Colonization shopping market search

Researched 2026-10-16 against RavenColonialWeb commit
`1b65322073bcdc39803776a43dce6bbc873bce87`, Ardent API commit
`8531e4cea1043c02465adc2430d980035ed8d43c` (version 6.4.1), and live
Spansh/Ardent responses. This records provider contracts and UI behavior for an
independent C# implementation; no upstream implementation was copied.

## Raven Colonial behavior to preserve

- `WhereToBuy` is an approximately 800px side panel with editable reference
  system, minimum landing pad size, maximum system distance (ly), maximum arrival
  distance (ls), sufficient-stock, exclude-surface, exclude-carrier, and shipyard
  filters. Its web radius control reaches 1,000 ly and uses zero for unlimited.
  Cached defaults are 500 ly, medium pads, excluded carriers, required sufficient
  stock, and unrestricted arrival/surface locations. The web frontend posts
  positive commodity needs to Raven's own `/api/project/markets` endpoint; direct
  Spansh query code is in its backend, outside this web repository.
  [Component](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/components/WhereToBuy/WhereToBuy.tsx),
  [defaults](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/local-storage.ts#L187),
  [API call](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/api/project.ts#L132).
- The help describes combining the five nearest markets for each commodity.
  Rows show station, pad/location/economy indicators, count of matching goods,
  system, distance, and arrival. Each expands to commodity/stock entries. All
  columns sort. Current component code initializes distance ascending; help still
  describes sorting by match count. Use the observed code for that discrepancy.
  [Help](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/views/About.tsx#L229),
  [component](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/components/WhereToBuy/WhereToBuy.tsx).
- Commodity controls sort alphabetically by display name and toggle highlights.
  “Only highlights” includes a market when it has **any** highlighted commodity
  (OR); zero highlights shows all. “Expand highlights” opens matching rows.
  Both needs and stock controls toggle the same highlight set. Insufficient
  supply is compared against that commodity's aggregate need and marked with a
  tooltip and dimmer theme color. Fleet Carrier mode can subtract known cargo.
  The user-requested comma-separated toggle list and green/red sufficiency are
  desktop adaptations of these interactions.
  [Highlight and supply rendering](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/components/WhereToBuy/WhereToBuy.tsx#L757).

## Spansh contract, verified live

`POST https://spansh.co.uk/api/stations/search`, JSON, zero-based page. This
request returned 26 stations and five rows, including Galileo and Daedalus in Sol
and three Duamta stations. Supply, arrival, and pad ranges use `comparison` and
array `value`; **distance alone uses `min`/`max`**. Group exclusions use uppercase
`logic: "NOT"`.
[Official field catalog](https://spansh.co.uk/api/stations/searchable_fields),
[official frontend request builder](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js),
[recorded search](https://spansh.co.uk/api/stations/search/recall/7F1106C2-C980-11F1-8E2F-8393E2A7A683).

```json
{
  "reference_system": "Sol",
  "size": 5,
  "page": 0,
  "sort": [{ "distance": { "direction": "asc" } }],
  "filters": {
    "distance": { "min": 0, "max": 30 },
    "market": [{
      "name": "Steel",
      "supply": { "comparison": "<=>", "value": [100000, 1000000000] }
    }],
    "type": { "value": ["Fleet Carrier"], "logic": "NOT" },
    "is_planetary": { "value": false },
    "has_large_pad": { "value": true }
  }
}
```

The newer supported equivalent is
`marketplace: [{commodity: {value: ["Steel"]}, supply: {comparison: "<=>", value: [1, 1000000000]}}]`.
Its commodity value list expresses OR. A live Sol/10 ly search with both Steel
and Water returned 70 stations in one page with size 100, covering 24 Steel
markets and 47 Water markets. Shopping now uses one combined query, pages of
100, and sequential pagination up to the shared 10,000-result boundary. This
replaces the old client-side cutoff of five 20-station pages per commodity.
Provider limits and later-page failures produce one summary per provider and
retain observations already fetched. A lower supply bound of 1 retains useful shortfalls; using the
entire need hides them. Choose a high enough upper bound for supported quantities.
[New field schema](https://spansh.co.uk/api/stations/searchable_fields),
[market names and ranges](https://spansh.co.uk/api/stations/field_values/market).

**Do not use `selling_commodities` or `buying_commodities` in new code.** These
fields are absent from the current official catalog and the live service silently
ignored `selling_commodities`. A Sol/30 ly/Steel request using it returned 7,738
stations, including zero-stock and absent-Steel markets. The validated `market`
range returned 286 candidates for minimum stock 1.
[Official supported fields](https://spansh.co.uk/api/stations/searchable_fields).

Response envelope is `{count, from, size, reference, results, search,
search_reference}`. `reference` supplies `id64`, name, and x/y/z. Each station has
numeric `market_id` (also string `id`), `name`, `system_name`, `system_id64`,
`system_x/y/z`, `distance`, `distance_to_arrival`, `type`, `is_planetary`,
`has_large_pad`, pad counts, and `market_updated_at`. `market` contains all goods,
including zero-stock entries, with display-name `commodity`, `supply`,
`buy_price`, `sell_price`, and `demand`. Select positive supplies locally.
`buy_price` is the price the player pays. Galileo's recorded Steel entry was
480,356 stock at 4,151 credits; Ardent's independent later observation can differ.
[Recorded response](https://spansh.co.uk/api/stations/search/recall/7F1106C2-C980-11F1-8E2F-8393E2A7A683).

## Ardent contract, verified against source and service

Use one GET per requested commodity:

```text
https://api.ardent-insight.com/v2/system/name/Sol/commodity/name/steel/nearby/exports?minVolume=1&maxDistance=30&maxDaysAgo=30&fleetCarriers=false&sort=distance
```

The system identifier supports `name` or `address`. Nearby exporters return up to
1,000 rows; ordering defaults to price, while `sort=distance` is supported.
`maxDistance` defaults to 100, is integer-parsed, and is clamped to 500 ly.
`fleetCarriers=false` excludes them, true selects only them, and omitted includes
both. `minVolume` defaults to 1; `maxPrice` is optional. Nearby queries exclude
the reference system. Add one
`/system/name/{system}/commodities/exports?minVolume=1&maxDaysAgo=30&fleetCarriers=false`
request for origin-system stock and filter it to wanted commodities.
[Nearby route](https://github.com/iaincollins/ardent-api/blob/8531e4cea1043c02465adc2430d980035ed8d43c/router/api/systems/commodity.js#L132),
[same-system route](https://github.com/iaincollins/ardent-api/blob/8531e4cea1043c02465adc2430d980035ed8d43c/router/api/systems/commodities.js#L100).

The response is a JSON array. Each observation provides:

| Meaning | Ardent field |
| --- | --- |
| Commodity canonical key | `commodityName` |
| Market identity and display | `marketId`, `stationName`, `stationType` |
| Location | `systemAddress`, `systemName`, `systemX`, `systemY`, `systemZ` |
| Supply and player purchase price | `stock`, `buyPrice` |
| Observation timestamp | `updatedAt` |
| Minimum travel/ship filters | `distanceToArrival`, `maxLandingPadSize` (1/2/3) |
| Distance from origin | `distance` on nearby rows, rounded whole ly |

Nearby distance filtering uses the rounded value too. Recalculate Euclidean
distance from coordinates if enforcing an exact radius. Same-system rows omit
`distance`. Current source defaults `maxDaysAgo` to 90 although README says 30;
specify it. Ardent exposes no shipyard flag or explicit surface flag on commodity
rows. Known surface types include `CraterOutpost`, `CraterPort`,
`OnFootSettlement`, `PlanetaryConstructionDepot`, and `SurfaceStation`; unknown
metadata should remain unknown. The live station-type catalog also includes
`StrongholdCarrier`, while the API's carrier filter matches `FleetCarrier` only.
[Returned columns and rounded filter](https://github.com/iaincollins/ardent-api/blob/8531e4cea1043c02465adc2430d980035ed8d43c/router/api/systems/commodity.js#L165),
[current constants](https://github.com/iaincollins/ardent-api/blob/8531e4cea1043c02465adc2430d980035ed8d43c/lib/consts.js#L35),
[live station types](https://api.ardent-insight.com/v2/stats/stations/types).

Canonical Ardent names match journal keys. Confirmed live: `cmmcomposite` is
**CMM Composite**, `ceramiccomposites` is **Ceramic Composites**;
`ceramiccomposite` returns 404. Spansh filters are case-sensitive: live
`CMM composite` returned no results while `CMM Composite` returned 444. However,
`Liquid oxygen` is correct and `Liquid Oxygen` returned no results. Existing UI
labels are insufficient as provider names; resolve exact official names.
[Raven mapping](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/types.ts#L461),
[CMM lookup](https://api.ardent-insight.com/v2/commodity/name/cmmcomposite),
[ceramic lookup](https://api.ardent-insight.com/v2/commodity/name/ceramiccomposites).

Special provider names and legacy key aliases:

| Current journal/Ardent key | Legacy key, if present | Exact Spansh name |
| --- | --- | --- |
| `agriculturalmedicines` | | Agri-Medicines |
| `hazardousenvironmentsuits` | | H.E. Suits |
| `heliostaticfurnaces` | `microbialfurnaces` | Microbial Furnaces |
| `terrainenrichmentsystems` | `landenrichmentsystems` | Land Enrichment Systems |
| `mutomimager` | `muonimager` | Muon Imager |
| `combatstabilisers` | `combatstabilizers` | Combat Stabilisers |
| `liquidoxygen` | | Liquid oxygen |
| `biowaste` | | Biowaste |
| `structuralregulators` | | Structural Regulators |

Canonicalize the four legacy keys before Ardent queries and when matching Spansh
responses back to needs. Ardent's live commodity report contains current compact
keys plus some display-name records; normalizing display names alone loses the
three internal renames above.
[Raven's corrections and mapping](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/types.ts#L436),
[official Spansh names](https://spansh.co.uk/api/stations/field_values/market),
[live Ardent report](https://api.ardent-insight.com/v2/commodities).

Ardent routes are anonymous. Documentation currently states no enforced rate
limit and requests respectful use; bounded concurrency and existing response
caching are appropriate. API/source defaults can evolve.
[API documentation](https://github.com/iaincollins/ardent-api/blob/8531e4cea1043c02465adc2430d980035ed8d43c/README.md#rate-limits).

## Integration decisions supported by the evidence

- Use a common editable 1–500 ly radius so both providers cover the same search.
- Aggregate positive remaining needs across selected builds/planned sites.
  Planned requirements are estimates; a tracked planned site and its active
  build must not count twice.
- Identify stations by market ID and observations by market ID plus canonical
  commodity. Never sum the same stock from Spansh and Ardent; choose the freshest
  valid timestamp per commodity and retain provider provenance. Group stations
  under systems if desired, while showing exactly which station has each good.
- Keep a successful provider's results when the other fails and make partial
  status visible. Missing or old data is not proof that a station has no stock.
- Apply commodity highlight filters locally to already-loaded results, without
  network requests. At least one highlighted available commodity is sufficient
  to keep the row; clearing all highlights hides every row until a commodity is
  selected again.

These are implementation recommendations, not extra upstream API guarantees.

## Source and licensing boundary

RavenColonialWeb is GPL-3.0 and Ardent API is AGPL-3.0. This work uses their public
behavior and wire contracts as references. Keep the desktop implementation
independent rather than copying backend/frontend code wholesale.
[Raven license](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/LICENSE),
[Ardent license](https://github.com/iaincollins/ardent-api/blob/8531e4cea1043c02465adc2430d980035ed8d43c/LICENSE).

## Carrier manifest commodity metadata

The manifest category catalog also includes the standard and rare commodity data
from [EDCD/FDevIDs](https://github.com/EDCD/FDevIDs/tree/c35612952dd6a547d1a7ac4cffab9c7051e86579),
retrieved 2026-10-17. `manifest-commodity-names.json` records the symbol, English
name and category from `commodity.csv` and `rare_commodity.csv`. Colony-specific
names retain the existing Raven labels. The Mining convenience group is not used
as a category. A commodity is considered a colony good only when it appears in
the bundled construction-cost catalog, so carrier fuel such as Tritium remains
available when the colony-only filter is turned off.

## Saved Spansh paging and bounded Ardent concurrency

Spansh's official frontend uses `GET stations/search/recall/{search_reference}/{page}`
for subsequent pages (API page 1 is the second page). Verified against the public
API on 2026-10-17 with a size-1 Steel/Water query near Sol: the cached page retained
the count and page size and returned a different station. The desktop now retains
the initial POST's reference and recalls later pages, falling back to POST paging
when an older response omits the reference. Source:
[Spansh frontend bundle](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js).
Ardent nearby commodity requests use a maximum of three concurrent requests after
the origin-system lookup. Provider failures still retain all completed quotes.
