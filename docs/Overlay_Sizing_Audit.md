# Live overlay sizing audit

## Reproduction and cause

The Next-jump editor was saving a panel size of 764 × 276 while its live window
still imposed a 600-pixel width. The earlier FSS fix forwarded the size override
only to presentations implementing `IOverlayPanelSizeAware`; other panels received
the override on their transparent wrapper. Forwarding the override alone left an
expanded panel centered inside a smaller window, reproducing the missing left edge.

The audit also found that native window size limits could crop enlarged panels,
size overrides could replace dynamic map/radar bindings, and font baselines could
be captured before role styles were resolved. Non-integer scaling could round the
host down while its content remained larger.

## Changes

- All catalog panels receive saved sizes on their shared presentation, as the editor does.
- Live windows measure that presentation instead of imposing a second set of width/height limits.
- Size overrides use reversible property values, preserving underlying XAML bindings and constraints.
- Surface survey dimensions follow its radar/tracker settings inside the shared presentation.
- Typography applies after attachment and reapplies when a panel moves between native and combined hosts.
- Size changes keep targeting the visible presentation after the combined host replaces its source-window content with a placeholder.
- Scaling preserves fractional layout dimensions instead of rounding the content and host separately.
- Human settlement placement uses measured window dimensions, including content-sized windows with automatic width/height.

## Live control interaction

The live drag handler received already-handled pointer presses and started a
window drag over buttons and scrollbar thumbs, taking away their mouse input.
Native and combined hosts now share a drag eligibility rule that recognizes
input controls and active map gestures. Passive panel areas remain draggable,
including handled presses from the FSS scroll content.

The rule accounts for Avalonia's implicit mouse capture to the clicked element;
the presence of capture alone does not establish an interactive gesture.
The [MouseDevice implementation used by this build](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.Base/Input/MouseDevice.cs#L128-L158)
documents that behavior.

`OverlayInteractionViewModelTests` exercises button clicks and scrollbar-thumb
drags through real native and combined interaction paths, followed by background
drags. It also checks handled passive content, custom capture, and the live mining
map with viewport interaction enabled and disabled.

## Executable coverage

`OverlayLivePanelSizingTests` renders real live window classes against their editor
presentations with the same simulated data. Its mapping covers every catalog entry
and fails if a newly added entry has no live-window mapping.

The 37 entries contain 61 representative states. Each state is checked in seven
scenarios (427 live/preview pairs):

1. Default settings.
2. A saved size at 80% of the catalog dimensions.
3. A saved size at 130% of the catalog dimensions.
4. Every text/icon role at its maximum +100% adjustment.
5. Overlay scaling at 1.75×.
6. Size and typography changed while the windows are already visible.
7. A saved size removed while the windows are already visible.

Each pair checks presentation dimensions and verifies that the live window contains
the presentation on every edge. Separate regressions cover the reported 764 × 276
Next-jump size, narrower/wider sizes, current binding values after reset, and moving
Next-jump into and back out of the combined overlay host without losing typography
or ignoring subsequent size changes.

### Catalog inventory

| Overlay | Identifier | States |
| --- | --- | ---: |
| Biology sample status | `PlotBioStatus` | 4 |
| System biology | `PlotBioSystem` | 3 |
| Body information | `PlotBodyInfo` | 1 |
| Colonization commodities | `PlotBuildCommodities` | 1 |
| Flight warning | `PlotFlightWarning` | 4 |
| Notifications | `PlotFloatie` | 1 |
| Ground combat | `PlotFootCombat` | 1 |
| FSS body feed | `PlotFSS` | 1 |
| FSS information | `PlotFSSInfo` | 1 |
| Galaxy Map system intelligence | `PlotGalMap` | 1 |
| Mining notifications | `PlotMiningNotifications` | 1 |
| Mining cargo hold | `PlotMiningCargo` | 1 |
| Mining Ref | `PlotMiningReference` | 1 |
| Firegroups | `PlotMiningFiregroups` | 1 |
| Mining rig range warning | `PlotMiningWarning` | 1 |
| Mining overview map | `PlotMineMap` | 1 |
| Surface mining survey guide | `PlotSurfaceMiningSurvey` | 5 |
| Surface mining | `PlotSurfaceMining` | 1 |
| Surface survey | `PlotGrounded` | 1 |
| Guardian site | `PlotGuardians` | 1 |
| Guardian status | `PlotGuardianStatus` | 8 |
| Guardian system | `PlotGuardianSystem` | 1 |
| Human settlement | `PlotHumanSite` | 1 |
| Next-jump information | `PlotJumpInfo` | 1 |
| Fleet carrier route | `PlotFleetCarrierRoute` | 3 |
| Route bodies | `PlotRouteBio` | 1 |
| Massacre missions | `PlotMassacre` | 1 |
| Mini tracker | `PlotMiniTrack` | 1 |
| Multiple Commander indicator | `PlotMultiGameCommander` | 1 |
| Prior scans | `PlotPriorScans` | 1 |
| Journal activity and SCO status | `PlotPulse` | 4 |
| Quest indicator | `PlotQuestMini` | 1 |
| Ram Tah guidance | `PlotRamTah` | 1 |
| Spherical search | `PlotSphericalSearch` | 1 |
| Station information | `PlotStationInfo` | 1 |
| System status | `PlotSysStatus` | 1 |
| Ground target | `PlotTrackTarget` | 1 |

### Auxiliary surfaces

The Guardian and mining zoom controls retain matching control/window dimensions;
the mining zoom window already measures its content. The surface mining alignment
helper is a deliberately fixed line rather than a data panel. Stream and combined
windows are game-sized canvases: existing projection/coordinator tests cover their
hosting and clipping, and the new combined-host regression exercises reparenting
an actual Next-jump presentation.

## Running the checks

```console
dotnet test tests/SrvSurvey.Desktop.Tests --configuration Release --filter FullyQualifiedName~OverlayLivePanelSizingTests
pwsh ./tools/Test-ChangedCodeQuality.ps1
```

## Validation results

- Release solution build with analyzer warnings treated as errors: zero warnings and zero errors.
- All 427 live/preview comparisons passed, including the existing FSS panels.
- The saved Next-jump size and combined-host regressions passed; the 764 × 276 live and editor images were also inspected.
- Focused live-interaction, managed-drag, and mining-map tests: 58 passed.
- Desktop suite: 2,746 passed, three platform-specific tests skipped.
- Localization tests: 19 passed; all seven source catalogs verified.
- CSharpier and the changed-line .editorconfig/local SonarCloud-profile gates passed.
- Changed production-code coverage, including live interaction and surface command shortcuts: 90.5% (134 of 148 line/branch points), above the 80% gate.

These are headless Avalonia layout/rendering checks. They do not emulate every
native compositor, multi-monitor arrangement, or content combination, and a panel
intentionally resized smaller than its contents can still require scrolling.
