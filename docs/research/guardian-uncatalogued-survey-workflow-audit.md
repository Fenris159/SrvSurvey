# Guardian uncatalogued survey workflow audit

Date: 2026-08-27
Current implementation: `SrvSurvey-Avalonia` working tree
Legacy baseline: `main` at `347846175ad531b68d0ce797a08d375483cefc10`

## Executive conclusion

The legacy workflow did **not** ask the commander to create a survey manually or type the system name. Approaching an Ancient settlement created a new per-site commander survey automatically from the live `ApproachSettlement` journal event. The event and current commander state supplied system/body identity, site index, and surface coordinates. For ruins, the commander then chose Alpha, Beta, or Gamma and explicitly calibrated the site heading before the map appeared.

The Avalonia port implements that same data-acquisition sequence in its model, but the current workspace does not present it coherently. In particular:

1. **Beta is a shared layout template, not a GR record.** The new uncatalogued site should own a new survey keyed to the current system/body/index/location and reference the Beta template. It must not reuse GR 446 merely because GR 446 was previously selected.
2. **The main Survey Map's live link requires a catalog reference.** An uncatalogued `GuardianLiveSiteSnapshot` has `Reference == null`, so the current code suppresses the commander marker, live target, and live measurement even after it has synthesized a commander-only row for that survey.
3. **Automatic current-site selection is filter-dependent.** Selection searches only the filtered `Rows` collection and retains the old selection if the active row is absent. A type/search/visit filter can therefore leave GR 446 selected while the overlay is operating on a different live site.
4. **The per-site controls exist, but they are buried below the map and omit site identity.** Site type, site heading, ruins relic heading, latitude, longitude, notes, POIs, obelisks, and Save are present. System, body, index, and site kind are neither shown in that editor nor repairable there.
5. **Heading zero is treated differently from legacy.** Legacy considers only `-1` unknown. Avalonia sends a valid heading of `0` back to heading mode unless `.map` is forced.

The user's observed state—an uncatalogued live site while the application still shows selected map GR 446—is therefore not a missing manual step. It exposes a current-site selection and identity-link gap in the port.

## Current-session saved state

A read-only check of the machine-local cross-platform Guardian survey folder confirms that the port already created the new survey. The newest file is `Blae Hypue MK-C d14-163 6 b-ruins-1.json`, last written at 2026-08-27 18:08 local time. It contains:

- system `Blae Hypue MK-C d14-163`;
- body `Blae Hypue MK-C d14-163 6 b`, ruins index 1;
- site type `Beta`;
- site heading `101` degrees; and
- an `ApproachSettlement` origin at latitude `20.588827`, longitude `35.39212`.

This proves that system identity, site type, origin, and heading were persisted for the current location. It does **not** prove that heading `101` is geometrically correct; that still depends on whether it was captured while facing the Beta guide's depicted buttress. It also confirms that GR 446 remaining in the selected-map card is a presentation/synchronization failure, not reuse of GR 446's survey file.

## What the workflow is supposed to do

| Value | Legacy acquisition | Avalonia acquisition | User action |
|---|---|---|---|
| Site kind | Settlement name prefix (`$Ancient:` ruins, `$Ancient_` structure) | Same prefix parsing | None |
| Site index | Parsed from `:#index=N;` | Same parsing | None |
| System address | `ApproachSettlement.SystemAddress` | Same journal field | None |
| System name | Current commander system, when address/body match | Cached `Location`/`FSDJump`/`CarrierJump` system name, then carried into the site | None in normal operation |
| Body ID/name | `ApproachSettlement.BodyID` / `BodyName` | Same journal fields | None |
| Survey origin | Latest `ApproachSettlement` latitude/longitude | `ApproachSettlement` latitude/longitude | Repair only if journal coordinates are wrong/missing |
| Site type | Structures inferred from settlement name; ruins start Unknown | Same | Choose Beta for this ruins layout |
| Map/template | Shared template selected by site type | `templates.Find(siteType)` | Choosing Beta should load Beta's shared layout into the new site survey |
| Site heading | Explicitly captured after aligning to a mapped feature | Same command/blink concept, or numeric editor | Align correctly, then capture/enter heading |
| GR/catalog identity | Optional public/reference match | Optional embedded/published reference | None for a genuinely uncatalogued site |

### Current Avalonia acquisition and save path

