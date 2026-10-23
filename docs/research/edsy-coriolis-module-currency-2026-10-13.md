# EDSY and Coriolis coverage of MercGear and hangars

Checked: 2026-10-13, America/Chicago. Scope: the two supplied repositories,
their actual module data, active Coriolis deployment branches, and the recent
MercGear and hangar changes. Public repositories were cloned to temporary
directories for source inspection. No application code, commit, push, or
external message was changed or sent.

## Finding

**EDSY is the stronger choice for the requested module catalogue and hangar
fitting rules. Coriolis's active code is slightly newer by date and includes
substantial Operations/MercGear support, so calling Coriolis abandoned or simply
outdated would be wrong.** Its checked active data nevertheless has specific
gaps: an Exposing Seeker record with a missing blueprint and incorrect display
name, no distinct MercGear High-Yield Enzyme module, and no equivalent to
EDSY's recent Large/Mk II vehicle-hangar compatibility checks.

This is a source-based assessment of the requested features, not a blanket
claim that every statistic in EDSY is correct. EDSY itself records incomplete
grades and verification TODOs. Coriolis offers explicit Merc Coin purchase and
engineering-cost calculations which EDSY still marks as TODOs.

## Frontier update context

Frontier's Operations notes describe Merc Coins buying ship modules and
engineering blueprints, including Thermal Plasma Conversion, enhanced fuel
scooping, pre-engineered weapons, and Heavy Duty Module Reinforcement
Packages. They also describe the adjusted Mk I vessel bay for the Nomad.
[Operations update notes](https://www.elitedangerous.com/news/updates/4-4-0-0).

The Rhino update and August developer log establish the subsequent surface
mining and Large Planetary Vehicle Hangar context. The developer log's current
rendered ship list specifies classes 2–6, or 2–4 for the Keelback. These official
pages were fetched successfully with Python's HTTP client after the web tool
returned HTTP 403. They establish the comparison's scope, not independent
validation of every statistic or fitting rule in either simulator.
[Rhino update notes](https://www.elitedangerous.com/news/updates/4-4-1-0),
[August developer log](https://www.elitedangerous.com/news/elite-dangerous-august-dev-log-2026).

## Source snapshots and why branch selection matters

| Source | Checked revision | Commit date | Role |
| --- | --- | --- | --- |
| [taleden/EDSY master](https://github.com/taleden/EDSY/tree/e85bb52c0de9886075ecd08ac4271003e3dc8668) | `e85bb52c0de9886075ecd08ac4271003e3dc8668` | 2026-09-23 16:45:09 UTC | EDSY code and data together |
| [EDCD/coriolis master](https://github.com/EDCD/coriolis/tree/68c042ca6e3db62372cbbb2077cf972345511712) | `68c042ca6e3db62372cbbb2077cf972345511712` | 2026-04-24 08:22:56 UTC | Supplied repository's default branch |
| [EDCD/coriolis develop](https://github.com/EDCD/coriolis/tree/1a2908fa252ee18b181957e363e0164189fb52da) | `1a2908fa252ee18b181957e363e0164189fb52da` | 2026-09-26 09:54:18 UTC | Newer EDCD development mirror |
| [EDCD/coriolis-data master](https://github.com/EDCD/coriolis-data/tree/0db9234b5b9ce8c939ea84133d7ce336eea88e27) | `0db9234b5b9ce8c939ea84133d7ce336eea88e27` | 2026-04-24 06:46:12 UTC | Older default data branch |
| [EDCD/coriolis-data develop](https://github.com/EDCD/coriolis-data/tree/24a218d0f91c11bc25c1d862928ab05f250177ca) | `24a218d0f91c11bc25c1d862928ab05f250177ca` | 2026-09-26 09:54:05 UTC | Newer EDCD data mirror |
| [Brighter-Applications/coriolis live](https://github.com/Brighter-Applications/coriolis/tree/515522f6720388e0b5bd12a53c2bb020c1d04205) | `515522f6720388e0b5bd12a53c2bb020c1d04205` | 2026-09-26 13:10:07 UTC | Actual active deployment source; alpha and beta point at the same revision |
| [Brighter-Applications/coriolis-data live](https://github.com/Brighter-Applications/coriolis-data/tree/a98190000615678150941933fd00225025d78558) | `a98190000615678150941933fd00225025d78558` | 2026-09-25 22:44:04 UTC | Active module data; alpha and beta point at the same revision |

The supplied Coriolis repository explicitly directs contributions and local
development to the Brighter-Applications app and data repositories.
[EDCD README](https://github.com/EDCD/coriolis/blob/68c042ca6e3db62372cbbb2077cf972345511712/README.md#L12-L14),
[local dependency](https://github.com/EDCD/coriolis/blob/68c042ca6e3db62372cbbb2077cf972345511712/package.json#L148).
Its active deployment workflow clones the matching app and data branch from
Brighter-Applications. Consequently, the April default-branch dates cannot be
used to infer the current Coriolis site's module coverage.
[Active workflow](https://github.com/Brighter-Applications/coriolis/blob/515522f6720388e0b5bd12a53c2bb020c1d04205/.github/workflows/autodeploy.yml#L87-L88).

## Recent additions

EDSY added Vessel Hangar naming and Mk II/free variants on August 7,
Large and Mk II Large planetary vehicle hangars on August 28, and the latest
Exposing Seeker, Force Impact Cannon, and Regenerative Burst modules on
September 23. The builtin MercGear catalogue also includes Enduring Feedback,
Far-Reaching, Double Screaming, Long Range Mining Laser, High-Yield Enzyme,
Rapid Phase, seeker variants, power distributors, Extended Cargo Racks, Heavy
Duty MRP, and Long Range DSS.
[Vessel-hangar commit](https://github.com/taleden/EDSY/commit/42423365e79446b9b20f50a0351900278b7bfbf9),
[Large-PVH commit](https://github.com/taleden/EDSY/commit/431b9721dcba7e6a5e0303af6b1ca4b3032d69c2),
[September 23 module commit](https://github.com/taleden/EDSY/commit/2e0c3a93c59694affb708b0c4a94b018d2455344),
[builtin modules](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L50-L138).

Coriolis's active data added more Operations weapons and the Large/Mk II Large
PVH records on September 25, followed by Mk II vessel-hangar ID fixes the same
day. The app added Merc Coin support on September 17 and completed purchase
plus engineering-cost reporting on September 26.
[September 25 data commit](https://github.com/Brighter-Applications/coriolis-data/commit/647070afc1525e8c5ea42af80199ff62d5ea6f10),
[hangar ID fix](https://github.com/Brighter-Applications/coriolis-data/commit/c3f21f430abfddcdf8eef93d09f7913cf1cf1ba3),
[Merc Coin support](https://github.com/Brighter-Applications/coriolis/commit/1b383cc3d78665a533b9b704e5c5cbc4def36dc2),
[Merc Coin cost fix](https://github.com/Brighter-Applications/coriolis/commit/1f9faed42482a500714eed2e3c705511b2864c6c).

Both active catalogues therefore contain ordinary and Mk II vessel hangars,
and Large/Mk II Large vehicle hangars in sizes 2, 4, and 6. Coriolis additionally
has explicit free Large-PVH records. Presence of these records alone does not
establish valid ship and slot compatibility.
[EDSY vessel data](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3460-L3471),
[EDSY vehicle data](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3724-L3735),
[Coriolis Mk II vessel data](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modules/internal/fighter_hangar_mkii.json#L1-L49),
[Coriolis vehicle data](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modules/internal/planetary_vehicle_hanger.json#L99-L277).

## Specific differences supporting the recommendation

### Large and Mk II vehicle-hangar fitting

EDSY's September 23 compatibility change explicitly encodes ordinary PVHs on
any ship except the Type-11 dedicated vessel slot, Large PVHs on fighter-capable
ships including that slot, and Mk II Large PVHs restricted to the Panther,
Caspian, and Type-11. The current module records contain the per-ship
`reserved` lists, and `isModuleIDAllowed` checks both ship restrictions and the
Type-11 slot exception.
[Dated compatibility commit](https://github.com/taleden/EDSY/commit/97e018ac7e1f021c7731382c1e17227b29e45462),
[current fitting checks](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L1016-L1026),
[current PVH restrictions](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L3724-L3735).

Coriolis groups every vehicle hangar, including Large/Mk II Large variants, as
`pv`. Those records have no per-ship restrictions. Its module enumeration and
mount check restrict `fh` and `fhmkii` but have no corresponding `pv` rule.
The Type-11 dedicated slot still permits only `fh`, so it excludes the new
`pv` and `fhmkii` groups from that slot. **Inference from source:** Coriolis can
offer Large/Mk II Large PVHs in ordinary slots on unsupported ships, while
excluding them from the Type-11 dedicated slot. No browser reproduction was
performed, so this is specifically an implementation finding.
[PVH group records](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modules/internal/planetary_vehicle_hanger.json#L99-L232),
[module enumeration](https://github.com/Brighter-Applications/coriolis/blob/515522f6720388e0b5bd12a53c2bb020c1d04205/src/app/shipyard/ModuleSet.js#L118-L143),
[mount checks](https://github.com/Brighter-Applications/coriolis/blob/515522f6720388e0b5bd12a53c2bb020c1d04205/src/app/utils/SlotFunctions.js#L14-L44),
[Type-11 slot](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/ships/type_11_prospector.json#L183-L188).

### Exposing Seeker

EDSY has a correctly named Exposing Seeker builtin and a Grade 1 blueprint;
its blueprint explicitly marks grades 2–5 and the Frontier blueprint name as
TODOs.
[EDSY builtin](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L92),
[EDSY Grade 1 blueprint](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L1958).

Coriolis's new module `8S` has `special: "Exposing Seeker"`, but `name:
"Lockdown Seeker"`, and points at `Weapon_ExposingSeeker`, which is absent from
the checked `blueprints.json`. This is a catalogue/reference inconsistency;
the exact UI failure was not reproduced.
[Module record](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modules/hardpoints/missile_rack.json#L518-L552),
[complete blueprint catalogue checked](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modifications/blueprints.json).

### High-Yield Enzyme module

EDSY includes the distinct MercGear High-Yield Enzyme builtin. Coriolis has a
`Weapon_HighYieldEnzyme` blueprint, but its enzyme module file contains only
the ordinary and older Community Goal variant; no MercCoin High-Yield module
record was found across the active module catalogue.
[EDSY builtin](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/edsy.js#L60-L61),
[Coriolis enzyme module file](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modules/hardpoints/enzyme_missile_rack.json#L1-L72),
[Coriolis blueprint](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modifications/blueprints.json#L10666).

## Limits and confidence

High confidence in the branch dates, module presence/absence, and source
implementation differences: these were inspected at the immutable revisions
above. The same active Coriolis revisions were on `live`, `beta`, and `alpha` at
inspection, so these features are not merely parked in an alpha-only branch.

Neither project should be described as fully complete or universally accurate.
EDSY still has Grade 2–5 TODOs for Exposing Seeker and Force Impact and multiple
verification/Merc Coin TODOs. Coriolis Force Impact grades 2–5 contain zeroed
placeholder effects. Coriolis's purchase-plus-engineering Merc Coin calculation
is a clear feature advantage, but it does not validate the underlying module
statistics.
[EDSY limitations](https://github.com/taleden/EDSY/blob/e85bb52c0de9886075ecd08ac4271003e3dc8668/eddb.js#L1953-L1974),
[Coriolis Force Impact placeholders](https://github.com/Brighter-Applications/coriolis-data/blob/a98190000615678150941933fd00225025d78558/modifications/blueprints.json#L11505),
[Coriolis Merc Coin calculations](https://github.com/Brighter-Applications/coriolis/blob/515522f6720388e0b5bd12a53c2bb020c1d04205/src/app/shipyard/Module.js#L475-L517).

The live [EDSY database](https://edsy.org/eddb.js) was independently fetched and
checked: 565,551 bytes, SHA-256
`de78ba5280ee9f01c0b1b3ade3ca50c70ddc68a73447d0f0eee109fa08add2e8`,
exactly matching `eddb.js` at the checked EDSY revision. Thus the EDSY catalogue
findings also apply to the live database retrieved in this session.

Direct public GETs of `coriolis.io/data/index.json` and the beta/alpha equivalents
returned HTTP 403 in this check. Source branch inspection establishes what is
prepared for those environments; it alone cannot prove the exact bytes served
by every live page or cached client. No build or game-statistic test was run.
