# Anaconda Mk II reference data

Checked: 2026-10-20, America/Chicago. Scope: the supplied FDevIDs PR and
SrvSurvey's existing offline ship, module, fitting, and overlay catalogs.

## Verified identities

[FDevIDs PR114](https://github.com/EDCD/FDevIDs/pull/114/commits) was open when
checked. Its single commit is
[`1498b21378955b459c055022014c9d4caa738faf`](https://github.com/EDCD/FDevIDs/commit/1498b21378955b459c055022014c9d4caa738faf).
The added identities are:

| Frontier ID | Journal symbol | Meaning |
| --- | --- | --- |
| 129046025 | `AnacondaMkII` | Anaconda Mk II |
| 129046027 | `AnacondaMkII_Armour_Grade1` | Mk II ablative lightweight |
| 129046028 | `AnacondaMkII_Armour_Grade1_Default` | Ordinary lightweight |
| 129046029 | `AnacondaMkII_Armour_Grade2` | Mk II ablative reinforced |
| 129046030 | `AnacondaMkII_Armour_Grade3` | Mk II ablative military |
| 129046031 | `AnacondaMkII_Armour_Mirrored` | Mk II ablative mirrored |
| 129046032 | `AnacondaMkII_Armour_Reactive` | Mk II ablative reactive |
| 129051593 | `Int_LargeCargoRack_Size6_Class1` | Size 6, D-rated Mk II rack |

The default and ablative lightweight armors are distinct modules; recognizing
one as an alias of the other would discard that distinction.
[Source rows](https://github.com/EDCD/FDevIDs/commit/1498b21378955b459c055022014c9d4caa738faf).

## Reuse the EDSY import

EDSY revision
[`ea787cf805f2c38f72c8d5f168fa1063341ebd9a`](https://github.com/taleden/EDSY/tree/ea787cf805f2c38f72c8d5f168fa1063341ebd9a)
already contains these identities and the ship's fitting data. It identifies
the ship as large, EDSY ID 18. Hardpoints are `[4,3,3,3,3,1,1]`, utilities eight,
core sizes `[1,8,7,8,5,8,8,6]`, military `[5,5]`, and internals
`[8,7,7,6,5,5,5,4,4,3,2,1]`. The size 6 internal is `Cargo01`, restricted to
cargo racks/fuel tanks. The new rack holds 96 t and permits ships 18/63.
[Database](https://github.com/taleden/EDSY/blob/ea787cf805f2c38f72c8d5f168fa1063341ebd9a/eddb.js#L1440-L1477),
[rack introduction](https://github.com/taleden/EDSY/commit/42dfa51e64e9b3d01707a289e4f674cdeac69485).

The existing Mining Volley Repeater is allowed only in this ship's lower
large hardpoints: source indices 3/4, imported as `LargeHardpoint3` and
`LargeHardpoint4`. Ship-wide permission alone is insufficient.
[Fitting rule](https://github.com/taleden/EDSY/blob/ea787cf805f2c38f72c8d5f168fa1063341ebd9a/edsy.js#L1006-L1029).
Vessel and large vehicle hangar restrictions also include the new ship.
[Hangar update](https://github.com/taleden/EDSY/commit/2bfa9cdd20409d8640ec6546b63a9e8703bff849).

The downloaded pinned source hashes are:

- `eddb.js`: `071f3c63bab630b0db33e7d77061c1dcd7c0d06426c507ab52bf87e9a1c99da2`
- `edsy.js`: `4c314ddb773bc9302169b47074c68daafcec8d2584d024e4853d2ca354086da6`

A research-only run of the existing importer against this revision produced
49 ships, 1,202 modules and 25 MercGear presets. Repinning the importer preserves
reproducibility, including new free hangar identities and current fitting
restrictions; hand-editing the generated snapshot would not.

## Local integration and limits

Add the journal symbol/display name to `OverlayVehicleCatalog` as Large and
to `FrontierCapiSnapshotParser`'s ship-name lookup. Refresh the generated module
snapshot, update its documented provenance/counts, and add the repeater's
two-hardpoint restriction to `ShipModuleCatalog` with focused fitting tests.
Existing loadout/SLEF code retains journal module identities; colonization and
mining capacity already use reported `CargoCapacity`, so neither needs a
guessed ship-specific capacity constant.

The inspected sources do not establish the ship's measured cockpit-to-pad
offset for human settlement maps. Retain the unknown-ship fallback until
measured; copying the original Anaconda offset would introduce unsupported
position data. No in-game validation was performed for this research note.
