# Behavior-focused validation

Use this map before adding another regression test. Protect outcomes at the module
interface; source inventories are navigation and lint, not proof that a feature works.
Keep fixture values independent of production calculations.

| Behavior worth protecting | Existing test surface and important cases |
| --- | --- |
| Captured uploads survive restart and failures | Core `JournalUploadScenarioTests`, `JournalUploadOutboxTests`, `JournalUploadStealthTests`: owner isolation, bootstrap versus live records, consent, successful acknowledgement, cooldown, crash/restart, corrupt/torn records, checkpoints, docking release. The mandatory scenario matrix remains a separate gate. |
| Upload completion and bounded shutdown | Core `VoxStellarPublisherTests`: in-flight and buffered uploads finish before completion returns, no new events after completion, canceling a completion wait leaves accepted transport alive, revoked queued work cannot send, failed work stays retained, synchronous application shutdown remains bounded. Observe transport completion rather than diagnostic prose or a short negative sleep. |
| Mining session totals match cargo | Core `MiningCargoJournalRegressionTests`: supplied real journal, six independent session totals, refinery carryover, cargo reconciliation, duplicate physical records, closing/reopening. Preserve the fixture and literal expected totals. |
| Mining search results survive travel and failures | Desktop `MiningSearchViewModelTests`, `SurfaceMiningSearchViewModelTests`, Core `MiningSearchSessionTests`: jump during/after search, force-include, edited filters during requests, offline cache restore makes zero HTTP requests, failed refresh preserves both the current readout and last successful saved results. |
| CAPI/journal freshness and carrier identity | Desktop `DedicatedCarrierIdentityTests` and commander/carrier view-model regressions: request timing, later journal events versus older fetches, later fetches versus earlier journal events, ambiguous carrier events, personal/squadron separation, only one current ship. |
| Exploration and project totals | Core `BoxelSurveyStatsStateTests`, `ExplorationValueCalculatorTests`, `ColonizationPlannedProjectsTests`: independent literal high-metal-content and water-world scan/current/mapping values, star exclusion, no double counting, stale stored rewards ignored, known catalog cargo transferred with independently worked totals. |
| Market stock eligibility | Desktop `ColonizationLocalMarketTests`: the real default provider and workspace share a fixed clock, exact age cutoff and one tick either side, future timestamps, project systems, pad/arrival/provider restrictions, allocations and carrier cargo, insufficient stock, restore, failed provider calls. |
| Overlay interaction and appearance | Desktop `OverlayResizeInteractionTests`: shared resize families at natural/configured sizes and small/default/large scales, released grip stops resizing, exact mission counts remain readable in narrow panels. `OverlayLivePanelSizingTests` retains all-panel/state runtime/editor parity; theme tests retain all six schemes. |
| Overlay visibility and shortcuts | Desktop `DesktopRuntimeTests` exercises real runtime dispatch and `DesktopOverlayController`: colonization priority is ready before overlay construction, colonization show/hide persists through docking, covered groups update saved settings, combat priority applies only to a presented colony panel, suits spare combat while manual/session suppression still applies, and retired controllers cannot overwrite an active session. Preference-only fixtures use their own registry. Runtime lifetime tests retain actual shutdown ownership and ordering contracts. |
| Portable journey reports | Core `JourneyReportTests`/`JourneyRouteMapTests`: statistics, escaping, traversal rejection, coordinates, negative Z, reference points, real journey legs. Desktop `JourneyReportBundleExporterTests`: originals, bounded previews, atomic failure cleanup, relocation. Browser cases cover both viewport widths, map switch, dated return-visit chooser, history selection, gallery enlargement/keyboard navigation/focus, and local assets with network disabled. |
| Real external platform integration | DBus protocol tests use an isolated fake portal. The installed portal probe is explicitly opt-in. Native OpenXR frame protocol tests and the Gamescope smoke scenarios remain separate real-platform checks. |
| Route scrollbar clearance | Desktop `RouteWaypointRemovalPresentationTests`: force both panes to overflow and measure content, headers, and row actions against the visible scrollbar boundary. Do not assert a specific padding literal. |

## Lanes

- `pwsh ./tools/Test-ChangedCodeQuality.ps1` retains the local formatter,
  localization, style, full Sonar-profile warnings-as-errors build, measured 80%
  changed-production coverage, and mandatory journal scenarios.
- Linux PR validation adds `-FullTestSuite -ResultsDirectory artifacts/TestResults/quality`.
  This runs the full solution once, including ReplayController and localization
  tests, retains TRX/OpenCover/hang diagnostics, and applies the same gates. A
  nonempty output directory is rejected so old results cannot satisfy a gate.
  Windows still runs its own full solution and native checks.
