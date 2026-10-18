# Codex nearest search: Spansh and upstream contracts

Checked: 2026-10-18, America/Chicago. This investigation compares the embedded
Codex taxonomy, original SrvSurvey, Canonn's published server code, and Spansh's
live first-party field catalogs and search responses. Searches were bounded,
read-only queries; no community records were submitted or changed.

## Findings

**Search the selected Codex entry or its explicit descendants.** Tree labels
are presentation groups and do not consistently name provider categories.
Spansh supports landmarks through `POST /api/bodies/search`; its landmark
subtype can represent Odyssey species, an entire named NSP organism, geology,
Guardian/Thargoid objects, anomalies, or green gas giants. A color belongs in
`variant` only when it is a separate Odyssey color. Preserve exact subtype
names such as `Albens Bell Mollusc`.
[Spansh searchable fields](https://spansh.co.uk/api/bodies/searchable_fields),
[Spansh landmark values](https://spansh.co.uk/api/bodies/field_values/landmarks).

**Omit the distance filter and sort ascending by distance from the current
coordinates.** This finds distant matches without an application radius cutoff.
The Bell Mollusc probe found its first result over 9,000 ly away. Spansh returns
bodies, so page and deduplicate systems if the UI promises up to 20 systems.
The first 20 Bell rows represented 16 systems; all 39 rows represented 33.
[Recorded Bell search](https://spansh.co.uk/api/bodies/search/recall/8B187BFA-CB0E-11F1-848B-C65918CFD148),
[second page](https://spansh.co.uk/api/bodies/search/recall/8B187BFA-CB0E-11F1-848B-C65918CFD148/1).

Canonn's generic nearest-Codex endpoint also has no radius restriction. It
accepts name substrings and caps the result at 20, but its server narrows the
candidates by spatial z-order before sorting by Euclidean distance. It therefore
does not establish an exact galaxy-wide nearest-20 guarantee. Use that
limitation explicitly when Canonn is the available fallback.
[Canonn `nearest_codex` source](https://github.com/canonn-science/Canonn-GCloud/blob/ea197d5f647dc3722c09eef09f113c8770427872/query/function/localpackage/challenge.py#L728-L806).

## What the current taxonomy contains

The embedded [`codexRef.json`](../../src/SrvSurvey.Core/Resources/ReferenceData/codexRef.json)
contains 1,070 entries: 814 Biology, 133 Cloud, 53 Anomaly, 23 Geology, 21 Thargoid,
12 None, eight Tourist and six Guardian. Its ordinary stellar-body entries are
eight **green** gas-giant entries; this snapshot contains no ordinary star-class
entries. “Support every Codex entry type” should not assume that all such types
are biology or that arbitrary ordinary star entries are already in this file.

[`CodexBingoCatalog`](../../src/SrvSurvey.Core/Exobiology/CodexBingoCatalog.cs)
builds `hud_category / sub_class / entry` for most records. Odyssey biology adds
species and color nodes; molluscs add a group by removing the first word from
each full entry name. Thus `Cloud / Mollusc / Bell Mollusc / Albens` points to
the complete entry `Albens Bell Mollusc` (2300601). The Bell parent contains
Albens, Lindigoticum, Gypseeum, Blatteum and Luteolum entries. Its `Species`
presentation metadata is not an Odyssey species-and-color contract.

Comparing every embedded English name against the live Spansh landmark values
produces:

| Matching rule | Entries |
| --- | ---: |
| Full English name equals an enumerated subtype | 236 |
| Final ` - ` separates an enumerated subtype and an enumerated color | 786 |
| Neither pattern matches | 48 |

These are lexical coverage counts, not evidence that every species/color
combination has a reported location. The current provider catalog lists
347 subtype names, 65 type names and 24 variant colors. Its JSON is 8,737 bytes
and has this shape:

```json
{
  "min_max": { "value": { "min": 0.0, "max": 20000000.0 } },
  "values": {
    "subtype": ["Albens Bell Mollusc", "Stratum Tectonicas"],
    "type": ["Mollusc", "Stratum"],
    "value": [],
    "variant": ["Green", "Lime"]
  }
}
```

The arrays above are abbreviated. The catalog supplies independent value lists;
it does **not** associate a subtype with its type or enumerate valid
species/color pairs. A cached or embedded lookup can validate exact subtype and
color spellings without inventing type relationships.
[First-party field values](https://spansh.co.uk/api/bodies/field_values/landmarks).

## Spansh request, paging and result contract

The current [Body Search](https://spansh.co.uk/bodies) advertises landmarks,
body types/subtypes, signals and reference coordinates separately. The
[`services/api` module in the shipped frontend](https://spansh.co.uk/assets/elite-dangerous-gui-4ce93861bd384bc982212db7e24f463c.js)
builds JSON requests with `filters`, `sort`, `size`, zero-based `page` and either
`reference_system` or `reference_coords`. Its saved-search API uses
`GET /api/bodies/search/recall/{search_reference}` for page zero, then appends
the zero-based page index for later pages. The public UI URL uses one-based
pages: `/bodies/search/{search_reference}/1`.

This live request found the Bell parent:

```json
{
  "reference_coords": { "x": 0, "y": 0, "z": 0 },
  "size": 20,
  "page": 0,
  "sort": [{ "distance": { "direction": "asc" } }],
  "filters": {
    "landmarks": [{
      "type": { "value": ["Mollusc"] },
      "subtype": { "value": [
        "Albens Bell Mollusc",
        "Blatteum Bell Mollusc",
        "Gypseeum Bell Mollusc",
        "Lindigoticum Bell Mollusc",
        "Luteolum Bell Mollusc"
      ] }
    }]
  }
}
```

The subtype value array expresses **OR**: distinct returned bodies had Albens
or Luteolum, among others. `Bell Mollusc` itself is not an enumerated subtype.
For an exact Albens search retain only its complete name and omit `variant`.
Its returned landmark has `type: Mollusc`, `subtype: Albens Bell Mollusc`, and
no `variant` property.
[Parent results](https://spansh.co.uk/api/bodies/search/recall/8B187BFA-CB0E-11F1-848B-C65918CFD148),
[exact Albens results](https://spansh.co.uk/api/bodies/search/recall/8C27D16C-CB0E-11F1-BC4D-A6784DB2EB58).

**Multiple landmark filter objects are not an OR list.** A probe using one
object for Albens and a second for Luteolum returned zero, although the single
OR-subtype request found both. Do not place unrelated entry predicates into
separate array elements and expect their union. A single subtype list works
for parent groups with no color restriction; different species-specific color
subsets need separate searches and a merge unless another union mechanism is
independently verified.
[Two-object probe](https://spansh.co.uk/api/bodies/search/recall/B2F0CF24-CB0E-11F1-970E-AE3E4EEC085F).

`type` can be omitted when a known exact subtype suffices: a subtype-only
`Green Water Giant` query returned two body records in the same system. This
avoids deriving an incorrect landmark type from `sub_class` or the first word.
Do not omit the subtype and broaden to all objects of its type when the
requested exact entry is absent.
[Subtype-only probe](https://spansh.co.uk/api/bodies/search/recall/B3E043C4-CB0E-11F1-A2A9-CABF0BAAC217).

Every probed direct body search completed synchronously with
`{count, from, size, search, search_reference, results}`. No queued job or
`/api/results/{job}` polling occurred. The public OpenAPI document covers
dump/system/body/station retrieval, not search submissions. Planner job APIs
must not be inferred to apply to these body searches.
[API documentation](https://docs.spansh.co.uk/),
[recorded body envelope](https://spansh.co.uk/api/bodies/search/recall/8B187BFA-CB0E-11F1-848B-C65918CFD148).

Result bodies contain `system_name`, `system_id64`, `system_x/y/z`, `distance`,
body identity, arrival distance and landmarks. The full landmark list can
include unrelated objects, so apply the requested matching predicate when
building notes. The first Bell result was a **Star / T Tauri Star** body with a
Mollusc landmark; a top-level `type: Planet` or `is_landable: true` restriction
would remove a valid NSP match. Keep 64-bit IDs lossless.
[Bell response](https://spansh.co.uk/api/bodies/search/recall/8B187BFA-CB0E-11F1-848B-C65918CFD148).

## Entry families and limits

| Codex selection | Safe Spansh interpretation | Qualification |
| --- | --- | --- |
| Odyssey `Stratum Tectonicas - Lime` | Exact subtype `Stratum Tectonicas`, variant `Lime`; type `Stratum` if independently known | Color is a separate variant. Species parent can omit variant; a subset of missing colors can use a variant OR list. |
| NSP `Albens Bell Mollusc` | Exact full subtype, omit variant | Latin color remains part of subtype. Bell parent is the OR of its five descendant names. |
| Legacy biology such as a Brain Tree or Anemone | Exact full subtype when enumerated | Do not strip Latin prefixes or require an Odyssey color. |
| Geology such as `Iron Magma Lava Spout` | Exact full landmark subtype | A geological signal count or body volcanism does not identify that Codex site subtype. |
| Clouds, crystals, pods, anomalies, Guardian or Thargoid objects | Exact enumerated subtype, with optional independently known type | Some reference entries are absent from current provider values. |
| Green gas giants | Exact `Green …` landmark subtype when enumerated | Ordinary body subtype `Class I gas giant` would return ordinary giants too. |
| Ordinary stars or planets in a future expanded catalog | Explicit map to the separate top-level body `subtype`, only after verifying semantics | Current values include `Neutron Star`, `Black Hole`, `Earth-like world`, etc. This does not establish Codex-specific luminosity, terraformability or green coloration. |

Sources: [landmark values](https://spansh.co.uk/api/bodies/field_values/landmarks),
[body subtype values](https://spansh.co.uk/api/bodies/field_values/subtype),
[signal values](https://spansh.co.uk/api/bodies/field_values/signals),
[searchable-field schema](https://spansh.co.uk/api/bodies/searchable_fields).
The Odyssey probe for `Stratum Tectonicas` with Lime/Green returned 20 rows,
beginning at 36 Ophiuchi, 19.3374 ly from Sol, with separate `variant: Lime`.
[Recorded Odyssey search](https://spansh.co.uk/api/bodies/search/recall/8D589954-CB0E-11F1-848B-C65918CFD148).

The 48 unmatched embedded entries are:

- **Tourist (2):** Green Gas Giant with Water Life; Green Class V Gas Giant.
- **Thargoid (2):** Large Thargoid Barnacle; Thargoid Mega Barnacles.
- **Cloud (19):** Prasinum Aster Pod; Puniceum Aster Pod; Viride Octahedral Pod;
  Blatteum Octahedral Pod; Rubeum Octahedral Pod; Cereum Aster Tree; Rubellum
  Aster Tree; Stolon Tree; Chryseum Void Heart; Viride Gourd Mollusc; Viride
  Torus Mollusc; Blatteum Torus Mollusc; Rubellum Torus Mollusc; Albulum Squid
  Mollusc; Caeruleum Squid Mollusc; Roseum Squid Mollusc; Puniceum Squid Mollusc;
  Rubeum Squid Mollusc; Viride Parasol Mollusc.
- **Anomaly (13):** K02, K07, K11, E01, L03, L05, P06, P08, P10, Q03, Q05,
  Q07 and Q08, each followed by `-Type Anomaly`.
- **None (12):** Thargoid Interceptor Cyclops, Basilisk, Medusa, Hydra and
  Orthrus; Thargoid Hunter Glaive and Scythe; Thargoid Revenants; Thargoid Scout
  Marauder, Berserker, Inciter and Regenerator.

This comparison uses the
[embedded reference](../../src/SrvSurvey.Core/Resources/ReferenceData/codexRef.json)
and [live provider names](https://spansh.co.uk/api/bodies/field_values/landmarks).
Absence from this catalog is not proof that the object does not exist in game.
The explicit `Chryseum Void Heart` subtype probe returned zero, without
returning unrelated Void objects.
[Recorded absent-subtype query](https://spansh.co.uk/api/bodies/search/recall/B36F5EF2-CB0E-11F1-BA03-981FB0A1E4B4).

## Canonn fallback and parent-name semantics

`GET /query/nearest/codex?x=0&y=0&z=0&name=Bell%20Mollusc&limit=20`
returned 20 records: Luteolum at 9,021.47 ly, then Albens at 9,030.23 ly.
The exact Albens request also returned 20. `Green Water Giant` returned one
system at 30,140.14 ly. These observations confirm distant and parent-substring
queries work without a radius parameter.
[Bell live endpoint](https://us-central1-canonn-api-236217.cloudfunctions.net/query/nearest/codex?x=0&y=0&z=0&name=Bell%20Mollusc&limit=20),
[green-giant endpoint](https://us-central1-canonn-api-236217.cloudfunctions.net/query/nearest/codex?x=0&y=0&z=0&name=Green%20Water%20Giant&limit=20).

Published server code selects reference IDs using SQL `LIKE` around the name,
excludes `hud_category: None`, defaults/caps `limit` at 20, and selects up to
100 matching records on either side of the reference z-order before distance
sorting. It accepts substrings, not a category or regular-expression contract;
SQL wildcard characters are not escaped. The response includes English name
and entry ID. Exact entry queries should validate returned IDs/names when
available. Its query can return several entries for one system, so 20 provider
rows need not mean 20 distinct systems.
[Canonn source](https://github.com/canonn-science/Canonn-GCloud/blob/ea197d5f647dc3722c09eef09f113c8770427872/query/function/localpackage/challenge.py#L728-L806).

Against the local reference, category-name substrings match zero Biology,
Geology and Tourist entries. `Cloud` matches only 12 of 133 Cloud entries.
`Anomaly` happens to match all 53; `Guardian` all six. Subclass labels such as
Aleoids, Bacterial, Conchas, Fonticulus, Fumerolas, Fungoids, Shrubs and Tussocks
also differ from their entry names. Even `K-Type Anomaly` does not occur in
`K01-Type Anomaly`. These are local lexical checks, not new provider mappings.
Use descendant entry predicates for general parent actions. A Bell substring
is safe for the five current Bell descendants, but this special case does not
generalize to arbitrary category labels.

## Original SrvSurvey clues

Upstream was inspected at commit `1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc`.
Its Bingo constructs the same special mollusc grouping, shows generic
“Find nearest” for entry leaves, and sends that entry's complete name to
Canonn. A separate Spansh action gathers missing Odyssey variant objects.
[Original Bingo](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/forms/FormCodexBingo.cs#L140-L204),
[nearest actions](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/forms/FormCodexBingo.cs#L438-L463).

The original nearest window displays only five Canonn results. Its Spansh
path assumes all input variants share one genus and species. The builder
requests ten body rows, sorts by distance from coordinates, and leaves its
distance filter commented out. Those assumptions explain why reusing an
Odyssey missing-color action for the Bell parent is insufficient; upstream
does not supply a generic mapping for all Codex categories.
[Original nearest window](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/forms/FormNearestSystems.cs#L17-L152),
[original Spansh builder](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/net/spansh.cs#L1113-L1137).

## Implementation verification

The replacement client was also exercised against live Spansh using the user's
reference coordinates `(-220.3125, -197.28125, -260.125)`. It returned:

| Selection | Unique systems | Nearest distance |
| --- | ---: | ---: |
| Bell Mollusc group | 20 | 9,310.90 ly |
| Albens Bell Mollusc | 20 | 9,319.70 ly |
| Stratum Tectonicas, any color | 20 | 55.15 ly |
| Green Water Giant | 1 | 30,401.24 ly |

Recorded searches: [Bell group](https://spansh.co.uk/api/bodies/search/recall/F5F5D686-CB11-11F1-830B-97DE7CF01972),
[Albens](https://spansh.co.uk/api/bodies/search/recall/F6D6D190-CB11-11F1-9675-F96389B469DD),
[Stratum](https://spansh.co.uk/api/bodies/search/recall/F7EF314E-CB11-11F1-B15E-F99054CB2305),
[green giant](https://spansh.co.uk/api/bodies/search/recall/F86AD736-CB11-11F1-B15E-F99054CB2305).
These are indexed community reports, not a completeness guarantee for in-game
locations. No reported match is broadened to an unrelated body class.

The embedded `spansh-codex-landmarks.json` is the verified first-party value
snapshot described above. New names remain editable in the advanced Spansh
fields, and unmatched typed Codex names can still use Canonn. Known entries in
HUD category `None` expose the unsupported-index limitation instead of claiming
that there are no locations anywhere in the galaxy.
