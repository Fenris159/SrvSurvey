# Guardian map authoring workflow audit

Date: 2026-08-27
Current implementation: `SrvSurvey-Avalonia` working tree at `762c22c0`
Legacy baseline: `main` at [`347846175ad531b68d0ce797a08d375483cefc10`](https://github.com/njthomson/SrvSurvey/commit/347846175ad531b68d0ce797a08d375483cefc10)
Official user documentation: [Surveying Guardian Sites](https://github.com/njthomson/SrvSurvey/wiki/Surveying-Guardian-Sites) and [Adding POI to a Guardian site](https://github.com/njthomson/SrvSurvey/wiki/Adding-POI-to-a-Guardian-site)

## Executive conclusion

The UI must keep two different editing scopes visibly separate:

1. **Edit this site survey** changes facts about one commander visit at one physical site: site type, origin, site heading, relic headings, item/tower statuses, notes, active obelisks, observed groups, component materials, and local raw points.
2. **Edit the shared Beta map template** changes geometry reused by every Beta site: background image, image origin offset, image scale, master POIs, destructible panels, and obelisk group-label locations.

The current **Start map draft** command is the second operation. It is not “new survey,” and it does not create a map just for the selected GR record. Renaming it to an unqualified **Edit Current Map** would be more misleading: the rendered map combines shared geometry with per-site survey state, while only the shared half would enter that draft.

Recommended primary labels are:

- **Edit this site survey** — opens/focuses the existing per-site Survey editor.
- **Edit shared Beta map…** — starts the existing template-authoring draft, after an impact warning that every Beta site uses it.

Use **Create shared map template…** only when no template exists. Do not use “Start map draft” as the route for attaching Beta to a new uncatalogued site; selecting Beta and saving the current site survey is the correct operation.

## The two data models

| Concern | One physical site / commander survey | Shared site-type template |
|---|---|---|
| Identity | system address/name, body ID/name, ruins/structure, index, commander | site type key such as `Beta`, display name |
| Calibration | journal-captured origin latitude/longitude; site heading; ruins relic heading | background image; image X/Y offset; metres-to-pixels scale |
| Survey content | notes, POI present/absent/empty status, relic headings, observed groups, active/scanned obelisks, component materials, raw `x*` points | master named POIs and their type/angle/distance/rotation, destructible panels, group-label positions |
| Persistence | one JSON file for this site and commander | one entry inside the complete `guardianSiteTemplates.json` catalog |
| Blast radius | this site only | every site using that type |

The official wiki defines ordinary Guardian surveying as measuring site/relic headings and recording which items and relic towers are present or missing. It describes past surveys and type templates as distinct choices in Survey Maps ([Surveying Guardian Sites, “Reviewing Surveys”](https://github.com/njthomson/SrvSurvey/wiki/Surveying-Guardian-Sites#reviewing-surveys)). It also says `.add` discoveries are stored locally and submitted with the site's survey data, not silently added to the global map ([Adding POI to a Guardian site](https://github.com/njthomson/SrvSurvey/wiki/Adding-POI-to-a-Guardian-site)).

The legacy model confirms the split. `GuardianSiteData` owns the site identity, origin, headings, statuses, groups, active obelisks, and raw POIs ([`GuardianSiteData.cs#L223-L251`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/game/GuardianSiteData.cs#L223-L251), [`GuardianSiteData.cs#L867-L923`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/game/GuardianSiteData.cs#L867-L923)). `GuardianSiteTemplate` is one reusable class of site and owns background metadata, image offset/scale, master POIs, destructible panels, and group-label positions ([`GuardianSiteTemplate.cs#L112-L134`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/GuardianSiteTemplate.cs#L112-L134)).

Avalonia preserves the same storage boundary. The per-site store writes identity, origin, headings, notes, groups, active obelisks, relic headings, statuses, raw POIs, and materials (`src/SrvSurvey.Core/Guardian/GuardianCommanderSurveyStore.cs:24-74,114-145`). The shared catalog exporter writes template name, background, image offset, scale, master POIs, group-label locations, and destructible panels (`src/SrvSurvey.Core/Guardian/GuardianSiteTemplateCatalogExporter.cs:150-214`).

## Intended ordinary survey workflow

Legacy's player workflow is **choose type -> calibrate this site's heading -> survey against the shared map**. The plotter only enters map mode after site type is known and `siteHeading` is not `-1` ([`PlotGuardians.cs#L124-L148`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L124-L148)). Type and heading are saved into the current site survey, including when captured from live cockpit-mode gestures ([`PlotGuardians.cs#L271-L348`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L271-L348), [`PlotGuardians.cs#L902-L930`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L902-L930)). The wiki documents the same alignment and double cockpit-mode toggle under [Measuring Site headings](https://github.com/njthomson/SrvSurvey/wiki/Surveying-Guardian-Sites#measuring-site-headings).

Avalonia's existing Survey editor is the proper home for that operation. It exposes Site Type, Site Heading, ruins Relic Heading, origin latitude/longitude, notes, observed groups, active obelisks, raw measured points, and Save (`src/SrvSurvey.Desktop/Views/GuardianView.axaml:1089-1275`). Saving copies the original site's immutable identity and replaces its type/calibration/observation values (`src/SrvSurvey.Desktop/ViewModels/GuardianSurveyEditorViewModel.cs:788-902`).

For an uncatalogued Beta site the UI flow should therefore be:

1. Open/select the live commander-created site survey.
2. Choose **Beta** as its site type.
3. Verify or recapture this site's journal origin.
4. Calibrate this site's heading using the Beta guide.
5. Save this site survey.
6. Survey expected Beta points; store unexpected observations as local raw points.

Nothing in this flow should create or modify the shared Beta map.

## Intended shared map-authoring workflow

### What legacy did

The public wiki does not document map-template authoring. Legacy exposed it as the developer-oriented `.editmap` command; `ee` selected the nearest POI in an already-open editor ([`PlotGuardians.cs#L602-L620`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L602-L620)). The editor mutated the active shared template and provided:

- live background offset/scale preview ([`FormEditMap.cs#L59-L70`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L59-L70), [`FormEditMap.cs#L223-L248`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L223-L248));
- master POI name/type/angle/distance/rotation editing with live preview ([`FormEditMap.cs#L277-L399`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L277-L399));
- a new master POI measured from the current site's origin and heading ([`FormEditMap.cs#L440-L463`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L440-L463));
- POI removal and shared group-label placement ([`FormEditMap.cs#L479-L527`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L479-L527)); and
- one Save action that wrote both the shared catalog override and the current site survey ([`FormEditMap.cs#L180-L188`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L180-L188)).

That last mixed Save is behavior to understand, not a UI pattern to repeat. It could write site status while editing shared geometry and made the operation's blast radius unclear.

Legacy also made the master/local distinction explicit in commands. `.new <type>` added a named master-template POI, while ordinary `.add <type>` created an `x*` raw point only in this site survey ([`PlotGuardians.cs#L415-L426`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L415-L426), [`PlotGuardians.cs#L468-L528`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L468-L528)).

### What Avalonia currently does

**Start map draft** is visible on the Selected Map card and invokes `TemplateAuthoring.StartCommand` (`src/SrvSurvey.Desktop/Views/GuardianView.axaml:634-660`). Starting clones the active base template and explicitly says changes remain local until catalog export succeeds (`src/SrvSurvey.Desktop/ViewModels/GuardianTemplateAuthoringViewModel.cs:477-490`; clone behavior is in `src/SrvSurvey.Core/Guardian/GuardianSiteTemplateAuthoringSession.cs:5-9,173-183`).

The draft tools edit background path, image X/Y, scale, measured master points, and group-label positions, then offer **Export verified catalog…** or **Discard draft** (`src/SrvSurvey.Desktop/Views/GuardianView.axaml:925-1087`). Master POI geometry is shared by every site of the type, and the selected-point UI already says so (`GuardianView.axaml:705-724`). Metadata changes mutate only the draft after **Apply metadata to draft**; a measured point is added to the draft from current live measurement (`src/SrvSurvey.Desktop/ViewModels/GuardianTemplateAuthoringViewModel.cs:493-533`). Selected master-point field changes have a temporary preview before Apply (`GuardianTemplateAuthoringViewModel.cs:653-690`). Discard drops the session and changes no file (`GuardianTemplateAuthoringViewModel.cs:624-637`). Changing to another site type currently discards an unexported draft automatically (`GuardianTemplateAuthoringViewModel.cs:387-415`).

This is a much safer base than legacy because it stages shared changes. Its main problem is discoverability and naming, not the draft model.

## Start map draft versus “Edit Current Map”

### Why “Edit Current Map” is unsafe wording

The displayed map is a composition:

```text
shared Beta template
  background + shared geometry + group-label positions

plus one site's survey
  origin + heading + statuses + relic headings + active obelisks + raw points
```

“Edit Current Map” does not say which half will change. It can reasonably be read as “fix only this GR/current location,” even though the existing command starts a shared type-level draft.

### Recommended interaction

On the Selected Map card, replace the single ambiguous action with an **Edit** menu or two explicit actions:

1. **Edit this site survey**
   Subtitle: “Type, origin, heading, statuses, notes, and local discoveries for this site.”
   Action: focus/expand the Survey editor; no template draft is created.

2. **Edit shared Beta map…**
   Subtitle: “Background and marker geometry used by every Beta site.”
   Confirmation: “Changes preview locally. Nothing is written until you export or install the shared catalog.”
   Action: start the existing template draft.

If a compact single button is required, prefer **Edit shared Beta map…** over **Edit Current Map**. The type name and the word “shared” carry the essential scope warning. When no base template exists, show a separate advanced **Create shared map template…** flow; the current `CanStart` requires an existing active template and cannot create one from nothing (`src/SrvSurvey.Desktop/ViewModels/GuardianTemplateAuthoringViewModel.cs:133-139,477-485`).

## Exact persistence and installation behavior

### Current commander surveys

On Windows, Avalonia's data root is `%APPDATA%\SrvSurvey\cross-platform` (`src/SrvSurvey.Core/Storage/AppDataPaths.cs:18-33,70-93`). A Guardian site survey is written atomically to:

```text
%APPDATA%\SrvSurvey\cross-platform\guardian\<FrontierId>\
    <BodyName>-ruins-<Index>.json
```

or `-structure-`; non-Odyssey data adds `legacy\` below the commander folder (`src/SrvSurvey.Core/Guardian/GuardianCommanderSurveyStore.cs:24-74`). Saving a per-site survey does not write the template catalog.

### Legacy shared template override

Legacy wrote the complete edited catalog to:

```text
%APPDATA%\SrvSurvey\SrvSurvey\1.1.0.0\guardianSiteTemplates.json
```

That editor file had higher load precedence than downloaded `pub` data and the packaged catalog ([`GuardianSiteTemplate.cs#L18-L47`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/GuardianSiteTemplate.cs#L18-L47)); `SaveEdits()` wrote the complete catalog there ([`GuardianSiteTemplate.cs#L68-L85`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/GuardianSiteTemplate.cs#L68-L85)). Public downloads lived separately under `pub\guardianSiteTemplates.json`, so updates did not overwrite the author's higher-priority local override.

Legacy **publish** was a maintainer action, not ordinary player save: it copied the override into source-controlled `SrvSurvey/guardianSiteTemplates.json`, wrote a backward-compatible `settlementTemplates.json`, and incremented reference-data versioning ([`GuardianSiteTemplate.cs#L87-L108`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/GuardianSiteTemplate.cs#L87-L108)). Publishing per-site survey data was separate and rebuilt `data/guardian/guardian.zip` ([`Git.cs#L455-L482`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/net/Git.cs#L455-L482)).

### Current Avalonia template export

The current file picker suggests `guardianSiteTemplates.json` but lets the user choose any local path (`src/SrvSurvey.Desktop/Views/GuardianView.axaml.cs:202-229`). Export writes the **complete catalog**, not a Beta-only patch; it stages a temporary file, hashes and round-trips it, backs up an existing target, detects concurrent target changes, atomically replaces the destination, and restores on activation failure (`src/SrvSurvey.Core/Guardian/GuardianSiteTemplateCatalogExporter.cs:32-147,150-214,226-358`). After a successful export, the running ViewModel adopts the updated catalog and rebuilds the current map in memory (`src/SrvSurvey.Desktop/ViewModels/GuardianTemplateAuthoringViewModel.cs:434-460`; `src/SrvSurvey.Desktop/ViewModels/GuardianViewModel.cs:4935-4949`).

However, **Export is not an Install command**:

- The picker does not copy the file into an application-managed override directory.
- On the next launch, the app loads templates from `%APPDATA%\SrvSurvey\cross-platform\pub\guardianSiteTemplates.json` if it exists and has at least embedded-catalog coverage; otherwise it falls back to the embedded catalog (`src/SrvSurvey.Core/Updates/LegacyReferenceCatalogLoader.cs:38-45,78-84,111-155`; `src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs:283-300,758-765`).
- Therefore, an export to Documents/Desktop is only an artifact. It is active in the current process because the ViewModel adopted it, but it is not reloaded on the next start.
- Saving directly over `cross-platform\pub\guardianSiteTemplates.json` would make it load next time, but that path is owned by the published-reference updater, which also stages downloads to that exact file (`src/SrvSurvey.Core/Updates/PublishedReferenceUpdateService.cs:308-342`). A later reference update can replace the edit. This is not a safe documented install path.

The current port therefore lacks the legacy-equivalent, higher-priority **local authoring override** destination.

## Background-image behavior

Legacy's template model serialized `backgroundImage`, but actual rendering/editor initialization used `images\<site-type>-background.png`. Choosing a PNG refreshed only the current runtime image; the chosen path was not copied or assigned back to the template field ([`FormEditMap.cs#L28-L30`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L28-L30), [`FormEditMap.cs#L192-L248`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/forms/FormEditMap.cs#L192-L248), [`PlotGuardians.cs#L761-L778`](https://github.com/njthomson/SrvSurvey/blob/347846175ad531b68d0ce797a08d375483cefc10/SrvSurvey/plotters/PlotGuardians.cs#L761-L778)). Shipping a new legacy map meant installing the conventionally named PNG with the application/source assets.

Avalonia's chooser assigns the selected absolute path to the draft (`src/SrvSurvey.Desktop/Views/GuardianView.axaml.cs:232-262`), and the exporter serializes that string. Rendering first uses it if it is a rooted existing file. Otherwise it takes only the filename and searches packaged `Assets/GuardianMaps` (`src/SrvSurvey.Desktop/Controls/GuardianMapImageCatalog.cs:8-27,46-89`). The desktop project packages only `Assets\GuardianMaps\*-background.png` (`src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj:74`).

Consequences:

- An absolute-path image works only while that same file remains at that same path.
- Export does not copy or bundle the PNG.
- A catalog plus a relative/background filename does not resolve beside the exported catalog; it falls back to a packaged asset of that filename.
- Export verification validates JSON/hash/template counts and geometry collection counts, but does not verify that the background image exists or decodes (`GuardianSiteTemplateCatalogExporter.cs:244-281`).

A portable template operation must therefore export/install a catalog **and** its background asset, or explicitly constrain authors to an already packaged filename.

## Recommended save, export, install, and publish semantics

### 1. Draft and preview

- Entering shared-map editing clones the current type template.
- Every field may preview live on the selected map, but all mutations stay in the draft object.
- **Apply point/metadata** accepts the form into the draft; clicking away restores the last applied draft state.
- Changing selected site/type while dirty should prompt **Keep editing / Discard / Cancel navigation**, not silently discard.
- The banner must remain visible: “Editing shared Beta map — affects every Beta site after install/publication.”

### 2. Save this site survey

- **Save survey** writes only the commander-site JSON.
- Site heading, origin, statuses, raw points, and notes never leak into a shared template export.
- Editing a raw point remains local. Add an explicit advanced **Promote measured point to shared Beta draft…** action if promotion is wanted; require a new stable master name and keep the site's present/absent status in the survey.

### 3. Export catalog…

- Keep the existing full-catalog, atomic, verified **Export catalog…** behavior.
- Label the result “Exported artifact; not installed.”
- Offer **Export portable bundle…** for a catalog plus copied PNG assets and a manifest/hash. Do not serialize arbitrary source-machine absolute paths in a portable bundle.
- Strengthen verification to compare all round-tripped fields, point identities and geometry—not only collection counts—and decode every included PNG.

### 4. Install local override…

Add a separate, reversible operation. It should:

1. write to a dedicated authoring directory, for example `%APPDATA%\SrvSurvey\cross-platform\authoring\guardianSiteTemplates.json`, never updater-owned `pub`;
2. copy selected backgrounds into `authoring\GuardianMaps\` and rewrite references to managed relative paths;
3. stage, hash, fully round-trip, decode images, and enforce catalog coverage before activation;
4. retain a timestamped backup and expose **Remove local override / Restore previous**;
5. load in explicit priority order `authoring override > published reference > embedded`, with the active source shown in the UI; and
6. either hot-reload transactionally or state that restart is required. Never imply that arbitrary Export installed it.

### 5. Publish

Do not expose “Publish” as a player save. Maintainer publication should remain a source-control/review operation that installs the catalog and conventionally named background assets in the repository, updates reference-data versioning as required, runs validation, and ships them together in a build or signed reference-data update. Per-site survey sharing/publication remains a separate workflow.

## Recommended workspace layout

Keep the compact Selected Map/Site card and survey point list as navigation. Then use two clearly scoped editor sections:

```text
Selected site: Blae Hypue ... 6 b · Ruins #1 · Beta

[Edit this site survey]
  identity (read-only) · origin · heading · statuses · notes · raw points
  [Save survey]

[Edit shared Beta map…]
  warning: used by every Beta site
  background · offsets · scale · master points · group labels
  [Export catalog…] [Install local override…] [Discard draft]
```

The shared editor can stay collapsed by default. Once opened, keep it near the map so live geometry preview is useful. The per-site editor should remain the obvious default because it is the normal commander workflow documented by the wiki.

## Acceptance criteria

1. The words “shared” and the current site type appear before entering template authoring.
2. **Edit this site survey** never creates a template draft or writes the template catalog.
3. **Edit shared Beta map…** never changes site heading, origin, statuses, notes, or raw points.
4. Raw-point promotion is explicit and retains site-local status separately.
5. Export reports that it is not installed; Install reports its exact managed path and rollback state.
6. Installed local overrides survive restart and published-reference updates without being overwritten.
7. Removing an override deterministically falls back to published, then embedded data.
8. Background images are included/managed and remain valid after source files move.
9. Full-catalog export/install validates exact round-trip content, coverage, hashes, and image decoding.
10. Navigation cannot silently discard a dirty shared draft.

## Sources and scope

This audit used only the current repository source, the pinned legacy `main` source, and the official `njthomson/SrvSurvey` wiki. The wiki documents the player survey workflow but not `.editmap` or template publication; those authoring details are established from the pinned legacy source. No production or test code was changed for this audit.
