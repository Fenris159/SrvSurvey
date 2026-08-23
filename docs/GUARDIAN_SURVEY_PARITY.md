# Guardian survey parity audit

Last audited: 2026-08-23

This audit compares the standalone Avalonia survey pipeline with the legacy
WinForms contract at upstream commit
`b9cac22183f00d846fbbaca4c47a40d1677532c4`. It treats the upstream source and
published JSON as review data: each behavior below was verified against the
current port before being accepted.

## Backend result

The port can build, persist, complete, and package Guardian site surveys in the
compact legacy JSON format. No backend format or completion defect was found.
The current pipeline preserves unknown top-level JSON and component entries,
uses an atomic file replacement, and restricts share inputs to the active
commander's Guardian folder.

| Contract | Port implementation | Evidence |
|---|---|---|
| Create/update a visited site | `GuardianLiveSiteState` and `GuardianViewModel` | Approach-settlement and live-site tests |
| Read legacy and current survey shapes | `GuardianCommanderDataReader` | Migration and malformed-data tests |
| Write commander identity, visit times, site/body identity, notes, headings, location, groups, and active obelisks | `GuardianCommanderSurveyStore` | Exact JSON round-trip test |
| Write present, absent, and empty POI sets | `GuardianCommanderSurveyStore` | Compact `poiPresent`, `poiAbsent`, and `poiEmpty` assertions |
| Write individual relic headings, raw POIs, and component/destructible-panel materials | `GuardianCommanderSurveyStore` | Legacy type-name and component-string assertions |
| Calculate completion | `GuardianSurveyCompletionCalculator` | Every published survey is checked against its legacy summary |
| Package discoveries for release | `GuardianSurveyShareService` | Content-addressed ZIP, discovery filtering, and path-safety tests |
| Author and export shared site templates | `GuardianSiteTemplateCatalogExporter` | All supported POI types and export round trips |

`CompleteSurveyRoundTripsIntoLegacyShareArchive` additionally exercises the
whole contract in one path: save a complete structure survey with every
category of commander data, reload it, calculate completion, package it, and
inspect the JSON inside the ZIP.

The upstream publication at `e257b427` is included in the embedded ruin and
structure catalogs, Guardian ZIP, and site templates. The following `ac45f26d`
Gamma T9 correction is also included and protected by a focused assertion.

## UI workflow coverage

The main-window Survey editor now closes the three repair and authoring gaps
identified by the audit:

1. A visible site-type selector can replace an unknown or misidentified
   template. Switching templates clears incompatible template-point state but
   preserves commander-specific raw points.
2. Latitude and longitude are editable together, with range validation and an
   explicit both-blank representation for an unknown surface origin.
3. Active-obelisk rows can be added, selected, edited, or deleted, including
   marker name, log code, artifact requirements, and commander scan state. The
   save path validates and writes the same compact legacy representation used
   by the reader, share service, and published catalogs.

Map interaction is linked to the same editor rather than a second workflow.
Hovering a point draws the segmented green focus ring used by the legacy map;
clicking selects that point and opens its editable details beside the map.
Commander-specific raw points additionally expose type, angle, distance, and
rotation for precision correction, plus deletion. Selecting a matching active
obelisk marker selects its metadata row.

Component materials remain available for component towers and destructible
panels when **Track Guardian component materials** is enabled in Overlay
Settings. POI status (including valid empty states), relic headings, obelisk
groups, notes, and share-bundle creation use the same Survey editor save path.

## Remaining runtime proof

Automated coverage cannot confirm live coordinate capture, fire-group/blink
gestures, the feel of map hit targets at every template and zoom level, or the
external recipient's ingestion of a fresh ZIP. Exercise those paths with an
Odyssey commander before promoting the next release candidate.