- Browser tests use the pinned `Microsoft.Playwright` test dependency. Build the
  Desktop tests, then run:

  ```sh
  PLAYWRIGHT_SKIP_BROWSER_GC=1 pwsh tests/SrvSurvey.Desktop.Tests/bin/Release/net10.0/playwright.ps1 install --only-shell chromium
  SRVSURVEY_RUN_BROWSER_TESTS=1 dotnet test tests/SrvSurvey.Desktop.Tests --configuration Release --no-build --filter Category=Browser
  ```

  Linux CI installs Chromium dependencies and runs this lane explicitly. Normal
  .NET test runs report these browser cases as skipped; they never download a
  browser or reach a live service as a test side effect. Each case exports and
  relocates its own real report folder, deletes the original image, and runs
  Chromium offline.
- Set `SRVSURVEY_RUN_PLATFORM_SMOKE=1` to opt into the installed DBus portal probe.
  This requires a suitable Linux desktop session; it is not a hermetic unit test.
- `Category=SourceContract` identifies the audited overlay inventory and route
  markup lint. File existence and markup shape must not be reported as behavioral
  coverage. Shared pipeline source-token checks and exact gutter-value checks
  were removed; runtime presentation and interaction tests protect the outcomes.

## Mutation checks

Before accepting a new/replacement behavior check, introduce a small, deliberate
production fault, run the focused test, require its relevant assertion to fail,
then restore the source and rerun green. A compilation error or changed expected
value is not mutation evidence. Keep the faults temporary; never commit them.
Record the checked faults and outcomes with the change's validation evidence.

### Verified targeted faults (2026-10-31)

The focused assertions failed for each temporary fault below, and the source was
restored after each run. Browser behavior is exercised at both widths; the
follow-up faults target return selection at desktop width and a corrupt second
image at narrow width independently.

| Deliberate fault | Assertion that detected it |
| --- | --- |
| Upload completion returns before its blocked HTTP operation finishes | `CompletionWaitsForAcceptedWorkAndStopsAcceptingNewWork` |
| A row exactly at the age cutoff is marked outdated | `MarketAgeIncludesTheExactCutoffAndFlagsOnlyOlderRows` |
| Restoring a saved search launches another provider query | `PowerplaySearchRowsReturnFromDiskWithoutAProviderRequest` (zero-request assertion) |
| A failed acquisition refresh overwrites the saved snapshot | `AcquisitionRetainsProviderFailureInItsFinalStatus` (restored target) |
| Mission progress labels are emptied | `NarrowMassacrePanelRetainsMissionCounts` (literal count text) |
| Colony-panel priority is ignored | `CombatSuppressionPreferenceAppliesOnlyWhileTheColonyPanelIsPresented` |
| Suit suppression incorrectly suppresses combat | `SuitSuppressionKeepsCombatCoordinatorEligibleWhileManualAndSessionSuppressionStillApply` |
| Calculated scan value is replaced with zero | `IngestSystemFileRecomputesValuesAndIgnoresReward`, `StarsAreExcludedFromValueAndHistogram` |
| Planned remaining cargo is off by one | `AsteroidPlanUsesCatalogCargoAndCannotBecomeALiveProjectThroughSerialization` |
| The browser gallery never opens | `RelocatedJourneyWorksOfflineWithReturnVisitsAndScreenshotNavigation` |
| The coincident-visit chooser never opens | `RelocatedJourneyWorksOfflineWithReturnVisitsAndScreenshotNavigation` |
| Retired overlay controllers remain subscribed | `RetiredControllerCannotOverrideTheActiveSessionsOverlayPreferences` |
| Overlay routing consumes shortcuts owned by another workspace | `UnrelatedShortcutsRemainAvailableToTheirWorkspace` |
| Completion exits before the second buffered upload | `CompletionWaitsForAcceptedWorkAndStopsAcceptingNewWork` (two successful transports) |
| Canceling the completion wait cancels accepted HTTP work | `CancelingCompletionWaitKeepsTheAcceptedTransportAlive` |
| Water-world reward constant is off by one | `IngestSystemFileRecomputesValuesAndIgnoresReward` (independent water-world vector) |
| Provider validation advances the injected clock by 30 days | `DefaultProviderUsesTheWorkspaceClockForQuoteAge` |
| Runtime startup omits initial overlay priorities | `StartupAppliesColonizationPriorityBeforeOverlayConstruction` |
| Runtime skips overlay-controller shortcut dispatch | `ComposedRuntimeDispatchesTheCoveredGroupsShortcutAndPersistsIt` |
| Retired controllers retain the behavior-change subscription | `RetiredControllerCannotOverrideTheActiveSessionsOverlayPreferences` (retired shutdown change) |
| Preference fixture uses the shared registry and leaks its session gate | `CollapseCoveredGroupsShortcutUpdatesAndPersistsDisplayStyle` (shared host eligibility) |
| Either route pane expands its content into the scrollbar | `ScrollableRouteContentAndHeadersStayClearOfTheVerticalScrollbar` (both panes) |
| Second return option navigates to the first visit | `RelocatedJourneyWorksOfflineWithReturnVisitsAndScreenshotNavigation` (desktop visit 3) |
| Relocated second original image is corrupt | `RelocatedJourneyWorksOfflineWithReturnVisitsAndScreenshotNavigation` (narrow image load) |
