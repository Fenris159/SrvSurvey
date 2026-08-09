# Exobiology parity checklist

Last updated: 2026-08-08

Compares the Avalonia port against upstream WinForms
(`njthomson/SrvSurvey`) for exobiology workflows: overlays, data feed,
lookups, settings/UI, and cross-surface integration.

Status legend: `done` · `n/a` (already equivalent) · `open`

## Workflow mapping

| Legacy | Port | Status |
|--------|------|--------|
| `PlotBioStatus` | `BiologyStatusViewModel` + overlay | done |
| `PlotBioSystem` | `BiologySurveyViewModel` + overlay | done |
| `PlotPriorScans` | `PriorScanPlanner` + overlay | done |
| `PlotGrounded` / `PlotTrackers` / mini-track | `SurfaceSurveyViewModel` | done |
| `PlotGrounded.drawPriorScans` | Surface radar `CanonnPrior` markers | done |
| `FormPredictions` | `BiologyPredictionsViewModel` | done |
| `FormShowCodex` | `BiologyCodexViewModel` | done |
| `FormCodexBingo` | `BiologyCodexBingoViewModel` | done |
| FormSettings Bio Scanning | Overlay Settings + Exobiology workspace | done |
| Main first-footfall checkbox | Exobiology workspace + hotkey / `.ff` | done |
| `PlotRouteBio` (port-only) | `RouteBioOverlayCoordinator` | n/a (legacy has no route-bio targets) |

## Checklist items

| ID | Item | Status | Notes |
|----|------|--------|-------|
| E1 | Clear-all-trackers command + settings control | done | Mirrors FormSettings `btnClearTrackers` |
| E2 | Prior-scan Horizons / Radicoida display names | done | `PriorScanPlanner.FormatDisplayName` |
| E3 | Bio status sample-range scale bar | done | Legacy cyan bar at 0.25× range |
| E4 | Bio status codex image indicator | done | Picture glyph when entry has/lacks image |
| E5 | Bio status stale active-scan warning layout | done | Red warning bars + footer copy |
| E6 | Predictions expand/collapse (= Everything / Bodies only) | n/a | Collapse all already collapses body expanders |
| E7 | Prior-scan external data gate | n/a | Legacy uses `useExternalData` only; port matches |
| E8 | Overlay fonts | done | `srv-overlay` Oxanium + Rajdhani styles |
| E9 | Regression tests for E1–E5 | done | Core + Desktop tests |
| E10 | Re-audit loop after implementation | done | Build + full related tests |
| E11 | Canonn rings on surface radar | done | Coordinator pushes `SurfaceMarkers` → `SetPriorScanSurfaceMarkers` |
| E12 | First-footfall control on Exobiology tab | done | Checkbox + `ToggleFirstFootfallCommand` |
| E13 | Live sample 1/2/analyse indicators on Exobiology tab | done | Bound to scanOne / scanTwo |
| E14 | Canonical legacy/Horizons organism identity | done | EntryID/variant-first matching preserves multiple same-genus organisms across journal, merge, and migration paths |
| E15 | Regional Codex fallback and first-discovery identity | done | Journal `Region` is used when coordinates are absent; unresolved EntryIDs are never treated as commander firsts |
| E16 | Prior-scan ownership history | done | Hide-own filtering includes durable non-death samples, not only the two live sampling slots |
| E17 | Sampling lifecycle and restart identity | done | Analyse always clears partial active state; restart display and active rows resolve EntryID/species before genus |

## Surface integration map

```text
Journal/Status
  → ExobiologyState / SystemScanState / SurfaceSurveyJournalTracker
  → SystemSurveyViewModel.ApplyUpdate
  → Biology* / PriorScans / SurfaceSurvey projections
  → ShouldShow* + coordinator obscuring
  → Avalonia overlays (Oxanium/Rajdhani)

Canonn POI (useExternalData + autoLoadPriorScans)
  → PriorScansOverlayViewModel (list + optional prior radar)
  → SurfaceMarkers → SurfaceSurvey.SetPriorScanSurfaceMarkers
  → PlotGrounded rings when ShowCanonnSignalsOnRadar
```

| Overlay / UI | Content refresh | Show/hide |
|--------------|-----------------|-----------|
| Bio status | `ApplyUpdate` rebuilds `BiologyStatus` | `ShouldShowBioStatus` + obscuring + DSS window |
| Bio system | same via `BiologySurvey` | `ShouldShowBioSystem` + map modes / body detail |
| Prior scans list | timer + `RefreshAsync`; Snapshot / status / exo | `ShouldLoadPriorScans && HasSpecies` |
| Surface radar / trackers | `ApplyUpdateAsync` + CurrentStatus / CurrentExobiology / Snapshot; Canonn markers via prior-scan plan | `ShouldShow` / mini-track gates |
| Route bio | route workspace hop targets | `ShouldShowRouteBioOverlay` (port extension) |
| Exobiology tab | `UpdateExobiologyDisplay` | always in nav |
| Predictions / Codex / Bingo | window coordinators from Exobiology tab | system/context gates per window |

Hard hide conditions exercised in tests: docked, taxi, FSD jump, System Map
(for bio status), AutoShow off, DSS temporary window expiry, guardian/human
obscuring (coordinator), build-project suppress.

## Integration notes (audit 2026-08-08)

**Wired and matching legacy**

- Overlay allow gates for bio status / system / prior / grounded / mini-track
- Guardian / Human / Station / Jump-info obscuring (mini-track intentionally not
  build-project or guardian-obscured in legacy either)
- Track1–8, first-footfall hotkey, `.ff` / `.show` / bookmark chat commands
- Composition-scan auto-trackers, auto-remove on sample / final analyse
- External data vs external bio data split; prior scans need external data only
- Codex images settings, reward buckets, predictions disable flag
- System status remaining bio list when bio system plotter is off
- Organic Codex surface filtering for fixed life events and invalid coordinates
- Body-local composition tracker suppression for already-analyzed species
- Canonical Horizons genus recovery for Brain Trees, anemones, bark mounds,
  Amphora Plants, crystalline shards, and sinuous tubers

**Port-only (no legacy equivalent)**

- Route bio hop overlay (Spansh exobiology routes)

**Known non-gaps**

- First-footfall inference color matching has settings store but no dedicated
  settings UI (advanced; values migrate from legacy JSON)
- Opening predictions/codex/bingo is UI-driven (legacy Main buttons), not hotkeys

## Offline residual-risk hardenings (items 1–4)

| Risk | Safe mitigation in tree |
|------|-------------------------|
| Canonn body-name join | `ExobiologyBodyNames` normalize (spaces + system prefix with token boundary); planner + prior context + personal-sample filter |
| Status thrash / empty flash | Prior plan presentation retained across reload of the same body; surface markers only apply when the list changes |
| Multi-body sampling | Tests: new organic on new body abandons prior active sample; status-only body change keeps stale sample; other-body sample not radar content |
| Bootstrap organic skip | Tests: `processJournalMutations: false` ignores ScanOrganic/SendText surface mutations; live path still mutates |

## Remaining runtime proof (not source gaps)

Live Elite Dangerous validation of overlay placement, Canonn POI freshness,
and multi-body sampling remains an RC acceptance gate rather than a missing
port feature.
