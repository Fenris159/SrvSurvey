# Exobiology parity checklist

Last updated: 2026-08-04

Compares the Avalonia port against upstream WinForms
(`njthomson/SrvSurvey`) for exobiology workflows: overlays, data feed,
lookups, and settings/UI.

Status legend: `done` · `n/a` (already equivalent) · `open`

## Workflow mapping

| Legacy | Port | Status |
|--------|------|--------|
| `PlotBioStatus` | `BiologyStatusViewModel` + overlay | done |
| `PlotBioSystem` | `BiologySurveyViewModel` + overlay | done |
| `PlotPriorScans` | `PriorScanPlanner` + overlay | done |
| `PlotGrounded` / `PlotTrackers` / mini-track | `SurfaceSurveyViewModel` | done |
| `FormPredictions` | `BiologyPredictionsViewModel` | done |
| `FormShowCodex` | `BiologyCodexViewModel` | done |
| `FormCodexBingo` | `BiologyCodexBingoViewModel` | done |
| FormSettings Bio Scanning | Overlay Settings + Exobiology workspace | done |

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

## Data feed contract

```
Journal/Status
  → ExobiologyState / SystemScanState / SurfaceSurveyJournalTracker
  → SystemSurveyViewModel.ApplyUpdate
  → Biology* / PriorScans / SurfaceSurvey projections
  → ShouldShow* + coordinator obscuring
  → Avalonia overlays (Oxanium/Rajdhani)
```

## Overlay refresh and visibility contract

| Overlay | Content refresh | Show/hide |
|---------|-----------------|-----------|
| Bio status | `SystemSurvey.ApplyUpdate` rebuilds `BiologyStatus` on every journal/status/exo change; bindings re-read the new record | `ShouldShowBioStatus` (+ coordinator obscuring, game focus, 250 ms timer) |
| Bio system | same via `BiologySurvey` | `ShouldShowBioSystem` (+ map modes / body detail, repeat-visit suppress) |
| Prior scans | 250 ms timer + `RefreshAsync`; recalculates on `Snapshot` / `CurrentStatus` / exo / filters | `ShouldLoadPriorScans && HasSpecies` |
| Surface radar / trackers | MainWindow `SurfaceSurvey.ApplyUpdateAsync` on journal/status; also reacts to `CurrentStatus` / Snapshot INPC | `ShouldShow` / mini-track gates |

Hard hide conditions exercised in tests: docked, taxi, FSD jump, System Map
(for bio status), AutoShow off, DSS temporary window expiry, guardian/human
obscuring (coordinator), build-project suppress.

## Remaining runtime proof (not source gaps)

Live Elite Dangerous validation of overlay placement, Canonn POI freshness,
and multi-body sampling remains an RC acceptance gate rather than a missing
port feature.
