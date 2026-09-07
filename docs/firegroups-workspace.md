# Firegroups workspace and overlay

## Requested workflow

The Firegroups workspace has a dedicated overlay settings window containing the enabled toggle, its existing shortcut and Overlay Exceptions. Overlay editor placement stays under Status & utilities. A–H selection uses letters and arrow buttons. Primary and secondary assignments support circled add/remove controls and select equipped modules from journal Loadout events. Add group stages the assignments and advances to an unused letter; a tree previews groups and their primary/secondary modules. The name field, Save and Remove share a row. Saved configuration names reopen the editor; expandable rows preview the saved contents. The overlay selects a saved configuration for the currently boarded ship and maps Status.json FireGroup 0–7 to A–H.

## Data and editing

Configurations are commander-scoped in the `firegroups` data folder. Journal ship type and ShipID identify a ship; ShipName is retained for display. Loadouts are cached so saved setups remain editable after restart. Named configurations for the same ship have a persisted active selection. Ship renames preserve identity, and two ships of the same type can use different configurations.

The editor uses observable rows and saved collections. Drafts survive switching configurations or ships within the application session. Group arrows retain entered assignments. Save validates name, content and repeated assignments, writes atomically, and publishes the saved row only after success. Errors remain visible below the workspace. Old flat Mining firegroups are imported once for the first identified ship; old Mining files remain intact. Imported or unequipped assignments are retained and marked for replacement, never silently remapped. Ship loadouts and named profiles are stored separately from mining reports; include the `firegroups` folder in application-data backups.

## Equipped module catalog

The checked-in catalog is a filtered snapshot of [EDCD FDevIDs outfitting.csv](https://github.com/EDCD/FDevIDs/blob/master/outfitting.csv), retrieved 2026-09-07. It contains 315 hardpoint, utility and internal limpet-controller variants, excluding shield cell banks. The game’s equipped module symbols are matched case-insensitively. Only equipped matching modules are offered for new assignments; slots distinguish duplicate modules. No runtime network lookup or new package is required. Future unlisted module symbols require a catalog update.

## Visibility and compatibility

Firegroups retains PlotMiningFiregroups, its shortcut identity and saved overlay placement. It now has its own settings category. Previous Global vehicle exceptions are copied once into that category, then saved independently. The overlay uses the saved profile and the reported active group rather than unsaved editor content. It does not send inputs to Elite. A vessel without an identified Loadout/profile has no active Firegroups overlay. Existing mining notifications keep their own visibility rules.

## Diagnosis and validation

A headless test reproduced the old Save symptom: the handler updated the model, while its plain list failed to refresh the rendered rows. The replacement exercises observable saved rows through the view’s Save command, with visible success feedback and expandable tree contents. Additional tests cover equipped-module filtering, duplicate modules, A–H mapping, multi-module persistence, same-type ship switching, draft retention, missing-module warnings, loadout no-op identity, save failure, commander isolation and legacy migration. Headless Monochrome rendering was inspected. Live gameplay remains a local-build verification step.
