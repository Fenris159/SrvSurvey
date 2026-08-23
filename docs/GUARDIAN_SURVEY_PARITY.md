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

## UI follow-up candidates

These do not block backend compatibility or the legacy completion score, but
they are worth a separate UX review:

1. Site type can be selected through the live overlay/chat flow (`.site`) and
   is normally inferred for known sites, but the survey editor has no visible
   site-type control. An unknown or misidentified site therefore has a
   discoverability and repair gap in the main window.
2. Surface location is captured from the live visit or published reference and
   displayed read-only. The survey editor cannot repair a missing or incorrect
   origin, even though location is required for a survey to be complete.
3. The UI can scan or unscan known active obelisks and persists their legacy
   representation, but it cannot author an active-obelisk name, log code, or
   artifact requirements for a newly discovered unpublished site. Active
   obelisks are intentionally ignored by legacy discovery comparison and
   completion scoring, so this is a catalog-authoring gap rather than a survey
   release-format defect.

Component materials are not missing: the editor exposes component towers and
destructible panels when **Track Guardian component materials** is enabled in
Overlay Settings. Raw points, POI status (including valid empty states), relic
headings, obelisk groups, notes, and share-bundle creation are all available.

## Remaining runtime proof

Automated coverage cannot confirm live coordinate capture, fire-group/blink
gestures, nearest-POI selection, or the external recipient's ingestion of a
fresh ZIP. Exercise those paths with an Odyssey commander before promoting the
next release candidate.
