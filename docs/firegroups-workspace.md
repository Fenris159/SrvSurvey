# Firegroups workspace and overlay

## Requested workflow

The Firegroups workspace has a dedicated overlay settings window containing the enabled toggle, its existing shortcut and Overlay Exceptions. Overlay editor placement stays under Status & utilities. A–H selection uses letters and arrow buttons. Primary and secondary assignments support circled add/remove controls and select equipped modules from journal Loadout events. Add group stages the assignments and advances to an unused letter; a tree previews groups and their primary/secondary modules. The name field, Save and Remove share a row. Each saved configuration has a trash button on its right; both deletion controls ask Yes/No before removing it, identify the saved configuration and preserve other drafts. Overlay settings are accessed only from the sidebar icon. Saved configuration names reopen the editor; expandable rows preview the saved contents. The overlay selects a saved configuration for the currently boarded ship and maps Status.json FireGroup 0–7 to A–H.

## Data and editing

Configurations are commander-scoped in the `firegroups` data folder. Journal ship type and ShipID identify a ship; ShipName is retained for display. Loadouts are cached so saved setups remain editable after restart. Named configurations for the same ship have a persisted active selection. Ship renames preserve identity, and two ships of the same type can use different configurations.

The editor uses observable rows and saved collections. Drafts survive switching configurations or ships within the application session. Group arrows retain entered assignments. Save validates name, content and repeated assignments, writes atomically, and publishes the saved row only after success. Errors remain visible below the workspace. Old flat Mining firegroups are imported once for the first identified ship; old Mining files remain intact. Imported or unequipped assignments are retained and marked for replacement, never silently remapped. Ship loadouts and named profiles are stored in the `firegroups` folder and included in the existing Mining backup ZIP. Restore validates the Firegroups document before applying it, retains the previous file for recovery, and immediately refreshes the workspace and overlay. Older ZIPs without Firegroups remain supported and leave current named configurations intact.

## Equipped module catalog

The checked-in catalog is a filtered snapshot of [EDCD FDevIDs outfitting.csv](https://github.com/EDCD/FDevIDs/blob/master/outfitting.csv), retrieved 2026-09-07. It contains hardpoint, utility, Surface Scanner, internal limpet-controller and supported mercgear entries. Shield cell banks, shield boosters, point defence, power distributors, module reinforcement packages and cargo racks are excluded. The game’s equipped module symbols are matched case-insensitively; when the journal supplies a recognized mercgear localized name, that distinct name is retained. D-Scanner, SC-Suite and Data Link Scanner are always offered as built-in ship actions. Only equipped matching modules are otherwise offered for new assignments, and slots distinguish duplicate modules. No runtime network lookup or new package is required. Future unlisted module symbols require a catalog update.

## Visibility and compatibility

Firegroups retains PlotMiningFiregroups, its shortcut identity and saved overlay placement. It now has its own settings category. Previous Global vehicle exceptions are copied once into that category, then saved independently. Migration is recorded even when Global uses its implicit all-allowed default, so later Global edits cannot change Firegroups on restart. The overlay uses the saved profile and the reported active group rather than unsaved editor content. It does not send inputs to Elite. A vessel without an identified Loadout/profile has no active Firegroups overlay. Existing mining notifications keep their own visibility rules.

## Diagnosis and validation

A headless test reproduced the old Save symptom: the handler updated the model, while its plain list failed to refresh the rendered rows. The replacement exercises observable saved rows through the view’s Save command, with visible success feedback and expandable tree contents. Additional tests cover equipped-module filtering, duplicate modules, A–H mapping, multi-module persistence, same-type ship switching, draft retention, missing-module warnings, loadout no-op identity, save failure, commander isolation and legacy migration. Headless Monochrome rendering was inspected. Live gameplay remains a local-build verification step.

## Standards review

Reviewed against starting commit `1816b3eb`. One durability concern was found: moving named configurations into their own store removed them from the existing Mining backup. The ZIP now carries validated Firegroups data, retains pre-restore recovery files and refreshes the live editor. Recheck found no remaining consequential issue.

## Spec review

One requirement defect was found: implicit default exceptions could inherit a later Global edit after restart. Migration now records completion even for the all-allowed default. A regression reproduced the failure before the fix; recheck found no remaining confirmed requirement defect.

Review totals: one standards concern and one spec defect, both addressed; zero remaining findings per axis. Validation covers 1,448 Core, 2,020 Desktop and 13 ReplayController tests. An existing legacy-import assertion was updated to compare settings immediately before/after import, while still checking the user's theme, because startup now records the exceptions migration marker. Its focused rerun passed; all other tests passed in the full solution run. No live Elite gameplay validation was performed.

## Saved-row deletion follow-up

The duplicate workspace overlay-settings button is removed; the sidebar icon remains. Shield boosters and point defence are excluded from fresh and cached dropdowns alongside shield cell banks. Previously saved assignments remain available for correction with an explicit excluded-module warning. Saved configuration rows provide a right-hand trash button. Both it and the editor's Remove button identify the target in a Yes/No confirmation; No, Escape or closing the dialog leave it intact. Deleting another row preserves the current draft, and stale rows cannot delete a configuration after a commander switch or restore.

Validation: all 2,024 Desktop tests passed, including fresh and cached filtering, direct deletion, cancellation, stale commander rows and draft preservation. The Monochrome saved-row layout and confirmation dialog were rendered and inspected.
