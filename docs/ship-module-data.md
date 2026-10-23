# EDSY module data and current-ship export

The snapshot also includes EDSY's FSD fuel coefficients, optimal mass, maximum
fuel per jump, and Guardian FSD booster range bonus. [Spansh route searches](spansh-route-search.md)
combine those facts with actual Loadout mass, fuel capacity, and engineering
modifiers to submit the equipped ship to Galaxy Plotter.

SrvSurvey embeds a factual snapshot of EDSY revision
[`ea787cf8`](https://github.com/taleden/EDSY/tree/ea787cf805f2c38f72c8d5f168fa1063341ebd9a),
whose module database was updated on 2026-10-20. It contains 1,202 module
records, including ship-specific armour, 49 ships with their slot layouts,
and 25 decoded MercGear presets. Frontier symbols are retained verbatim.
Mass and power draw are null where the source does not establish a finite
value; unknown blueprint symbols remain null. This is a catalogue and fitting
reference, not a replacement for EDSY's statistical simulator.

`ShipModuleCatalog` provides case-insensitive symbol lookup and checks slot
type, size, exact-size equipment, ship restrictions, mining/cargo/limpet
slots, and the Type-11 vessel slot. Large planetary vehicle hangars are
restricted to fighter-capable ships; Mk II Large variants are restricted to
the Panther, Caspian, Type-11 and Anaconda Mk II. Ordinary planetary vehicle hangars cannot
use the Type-11 vessel slot. The snapshot includes ordinary, Mk II and
free vessel hangar symbols exactly as EDSY records them. Free Large planetary
vehicle hangars are included under their verified upstream symbols.

The Anaconda Mk II has its own ship/armour identities and slot layout, including
its dedicated size-6 cargo slot. The size-6 Mk II cargo rack and supported hangars
are included. The Mining Volley Repeater fits only its lower large hardpoints
(`LargeHardpoint3` / `LargeHardpoint4`). The pinned refresh also includes the
current Asp Scout, Type-6, Type-7, Federal Dropship and Imperial Clipper layouts
and updated MercGear blueprint identities. See the [Anaconda Mk II source
verification](research/anaconda-mkii-reference-data-2026-10-20.md).

## Refresh

Run `node tools/Import-EdsyModuleData.mjs` to fetch and extract the pinned
source. An optional directory argument reads an existing checkout instead.
The importer records source hashes and decodes MercGear identities with
EDSY's headless API. Node is only needed for refreshing the snapshot; the
application performs no network lookup and adds no runtime package.

When updating the pinned revision, inspect upstream fitting changes alongside
the data and update focused tests. Preserve the provenance and notices.
The [import-contract research](research/edsy-slef-module-import-contract.md)
records upstream limits and primary-source references.

## Firegroups

The existing curated module list already covers every ordinary hardpoint and
active utility in this EDSY revision. The EDSY catalogue supplements that list
and identifies MercGear weapons by their actual engineering blueprint when
localized names are absent. Source-confirmed localized names still work.
New catalogue entries offered through this path are limited to hardpoints and
active utilities; passive internal equipment and hangars are not offered.
Existing scanner, interdictor and limpet-controller actions remain supported.
Saved configurations retain their module/slot identities.

## Current-ship SLEF

Overview offers **Copy ship for EDSY** for the currently piloted journal ship.
Paste the copied JSON into EDSY's Import dialog. This works independently of
Frontier account authentication and manual CAPI commander selection.

`JournalSessionState.ShipLoadout` retains a clone of the complete journal
Loadout, including all internals, slots, module symbols, on/off settings,
zero-based priorities, engineering, original modifier values and unknown
extension fields. The SLEF export wraps that data in an array containing a
`header` with `appName`, `appVersion` and `appURL`, followed by `data`.
The firegroups projection is not used as the export source.

Export is disabled while commander identity is pending, outside the main
ship, in multicrew, at the main menu, or after shutdown. Ship switching and
partial outfitting changes invalidate the snapshot until a new complete
Loadout arrives. Complete engineering events update the matching module's
recorded engineering and modifiers; preview conversions leave it intact.
An incomplete engineering/effect event disables export until a full Loadout.
Matching ship-renaming events update its name and
identifier. Unknown ships are retained but cannot be exported through the
EDSY action until the reference catalogue supports them. No modules or
engineering modifiers are synthesized from incomplete upstream blueprints.

## Validation

The pre-commit hook runs
`pwsh ./tools/Test-ChangedCodeQuality.ps1 -BaseRef origin/SrvSurvey-Avalonia`
to verify CSharpier, seven-language localization freshness, changed-line
Sonar/style rules, the Core and Desktop test suites, mandatory journal-upload
scenarios, and localization tests. Changed production code must meet the
80% line/branch coverage gate. The standard Release solution build also treats
warnings as errors. Run-specific counts and results belong in the PR evidence
and CI reports so these instructions remain current as the suites grow.

Two JSON exports produced by the C# `ShipLoadout.ExportSlef` implementation
were imported and re-exported with the pinned upstream EDSY headless API,
with zero warnings: a Sidewinder (nine occupied modules) and a Type-11
(12 occupied modules) carrying a Mk II Large Planetary Vehicle Hangar,
a Long Range Mining Laser and a Regenerative Burst Laser. The latter's
unknown upstream blueprint name did not prevent its seven recorded
engineering modifiers from surviving import. Snapshot regeneration from
the verified source is deterministic. Live Elite gameplay was not exercised.
