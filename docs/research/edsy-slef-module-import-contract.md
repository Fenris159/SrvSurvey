# EDSY module data and SLEF import contract

Checked: 2026-10-13, America/Chicago. Upstream `HEAD` was rechecked with
`git ls-remote` and remains `e85bb52c0de9886075ecd08ac4271003e3dc8668`,
2026-09-23 16:45:09 UTC. All upstream links below are pinned to that revision.
This investigation changes only this research note. Temporary extraction and
verification tools live under `/tmp`.

## Recommended implementation

Preserve the piloted ship's complete Journal `Loadout` JSON independently of
the filtered Firegroups model. Export that JSON as `data` in a one-entry SLEF
array. Use a checked-in factual module/ship snapshot for names and compatibility,
and preserve unrecognized symbols and engineering unchanged in the export.
This avoids losing fitted core/inactive modules, per-slot power settings,
MercGear identity, and actual engineering rolls.

The existing `FiregroupShip` retains only type/id/name plus filtered
slot/symbol/name records. It excludes many module groups and has no engineering
or power metadata; it cannot reconstruct an accurate outfitting export.
[Current local model](../../src/SrvSurvey.Core/Firegroups/FiregroupConfiguration.cs).
The current `FrontierShipModuleSnapshot` keeps blueprint/level/effect descriptions
but not all Journal engineering modifiers or quality, so it is also insufficient
for a lossless export on its own.
[Current local CAPI model](../../src/SrvSurvey.Core/Frontier/FrontierAccountSnapshot.cs).
`JournalEventEnvelope.Payload` already clones the complete source object.
[Current journal envelope](../../src/SrvSurvey.Core/Journal/JournalEventEnvelope.cs).

## SLEF envelope accepted by EDSY

EDSY's own export is a JSON array of objects with `header` and `data`. Its
importer identifies SLEF when the array is nonempty, the first entry's
`header.appName` is truthy, and `data.Ship` is truthy. Each entry then passes
directly to `Build.fromJournal(data, errors, header)`.
[Export](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3259-L3272),
[import detection](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L6357-L6364).

Recommended emitted structure:

```json
[
  {
    "header": {
      "appName": "SrvSurvey",
      "appVersion": "the running application version",
      "appURL": "https://github.com/Fenris159/SrvSurvey"
    },
    "data": {
      "event": "Loadout",
      "Ship": "original Journal ship symbol",
      "ShipID": 123,
      "ShipName": "original ship name",
      "ShipIdent": "original ship identifier",
      "Modules": []
    }
  }
]
```