`GuardianLiveSiteState` listens for `Location`, `FSDJump`, `CarrierJump`, `SupercruiseExit`, and `ApproachSettlement` events (`src/SrvSurvey.Core/Guardian/GuardianLiveSiteState.cs:37-78`). It caches the current system name/address from location-style events (`GuardianLiveSiteState.cs:218-231`). On `ApproachSettlement`, it parses kind/index, requires system address/body identity, captures latitude/longitude, and creates a live snapshot; a catalog reference is optional (`GuardianLiveSiteState.cs:233-297`).

When no survey exists, `CreateOrUpdateSurvey` creates one with the live settlement name, commander, visit times, kind-derived type, index, system address/name, body identity, and location; headings start at `-1` (`GuardianLiveSiteState.cs:150-215`). `GuardianViewModel` invokes that path automatically for a recognized live `ApproachSettlement` and saves the result (`src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs:2024-2083,2146-2178`). The store writes a legacy-compatible JSON file named `{BodyName}-ruins-{index}.json` or `{BodyName}-structure-{index}.json` and persists all identity/calibration fields (`src/SrvSurvey.Core/Guardian/GuardianCommanderSurveyStore.cs:24-74,114-145`).

This means there should be no separate “New survey” form. The equivalent operation is: **detect current Ancient settlement -> create/select commander survey -> choose Beta -> calibrate heading -> show Beta template at the captured origin**.

### Legacy acquisition and save path

Legacy uses the same journal-first design. An Ancient `ApproachSettlement` creates or updates the settlement and refreshes its location from the latest journal event (`main@34784617 — SrvSurvey/game/SystemData.cs:1186-1206`). `GuardianSiteData.Load(entry)` creates the file when missing, derives its filename from body/kind/index, gets location/address/body from the event, and copies the current commander system name only when live address/body agree (`main@34784617 — SrvSurvey/game/GuardianSiteData.cs:41-121`). The legacy map form's apparent system textbox actually displays `bodyName` and is read-only (`main@34784617 — SrvSurvey/forms/FormRuins.cs:222-239; SrvSurvey/forms/FormRuins.Designer.cs:479-487`).

The active legacy `systemSite` follows the nearest Ancient settlement and switches records when the nearest settlement changes (`main@34784617 — SrvSurvey/game/Game.cs:2113-2177,2756-2778`). This is the important behavior the current workspace must preserve: the active survey is a live site instance, not whichever catalog map the user last browsed.

## Beta template versus GR 446

Legacy explicitly separates shared templates from surveyed site records. `GuardianSiteTemplate` is keyed by site type and owns the background, offsets, scale, shared POIs, and group label positions (`main@34784617 — SrvSurvey/GuardianSiteTemplate.cs:10-60,115-126`). The legacy form can preview a “Beta Template” separately from selecting a surveyed site (`main@34784617 — SrvSurvey/forms/FormRuins.cs:150-216,275-368`).

Avalonia also has this separation:

- The survey editor's `SiteType` reloads points from the selected type's template (`src/SrvSurvey.Desktop/ViewModels/GuardianSurveyEditorViewModel.cs:168-187`).
- The live map resolves the commander survey's type and calls `FindTemplate(siteType)` before projecting (`src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs:4522-4562`).
- “Start map draft” opens **shared template authoring**, whose text says edits are shared by every site of that type and persist through catalog export (`src/SrvSurvey.Desktop/Views/GuardianView.axaml:656-660,705-783,925-1087`). It is not the action for creating the current site's survey.

Therefore the correct model for the user's current location is:

```text
uncatalogued live site survey
  identity: current system + current body + ruins index + current coordinates
  catalog reference: none
  site type/template: Beta
  calibration: this site's heading
  observations: this site's POI states, notes, relic headings, raw POIs
```

GR 446 is a different site instance and should not supply the new survey's identity or origin.

## Heading calibration: what legacy actually did

Legacy enforced the sequence **site type -> site heading -> map**. Unknown type entered site-type mode; only `siteHeading == -1` entered heading mode; otherwise it loaded the map (`main@34784617 — SrvSurvey/plotters/PlotGuardians.cs:124-166`).

For ruins, heading mode showed a vertical alignment stripe and a type-specific guide. The commander faced the depicted buttress/alignment feature and then either:

- sent `.heading` while already in heading mode to capture the current compass heading,
- sent `.heading N` (or a bare number while in heading mode), or
- used the configured double-toggle/blink gesture, which captured the current status heading.

The relevant legacy implementation is `main@34784617 — SrvSurvey/plotters/PlotGuardians.cs:173-184,309-348,745-755,902-920,1998-2045`. `.alphaflip` adds 180 degrees for the reversed Alpha interpretation (`PlotGuardians.cs:337-343`); it is not a general Beta correction.

