# Surface Hunt geology filters against Spansh

Audited 23 September 2026. This checks whether SrvSurvey's Surface Hunt clues can be translated faithfully into **Spansh body search fields**. It does not prove the game's deposit-generation rules: neither the [Frontier journal manual](https://hosting.zaonce.net/community/journal/v34/Journal_Manual_v34.pdf) nor the [Spansh API schema](https://docs.spansh.co.uk/) publishes a commodity-to-volcanism spawn table. The material associations below come from SrvSurvey's [`SurfaceMiningCommodityCatalog`](../../src/SrvSurvey.Core/Mining/SurfaceMiningCommodityCatalog.cs), so they remain prospecting hypotheses until checked against in-game deposits.

## Key distinction

Frontier's `Scan` event records a body's `Volcanism` separately from `Parents`, `PlanetClass`, and `ReserveLevel`; its `FSSBodySignals` event only counts geological signals. The manual lists `ReserveLevel` **if rings are present**. [Frontier journal manual, §§6.3–6.5 and 15.3–15.5](https://hosting.zaonce.net/community/journal/v34/Journal_Manual_v34.pdf). Spansh likewise exposes `volcanism_type` and `landmarks[].subtype` as separate body fields. A recorded `Iron Magma Lava Spout` is a site/landmark, not the body's `Metallic Magma` volcanism; requiring a landmark drops otherwise plausible bodies whose sites are not recorded. [Spansh API schema](https://docs.spansh.co.uk/).

The current Spansh `volcanism_type` enum offers these relevant families (all spellings exactly as in the schema):

| Frontier / Surface Hunt phrase | Spansh body `volcanism_type` values | Confidence |
|---|---|---|
| Iron/metallic magma | `Metallic Magma`, `Minor Metallic Magma`, `Major Metallic Magma` | High for API mapping. `Iron Magma` is **not** a Spansh volcanism value. |
| Silicate magma | `Rocky Magma`, `Minor Rocky Magma`, `Major Rocky Magma` | Medium. Spansh omits `Silicate Magma` as a volcanism value; a live `Rocky Magma` body near Colonia has a `Silicate Magma Lava Spout`, supporting this practical mapping but not proving it universally. |
| Silicate-vapour geysers | `Silicate Vapour Geysers`, `Minor Silicate Vapour Geysers`, `Major Silicate Vapour Geysers` | High. |

Source: [Spansh API schema](https://docs.spansh.co.uk/), compared with [Frontier's volcanism classes](https://hosting.zaonce.net/community/journal/v34/Journal_Manual_v34.pdf). A read-only live `POST /api/bodies/search` on 23 September 2026 returned `Eol Prou LW-L c8-127 B 2` with `volcanism_type: "Rocky Magma"`, `reserve_level: null`, and a `Silicate Magma Lava Spout` landmark. That observation supports the silicate/rocky mapping but shows why a site subtype is too narrow as a required body filter.

## Per-material audit

Use `is_landable = true` and the listed body subtypes for every row. `M` below means all three **Metallic Magma** values above, `R` means all three **Rocky Magma** values, and `V` means all three **Silicate Vapour Geysers** values. Do not require a landmark subtype for the magma or vapour rule. Body types and geology phrases in this table are SrvSurvey catalog entries, not independently verified deposit rules. [SrvSurvey Surface Hunt catalog](../../src/SrvSurvey.Core/Mining/SurfaceMiningCommodityCatalog.cs); [Spansh API schema](https://docs.spansh.co.uk/).

| Material | Catalog body types | Catalog geology | Recommended Spansh geology filter | Qualification |
|---|---|---|---|---|
| Diamond | Metal-rich, High metal content, Rocky, Rocky ice | Silicate or iron magma | `R ∪ M` | Separate commodity from Low Temperature Diamonds. |
| Sapphire | Metal-rich, High metal content, Rocky | Iron magma | `M` | Do not require iron lava-spout landmark. |
| Ruby | Metal-rich, High metal content, Rocky | Iron magma | `M` | Same. |
| Osmium | High metal content, Metal-rich | Iron magma | `M` | Same. |
| Monazite | Rocky | Silicate or iron magma | `R ∪ M` | Previously a silicate/iron lava-spout OR; use body volcanism. |
| Alexandrite | Rocky | Silicate or iron magma | `R ∪ M` | Same. |
| Periclase Dunite | Rocky | Iron magma | `M` **and a white-dwarf host in its ancestor chain** | User confirmed secondary white dwarf also counts. Check the body's actual star ancestor, not just the system's primary star. |
| Serendibite | Rocky | Silicate or iron magma | `R ∪ M` | Same magma mapping. |
| Bastnasite | Rocky | Silicate or iron magma | `R ∪ M` | Same magma mapping. |
| Quartz Pyroxenite | Rocky, Rocky ice | Iron magma | `M` | Do not require iron lava-spout landmark. |
| Jadeite | Rocky | Silicate-vapour geysers | `V` | `Silicate Vapour Gas Vent` is a site subtype, not the body rule. |
| Olivine | Rocky, Rocky ice | Silicate or iron magma | `R ∪ M` | Same magma mapping. |
| Helium | High metal content, Metal-rich, Rocky, Rocky ice | Multiple geyser types | Body subtypes only | Geology is not applied to this search. |

The remaining Surface Hunt materials have `None required` for geology in the catalog; leave their volcanism unfiltered. Mixing such a material with a geology-dependent one must also omit the request-level volcanism filter, then assess each material against each body locally. Where *all* selected materials require geology, OR their allowed Spansh values. This preserves the catalog's OR semantics rather than accidentally requiring several unrelated volcanism types on one body. [SrvSurvey catalog](../../src/SrvSurvey.Core/Mining/SurfaceMiningCommodityCatalog.cs); [SrvSurvey filter plan](../../src/SrvSurvey.Core/Search/PlanetaryMiningPlan.cs).

## Special clues and parent stars

The catalog says **Periclase Dunite** and **Helium-3** favour a white dwarf. Frontier enumerates white-dwarf stellar classes `D`, `DA`, `DAB`, `DAO`, `DAZ`, `DAV`, `DB`, `DBZ`, `DBV`, `DO`, `DOV`, `DQ`, `DC`, `DCV`, and `DX`. A planet or moon around a *secondary* member of one of those classes qualifies under the user's rule. Frontier's `Parents` chain can identify the star ancestor; a moon may first point to a planet, so traverse parent bodies to the star. [Frontier journal manual, §§6.3 and 15.2](https://hosting.zaonce.net/community/journal/v34/Journal_Manual_v34.pdf). Spansh live body results include typed parents with star `subtype` and `id64`; fetch unresolved parent bodies by `id64` when needed. [Spansh API schema](https://docs.spansh.co.uk/). The catalog's phrase **“White-dwarf primary”** should be revised to “white-dwarf host star” so a secondary-star planet is not misdescribed.

Other catalog clues are **trace iron** for Grandidierite, **trace yttrium** for Thortveitite, and “do not expect alongside Platinum” for Rhodplumsite. Frontier's `Scan.Materials` is a list of elemental raw materials and their occurrence percentages, not a record of the newer mineable commodities; a body's `Composition.Metal` is likewise bulk composition. These clues should remain advisory until in-game deposit observations validate a threshold or exclusion rule. [Frontier journal manual, §6.3](https://hosting.zaonce.net/community/journal/v34/Journal_Manual_v34.pdf); [SrvSurvey catalog](../../src/SrvSurvey.Core/Mining/SurfaceMiningCommodityCatalog.cs).

## Known data limits and verification

* Surface Search now omits the reserve filter: Frontier documents reserve on the `Scan` event **when rings are present**, and the sampled live landable `Rocky Magma` body above has `reserve_level: null`. A named reserve would therefore hide plausible surface targets. Powerplay ring searches still use reserve; its Planetary Mining path ignores it. [Frontier journal manual, §6.3](https://hosting.zaonce.net/community/journal/v34/Journal_Manual_v34.pdf); [Spansh API schema](https://docs.spansh.co.uk/).
* The recommended mapping is a **candidate filter**, not deposit confirmation. Validate with a small set of FSS/system-map observations and actual surface deposits for each material before presenting a result as a guaranteed source. No public first-party commodity spawn matrix was located in the sources reviewed.
