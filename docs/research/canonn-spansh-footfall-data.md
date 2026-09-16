# Canonn and Spansh first-footfall data

Date checked: 2026-09-15

Current SrvSurvey revision: `3cc126a1d04767888cd29ad1635d9199949acec1`

## Conclusion

Neither public response currently used by SrvSurvey supplies an explicit first-footfall, prior-footfall, completed-organic-sale, or biology-bonus-claimed field.

- Canonn `getSystemPoi` supplies known Codex/organic observations and a `scanned` flag. In the provider source, `scanned` only means that the commander named in the request appears among the reports; it does not mean global first footfall, completed genetic analysis, or sold organic data.
- Spansh `/dump/{id64}` supplies body signal counts and genus names under `signals`. Its separate `/body/{id64}` response can supply exact biological landmarks and estimated values, but neither documented schema has a footfall, organic-sale, or bonus-claimed property.
- Canonn separately ingests `SellOrganicData` values and bonuses into an internal `organic_sales` table, but `getSystemPoi` does not query or return those rows. SrvSurvey therefore cannot use that internal data through its current Canonn endpoint.

Consequently, treating externally confirmed biology as base/minimum reward would be a conservative application rule, not a statement that Canonn or Spansh explicitly confirmed that the discovery bonus was already claimed. That rule is strongest for an exact Canonn organism observation (or an exact Spansh landmark if SrvSurvey later adopts the body endpoint). A Spansh genus observation confirms only the genus reported by a body-signal scan and does not prove that any particular species was sampled or sold.

## Provider evidence

### Canonn `getSystemPoi`

Canonn documents `getSystemPoi` as returning POIs captured from `CodexEntry`, `SAASignalFound`, and `FSSSignalDiscovered`, plus commander POIs ([official endpoint documentation](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/query/README.md#getsystempoi)). Its current implementation also queries `organic_scans` and returns them in `ScanOrganic`; it combines Codex reports and organic scans for the `codex` result ([`getSystemPoi`, lines 507-530](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/query/function/localpackage/poidata.py#L507-L530)).

The public biology rows contain these fields:

- `body`
- `latitude`
- `longitude`
- `entryid`
- `english_name`
- `hud_category`
- `index_id`
- `scanned`

The source computes `scanned` by comparing stored `cmdr`/`cmdrName` to the commander query parameter ([organic-scan query, lines 44-74](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/query/function/localpackage/poidata.py#L44-L74); [Codex result construction, lines 155-200](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/query/function/localpackage/poidata.py#L155-L200)). It is therefore commander-specific scan history, not a global footfall or sale marker.

Canonn's ingestion code stores `ScanOrganic` only when `ScanType` is `Log` or `Sample`, so the existence of an organic-scan row does not itself prove completion of the three-sample analysis ([source, lines 803-808](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/postEvent/function/main.py#L803-L808)).

Canonn does ingest the `Value` and `Bonus` members of `SellOrganicData` and writes them to `organic_sales` ([sale extraction, lines 1173-1212](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/postEvent/function/main.py#L1173-L1212); [database insert, lines 1412-1446](https://github.com/canonn-science/Canonn-GCloud/blob/1a515d40c65808a273ba7b43b7c8de4bda6caea8/postEvent/function/main.py#L1412-L1446)). The official Frontier journal manual likewise defines `Value` and `Bonus` on each `SellOrganicData.BioData` item ([Frontier Journal Manual v37, section 12.24](https://hosting.zaonce.net/community/journal/v37/Journal_Manual_v37.pdf)). No public Canonn query of `organic_sales` was found in the provider's query source, and `getSystemPoi` does not expose these fields. The journal sale item also carries no originating system or body; Canonn's ingestion attaches the game state's current system/body at the sale location. Those stored rows therefore could not reliably prove bonus state for the originating body without additional provenance.

A live public request with an empty commander parameter was also checked on 2026-09-15: [`getSystemPoi` for 36 Ophiuchi](https://us-central1-canonn-api-236217.cloudfunctions.net/query/getSystemPoi?system=36%20Ophiuchi&cmdr=&odyssey=Y). Its `codex` and `ScanOrganic` rows had the eight fields listed above and no footfall, sale, reward, or bonus field. This is a current payload observation, not a guarantee that the undocumented response can never change.

### Spansh system dump

Spansh's official OpenAPI documentation defines body `signals` with exactly:

- `signals`: signal-name/count map
- `updateTime`: timestamp
- `genuses`: array of genus identifiers

The object has `additionalProperties: false`; no footfall, organic-sale, species, reward, or bonus property is documented ([Spansh API 2.3.2 documentation, `GET /dump/{id64}`](https://docs.spansh.co.uk/)).

A live public response was checked on 2026-09-15: [`/api/dump/10477373803` for Sol](https://spansh.co.uk/api/dump/10477373803). Across the response, body `signals` objects contained only `signals`, `updateTime`, and `genuses`, matching the OpenAPI schema. This is a current payload observation, not proof about data Spansh may hold internally.

### Spansh body record

Spansh's separate documented `GET /body/{id64}` schema is richer. It can return `genuses`, `landmark_value`, and `landmarks`. Each landmark can contain `id`, `type`, `subtype`, `variant`, `latitude`, `longitude`, and `value` ([Spansh API 2.3.2 documentation](https://docs.spansh.co.uk/)). That can provide exact known biology and an estimated base value, but the schema still has no first-footfall, first-discovery, sale, or bonus-claimed field.

A live public body response was checked on 2026-09-15: [`/api/body/792639665784951522` for Yoru 12 b](https://spansh.co.uk/api/body/792639665784951522). Its biological landmarks matched the documented fields and contained no footfall or sale/bonus status.

## Current SrvSurvey parsing

Canonn parsing reads the `codex` array and retains `body`, display name, `entryid`, coordinates, and `scanned` as `IsCommanderScan`; it does not read a footfall or sale field (`src/SrvSurvey.Core/Exobiology/CanonnSystemPoiClient.cs:70-126`). It also does not parse the top-level `ScanOrganic` array separately.

Spansh parsing calls only `/dump/{systemAddress}` and reads biological signal count and `signals.genuses` (`src/SrvSurvey.Core/Exploration/SystemBodyDataClient.cs:86-96,247-305,321-375`). It does not call `/body/{bodyId64}` and therefore does not currently consume Spansh landmarks or their values. The constructed external body snapshot sets `WasFootfalled` to `null` and `IsFirstFootfall` to `false`, so Spansh enrichment cannot currently change the first-footfall reward state.

## Implementation implication

Keep these concepts separate:

1. **Confirmed organism identity**: an exact Canonn `entryid`/name can justify a solid, non-predicted organism PIP.
2. **Confirmed genus only in the current client**: a Spansh `/dump` `signals.genuses` entry can justify a solid genus-level PIP, but the reward can remain a range when the species is unknown. The richer `/body` landmarks are not currently requested.
3. **Confirmed bonus unavailable**: neither current public endpoint proves a sale or claimed biology bonus. Showing base/minimum reward for externally known biology is a deliberate conservative policy.
4. **Potential future proof**: a provider field derived from `SellOrganicData` would be materially stronger than scan presence, but current sale rows lack reliable origin-body provenance. A future provider contract would need to match the sale to the originating system, body, and organism before SrvSurvey could suppress the bonus as provider-confirmed fact.