Avalonia ports those interactions: its prompt says to face the mapped alignment feature and use `.heading` or `.heading <degrees>` (`src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs:607-624`); its commands save the normalized heading (`GuardianViewModel.cs:2839-2935`); and its blink gesture saves the current status heading (`GuardianViewModel.cs:3090-3118`). It also has Beta-specific heading guidance (`GuardianViewModel.cs:697-727`).

### Interpreting an “off” map

- If every marker is rotated around the origin by roughly the same amount, the saved **site heading** is wrong. Re-enter heading mode, align to the Beta guide's feature, and recapture the compass heading.
- If the map is consistently shifted rather than rotated, inspect the saved **surface latitude/longitude** against the live site's `ApproachSettlement` coordinates. Both legacy and Avalonia use those coordinates as the survey origin.
- If the overlay looks coherent but the application map lacks the live commander/target or remains on GR 446, that is the uncatalogued-site linking defect below, not proof that the coordinates are wrong.
- A Beta map that is exactly 180 degrees wrong should be recalibrated against the Beta guide; the Alpha-only flip command is not the correct general remedy.

## Controls currently present in the Guardian workspace

The Survey Map tab currently contains:

- selected-map projection, zoom, orientation help, collapsible legend, selected-map summary, compact survey points, and marker editing (`src/SrvSurvey.Desktop/Views/GuardianView.axaml:530-923`);
- shared template draft tools for background, offsets, scale, measured master points, labels, and catalog export (`GuardianView.axaml:925-1087`);
- a per-site Survey editor containing Site Type, Site Heading, Ruins Relic Heading, Surface Latitude, Surface Longitude, notes, obelisk groups, active obelisks, raw points, and Save (`GuardianView.axaml:1089-1275`).

The `SystemNameEntry` elsewhere on the Sites & surveys tab is labeled **DISTANCE ORIGIN** and affects list distances; it is not the current survey's system name (`GuardianView.axaml:90-123`). No per-site identity fields bind system name, system address, body name, body ID, kind, or index. The editor saves by copying the original survey and replacing type/calibration/observation fields, so it cannot repair identity (`src/SrvSurvey.Desktop/ViewModels/GuardianSurveyEditorViewModel.cs:829-864`).

## Confirmed Avalonia parity gaps

### P0 — uncatalogued live site cannot drive the selected map

The main-map bridge requires both selected and active sites to have equal, non-null `Reference` objects:

- `SelectedMapCommanderPosition` returns proximity only when `ActiveSite.Reference` is non-null and equals `SelectedSite.Reference`.
- selected-map target/manual highlight synchronization uses the same reference equality.

See `src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs:942-975`. But an uncatalogued site's live snapshot deliberately has `Reference == null`; after save, the visit catalog creates a synthetic commander reference (`src/SrvSurvey.Core/Guardian/GuardianSiteVisitCatalog.cs:31-38,166-195`). Those are the same physical site by address/body/kind/index, but the equality gate cannot recognize them.

The same problem blocks live measured-point data. `UpdateProximity` sends measurement to the survey/template editors only when the active catalog `reference` is non-null and equals the selected reference (`GuardianViewModel.cs:4506-4602`). Thus a commander-only uncatalogued survey cannot get the same live editor behavior as a catalogued site.

**Required change:** use one canonical site-identity predicate (system address + body identity + kind + index), not catalog-reference object identity, everywhere active and selected site state are synchronized.

### P0 — old selected map can survive live-site detection

After saving a live site, Avalonia rebuilds visits, applies filters, then calls `SelectActiveReference` (`GuardianViewModel.cs:1997-2003`). `ApplyFilters` builds `Rows` from the active kind/visit/type/text filters and preserves the prior selection when possible (`GuardianViewModel.cs:4940-4997`). `SelectActiveReference` searches only those filtered rows and falls back to the existing selection when no match is present (`GuardianViewModel.cs:3675-3692`).

Consequently, if the new Unknown/Beta site is hidden by an existing filter, GR 446 can remain selected. Even without a filter, the absence of a direct “Use current site” action makes the resulting state difficult to diagnose.

**Required change:** active-site selection must resolve against the unfiltered visit model, clear/bypass incompatible filters, and provide an explicit **Open current survey** action. Never silently retain an unrelated selected site after a live Ancient settlement is recorded.

### P1 — valid heading zero is treated as incomplete