This is a shape example, not a usable fitted ship: retain the original
`Modules` array and all original `Loadout` fields in the real file. EDSY's own
export includes hull/modules value, unladen mass, cargo capacity, jump range,
main/reserve fuel capacity and rebuy. These do not need to be fabricated when
absent; copying the Journal event preserves the game's observations.
[EDSY's exported Loadout](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3205-L3255).

EDSY reads ship symbols case-insensitively. `ShipName` and `ShipIdent` populate
the fitted ship's displayed name/identifier. `ShipID` does not participate in
EDSY's SLEF import identity: its identity combines app name, EDSY ship type,
display name and identifier. Retain the numeric game ID for fidelity and use
commander plus ship symbol/ID locally when choosing the current ship.
[Ship/name import](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3674-L3695),
[SLEF import identity](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L6359-L6364).

## Module and engineering preservation

Retain each original module object, especially these fields:

| Field | Meaning in EDSY |
| --- | --- |
| `Slot` | Game slot name; read case-insensitively, including irregular ship-specific slots |
| `Item` | Actual Frontier module symbol; looked up case-insensitively against common and ship-specific records |
| `On` | Powered flag |
| `Priority` | Journal priority is zero-based; EDSY internally adds one |
| `Value`, `Health` | Used for cost/discount handling; retain the game values unchanged |
| `Engineering.BlueprintName` | Frontier blueprint name, mapped per module type |
| `Engineering.Level` | Blueprint grade |
| `Engineering.Quality` | Roll/progress value, if available |
| `Engineering.ExperimentalEffect` | Frontier effect symbol, looked up case-insensitively |
| `Engineering.Modifiers[]` | Actual effective values override the blueprint baseline |
| Modifier `Label`, `Value`, `OriginalValue` | Frontier attribute label, resulting absolute value, original absolute value |

[Module lookup and power settings](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3801-L3944),
[exported fields](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L2058-L2112).

`Modifiers.Value` is an absolute effective value, not a percentage to invent
from the blueprint. If `OriginalValue` is absent, EDSY falls back to its own
base statistic. It imports Damage/DPS/rate-of-fire carefully and may clear
unreported baseline modifications for special pre-engineered modules.
Preserving the supplied modifiers also preserves usable stats when EDSY lacks
a blueprint-name mapping.
[Modifier application](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3851-L3926).

EDSY does not read `Item_Localised` when choosing the module or engineering.
Keep it for local display, but do not use it instead of `Item` in SLEF and do
not translate an engineering identity into a guessed new module symbol.
MercGear builtin entries are normal base modules with particular blueprint,
experimental and modifier values; EDSY recognizes matching builtin hashes
after importing those values.
[Builtin presets](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L50-L138),
[builtin matching](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3929-L3933).

For local identity/display, match verified base symbol plus verified blueprint
and effect metadata. Raw Journal modifiers remain authoritative for exported
stats. Known localized labels can be a display fallback, not a manufactured
Frontier blueprint or symbol alias. Unknown identities remain unknown.

## Exact MercGear blueprint names and gaps

These are the checked EDSY `blueprint` entries; a null value is an upstream gap
and must remain null in extracted metadata.

| EDSY key | Display name | Frontier `fdname` |
| --- | --- | --- |
| `wpn_dscr` | Double Screaming | `Weapon_DoubleScreaming` |
| `wpn_drsk` | Drag Seeker | `Weapon_DragSeeker` |
| `wpn_ef` | Enduring Feedback | `Weapon_EnduringFeedback` |
| `wpn_exp` | Exposing Seeker | null |
| `wpn_fi` | Force Impact | null |
| `wpn_fr` | Far-Reaching | `Weapon_FarReaching` |
| `wpn_hy` | High-Yield | `Weapon_HighYieldEnzyme` |
| `wpn_lwts` | Lightweight Thermal Seeker | `Weapon_LightweightThermalSeeker` |
| `wpn_ld` | Lockdown | `Weapon_LockdownSeeker` |
| `wpn_lrml` | Long Range Mining Laser | `Weapon_LongRangeMiningLaser` |
| `wpn_ol` | Overloaded | `Weapon_Overloaded` |
| `wpn_pc` | Plasma Conversion | `Weapon_PlasmaConversion` |
| `wpn_rp` | Rapid Phase | `Weapon_RapidPhase` |
| `wpn_reg` | Regenerative | null |
| `cpd_b` | Balanced | `PowerDistributor_Balanced` |
| `cpd_uf` | Support Focused | `PowerDistributor_SupportFocused` |
| `icr_xcr` | Extended Cargo Rack | `CargoRack_Extended` |
| `ifs_sre` | Scoop Rate Enhanced | `FuelScoop_ScoopRateEnhanced` |
| `imrp_hd` | Heavy Duty | `MRP_HeavyDuty` |
| `iss_lrd` | Long Range | `Sensor_LongRangeDSS` |

[Weapon blueprint data](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L1953-L1974),
[distributors/internals data](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L2013-L2054).

Exposing Seeker and Force Impact also have only Grade 1 attribute arrays, with
grades 2–5 explicitly marked TODO. Regenerative has Grade 1–5 arrays but no
Frontier blueprint name. Do not fill those names from another project's naming
convention. A real Journal blueprint name remains preserved even if EDSY warns
that it is unknown.

Decoded builtin examples confirm Exposing uses
`Hpt_BasicMissileRack_Fixed_Large` plus `special_drag_munitions`; Force Impact
uses `Hpt_Cannon_Fixed_Huge` plus `special_force_shell`; Regenerative uses
`Hpt_PulseLaserBurst_Gimbal_Medium` plus `special_regeneration_sequence`.
Enduring Feedback uses the distinct `special_feedback_cascade_cooled` symbol.
These were decoded with EDSY's own `Slot.setStoredHash` and getters; they are
not inferred from English names.
[Preset hashes](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L50-L93),
[effect symbol data](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L2063-L2087).

## Factual snapshot fields to extract

Pin a source revision and keep its identity/version/date and attribution with
the generated JSON. Recommended fields:

| Source | Extracted factual fields |
| --- | --- |
| `eddb.module` | EDSY ID, `fdname`/`fdid`, `name`, `namekey`, `mtype`, `class`, `rating`, `mount`, `tag`, `hidden`, `reserved`, `noundersize`, `limit`; optionally base mass/power/integrity and vessel counts when actually used |
| `eddb.ship` | EDSY ID, `fdname`/`fdid`, name, slot sizes, exact `slotnames`, per-slot `reserved`, and ship-specific `module` overrides |
| `eddb.group` | Legal module types for hardpoint/utility/core/military/internal groups |
| `eddb.mtype` | Name, supported blueprints/effects, modifiable attribute identifiers |
| `eddb.blueprint` | Internal key, display name, exact nullable `fdname`, `maxgrade`; completeness metadata for partial grade arrays |
| `eddb.expeffect` | Internal key, display name, exact `fdname` |
| EDSY MercGear builtins | Preset ID/display name, decoded base module ID/symbol, blueprint key/name/nullable `fdname`, grade/quality, effect key/name/`fdname` |

Ship-specific modules must be merged over common module records before
extracting armour symbols: the common armour templates have no generic
Frontier symbol or per-ship values. EDSY resolves ship-specific symbol/ID
overrides before common records.
[Source mapping](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L6731-L6760),
[Type-11 armour overrides](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L1326-L1331).

The source is a JavaScript object, not JSON: it contains comments, unquoted
keys, `NaN`, and some expressions. Preserve absent/unknown numeric values as
unknown, not zero. Hidden free modules with actual symbols must remain in the
lookup catalogue even when excluded from ordinary UI choices.
[Database source](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js).

## Exact hangar symbols and fitting

The checked catalogue contains:

| Module family | Exact Frontier symbol pattern | Sizes |
| --- | --- | --- |
| Vessel Hangar | `Int_FighterBay_SizeN_Class1` | 5, 6, 7 |
| Free Vessel Hangar | `Int_FighterBay_SizeN_Class1_Free` | 5, 6, 7 |
| Mk II Vessel Hangar | `Int_FighterBayMk2_SizeN_Class1` | 5, 6, 7 |
| Free Mk II Vessel Hangar | `Int_FighterBayMk2_SizeN_Class1_Free` | 5, 6, 7 |
| Ordinary PVH, H/G | `Int_BuggyBay_SizeN_Class1` / `Int_BuggyBay_SizeN_Class2` | 2, 4, 6 |
| Large PVH | `Int_LargeBuggyBay_SizeN_Class3` | 2, 4, 6 |
| Mk II Large PVH | `Int_MkIILargeBuggyBay_SizeN_Class3` | 2, 4, 6 |

Here `N` represents only the enumerated sizes, not arbitrary sizes. EDSY has
no free Large/Mk II Large PVH records at this revision; do not create those
aliases as though they came from EDSY. Keep observed unknown Journal symbols
in exports unchanged.
[Vessel records](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3460-L3471),
[vehicle records](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3724-L3735).

Compatibility combines legal group/type, module size, ship restrictions,
per-slot restrictions, and specific slot rules. Ordinary PVHs have no ship
restriction; Large PVHs carry the fighter-capable ship list. Mk II vessel and
Mk II Large PVHs carry only ship IDs 63/38/47, whose exact symbols are
`PantherMkII`, `LakonMiner` and `Explorer_NX`.
Type-11's `FighterBay01` permits vessel hangars and Large/Mk II Large PVHs but
rejects ordinary PVHs. Its `LimpetController01` and mining hardpoint names must
also be preserved verbatim. EDSY groups both `FIGHTERBAY` and `LIMPETCONTROLLER`
as internal slots; no invented `VesselBay01` alias is needed.
[Compatibility](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L996-L1027),
[Type-11 slots](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L1298-L1318),
[import slot groups](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L6700-L6728).

The importer recognizes `CargoHatch` separately and currently ignores
`PlanetaryApproachSuite`. The exported SrvSurvey file should still preserve
both source records and let the receiving tool apply its own behavior.
[Special slot handling](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L3739-L3758).

## Source licensing and attribution

EDSY's file headers identify taleden's design/markup/scripts as
Creative Commons Attribution-NonCommercial 4.0, while the game logic/data
remain Frontier property used with its permission. These are separate source
statements. SrvSurvey has a GPLv3 license. Do not re-label copied EDSY code as
GPLv3; retain the source and terms when extracting factual reference data and
write SrvSurvey's own implementation of the documented contract.
[EDSY header](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L1-L10),
[EDSY script header](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L1-L10),
[SrvSurvey license](../../LICENSE).
This note records those statements; it does not assert a broader permission
to redistribute EDSY implementation or resolve all license compatibility.

## Temporary verification artifacts

EDSY intentionally exposes `Build`, `Slot`, and versions for headless tooling
when `document.title` is not `EDSY`.
[Headless seam](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L12699-L12707).

- `/tmp/edsy-mercgear-presets.json`: 25 decoded MercGear preset records with
  source/revision, exact base symbol and blueprint/effect identity.
- `/tmp/edsy-extract-mercgear-presets.mjs`: extraction script using the pinned
  upstream headless API.
- `/tmp/verify-edsy-slef.mjs`: `node /tmp/verify-edsy-slef.mjs <file.json>` imports
  SLEF or a raw Loadout with EDSY and reports imported slots, symbols,
  blueprint/effect names, modifier counts and upstream warnings. It initializes
  only localization caches missing in headless mode; import/export logic is
  unchanged. Warnings return exit code 1 unless `--allow-warnings` is supplied.
- `/tmp/edsy-slef-smoke.json`: Sidewinder fixture verified to import and re-export
  15 modules with zero warnings. The minimal fixture omits `ModulesValue` to
  avoid an irrelevant upstream estimated-discount warning.

Additional direct headless checks confirmed the Type-11 dedicated slot accepts
Large/Mk II Large PVHs and ordinary/Mk II vessel hangars but rejects ordinary
PVHs. A Sidewinder size-2 slot accepts ordinary PVH and rejects both Large types.
These checks establish the inspected upstream behavior, not every in-game
statistic or every incomplete engineering blueprint.

## Journal snapshot lifecycle follow-up

Frontier's Journal manual v37 documents full `Loadout` events at startup/menu
load, ship changes, after Outfitting changes and SRV docking. `ModuleInfo` is
a marker with no parameters; the accompanying **`ModulesInfo.json`** contains
the module list, `Slot`, `Item`, `Power` and optional `Priority`. Its documented
example contains no `On`. The exact bulk-storage event is **`MassModuleStore`**.
`ModuleSetPower` and `ModuleSetPriority` were not found in the manual or current
EDMC source; do not design Journal tests around invented event names.
[Frontier manual sections 3.4, 8.18 and 13.28](https://hosting.zaonce.net/community/journal/v37/Journal_Manual_v37.pdf).

Current EDMC was checked at `89f410c9c75c16ff564068ab5201ed3d65ba68ce`,
2026-09-02. It reads `ModulesInfo.json` on `ModuleInfo`, stores that separately,
and does not merge its data into fitted module `On`/`Priority`. Its SLEF string
is refreshed by full Loadout processing. Independently, it updates its mutable
module state on several fitting events and applies `EngineerCraft` or actual
`EngineerLegacyConvert` directly to the matching slot/module. Preview legacy
conversion is excluded.
[Sidecar handling](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/monitor.py#L1586-L1595),
[Loadout/SLEF handling](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/monitor.py#L681-L751),
[engineering handling](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/monitor.py#L1668-L1699).

`ModuleBuy` and `ModuleRetrieve` do not supply every engineering, power and
priority field required to reconstruct a full snapshot. EDMC's synthesized
purchase defaults are an implementation convenience, not evidence of those
values in the event. Recommendation: invalidate export readiness after a
fitting mutation until a new authoritative Loadout, or explicitly track an
incomplete snapshot while applying only fields the event actually reports.
Treat a ship switch similarly; do not associate old fitted modules with the
new ship while awaiting its Loadout. A read-only cached previous Loadout may
still be useful if clearly represented as the last complete observation.
[Frontier manual sections 8.25–8.30](https://hosting.zaonce.net/community/journal/v37/Journal_Manual_v37.pdf),
[EDMC fitting mutations](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/monitor.py#L753-L789),
[EDMC ship-switch invalidation](https://github.com/EDCD/EDMarketConnector/blob/89f410c9c75c16ff564068ab5201ed3d65ba68ce/monitor.py#L659-L679).

The v37 manual is an older official specification, supplemented here by current
EDMC source. It does not establish that a full Loadout immediately follows
every EngineerCraft or power-toggle action. `Power` in ModulesInfo is a numeric
power value, not a documented replacement for a Boolean `On` flag.