Legacy advances to the map for every heading except `-1` (`main@34784617 — SrvSurvey/plotters/PlotGuardians.cs:133-148`). Avalonia enters heading mode for `heading == 0` unless map mode is forced (`src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs:4281-4304`), and the current test codifies `.heading 0` followed by a `.map` workaround (`tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs:963-970`).

**Required change:** reserve `-1` for unknown and accept `0..359` as calibrated headings, matching the legacy data contract.

### P1 — setup state is hidden and system identity is not repairable

The live card shows a title, description, coordinates, visit, and “Uncatalogued site” badge (`GuardianView.axaml:61-88`), while the editable calibration controls are far below the map (`GuardianView.axaml:1089-1150`). The screen has no cohesive statement of:

- which live system/body/index is being surveyed,
- whether a commander survey file was created,
- whether it is linked to a catalog GR entry or deliberately unlinked,
- which shared template is attached,
- whether heading calibration is complete, and
- whether the selected map is the same physical site as the live site.

Legacy did not provide manual system entry either, but it kept one active `systemSite` synchronized to proximity. The port's split browser/workspace requires stronger state presentation.

## Recommended workspace fit

### 1. Add a compact “Current site setup” card above the map

Use the active live site, not the selected browser row, as the source. Show:

- System name and address (read-only normally)
- Body name and ID (read-only normally)
- Site kind and index
- Origin latitude/longitude with “captured from ApproachSettlement” status
- Catalog reference: GR ID or “Uncatalogued / not linked”
- Survey file state/path
- Template/site type selector (Beta here)
- Heading state: Unknown or calibrated degrees
- Primary action: **Open current survey / Use current site**

If the selected map differs from the active site, show an explicit warning and a one-click switch rather than silently rendering GR 446.

### 2. Put per-site calibration beside identity

Keep Site Type, Site Heading, Ruins Relic Heading, and coordinates together. Add explicit actions:

- **Begin/recapture heading** — enters heading mode and shows the Beta guide
- **Use current heading** — captures the live compass heading with a confirmation
- **Enter degrees** — retains the numeric repair path
- **Use current journal origin** — restores captured ApproachSettlement coordinates

Do not put these in “Map draft tools”; they belong to this site's survey.

### 3. Keep shared template authoring separate and advanced

Rename or annotate it as **Shared Beta template authoring (affects every Beta site)**. It should remain collapsed unless deliberately entered. “Start map draft” must never read like “create a survey for this current site.”

### 4. Add a collapsed identity-repair section

Normal identity stays journal-driven. When system name is blank or a journal field is demonstrably wrong, permit validated repair of system name/address, body name/ID, kind, and index. Explain that changing these fields changes the survey identity/path and should be exceptional.

### 5. Cover the uncatalogued path end-to-end

Add tests proving that an uncatalogued ruins `ApproachSettlement`:

1. creates exactly one commander survey with live identity/location;
2. synthesizes and selects its site row even when the previous row was GR 446;
3. bypasses or reports filters that hide it;
4. attaches Beta after type selection without acquiring a GR identity;
5. shows commander position, nearest target, and live measured-point data on the main map;
6. accepts headings `0`, `1`, and `359` as complete; and
7. keeps template authoring separate from the per-site survey.

The existing live-visit test proves creation and selection only for a **known catalog reference** (`tests/SrvSurvey.Desktop.Tests/ViewModels/GuardianViewModelTests.cs:622-660`); it does not exercise the uncatalogued null-reference seam.

## Practical interpretation for the current session

The intended current-build sequence is:

1. Re-approach the Ancient settlement if necessary so a fresh `ApproachSettlement` event is observed.
2. Confirm the LIVE SITE card shows the current body, “Uncatalogued site,” and plausible coordinates, and that status reports the live Guardian site was recorded.
3. Clear Sites & surveys filters if the current row is not visible. Select the commander-created row for the current body/index—not GR 446—and open Survey map.
4. In Survey editor, choose **Beta**. Do not use “Start map draft” merely to make this site use the Beta layout.
5. Align to the feature shown by the Beta heading guide and recapture site heading with the overlay gesture/`.heading`, or enter the verified degrees in Survey editor and save.
6. If heading `0` is correct in the current build, `.map` is the existing workaround to leave heading mode.

These steps describe the intended ported workflow, but the null-reference and filtered-selection gaps mean the application window may still fail to follow the uncatalogued active site correctly. That behavior should be fixed rather than documented as a required commander workaround.
