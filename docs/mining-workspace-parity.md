# Mining workspace implementation checklist

Reference: [EliteMining](https://github.com/Viper-Dude/EliteMining/tree/7524580059d23753f3583a6c03821983083740e2), pinned at `7524580059d23753f3583a6c03821983083740e2`.

This checklist maps the implemented workspace to the pinned reference. Validation status is recorded separately from gameplay verification. Source evidence comes from README, `prospector_panel.py`, `mining_statistics.py`, `mining_missions.py`, `ring_finder.py`, `marketplace_api.py`, `system_finder_api.py`, `report_generator.py`, `ring_guide_tab.py`, and `announcer.py`.

| Capability | Implementation / disposition | Validation / remaining work |
| --- | --- | --- |
| Start, stop, pause, resume, automatic prospector start | `MiningSessionTracker`, Mining → Session | Accounting and bootstrap tests pass |
| Persistent session recovery per commander | `MiningStore`, paused recovery | Restart and isolation tested |
| Prospector results, core hits, yields, quality thresholds | Session statistics and announcement filters | Manual asteroid and mineral quality-hit corrections retain source observations |
| Cargo and limpet counts | Existing `CargoInventoryState` snapshot | Shares main application inventory |
| Refined and collected notifications | `MiningWorkspaceState`, dedicated Mining panel | Separate groups; ship-only gate tested |
| Engineering raw materials and grades | Raw collection totals and grades 1–4 | Connected to collection events |
| Refinery bins | Manual refinery estimates, matching reference input | Kept separate from proven refined tonnage |
| Cargo-full idle reminder | One-minute full and idle check | One-minute delay, no repetition and commander-loss test passes |
| Auto-switch mining tabs | Session preference | Connected |
| Voice announcements and voice/rate/volume | Optional Windows SAPI worker and installed voice selector | Platform-limited; audible output not tested during development |
| Announcement presets and core/non-core filters | Commander-scoped settings and named presets | Separate from excluded ship presets |
| Firegroup configuration / overlay | Sidebar → Firegroups; independent Mining overlay | No game inputs or ship presets |
| Reports, notes, CSV, HTML, print/PDF | `MiningReport`, Mining → Reports | Encoding and totals tested |
| Material graphs, yield timeline, history comparison | Observed-yield and cumulative-refining SVG charts; per-material session comparison | Timed observations, encoded labels, totals and summary-only imports tested |
| Session screenshots | Report attachments | HTML embeds selected local images |
| Historical report import / batch management | EliteMining/SrvSurvey CSV import, all-session comparison, deletion/undo | Summary import retains fields without fabricating events |
| Bookmark CRUD, notes, rating | Shared catalog, Navigation → Bookmarks | Persistence and merge import tested |
| User categories and filtering | Shared Bookmarks workspace | Category and text filtering tested |
| Mining bookmark reuse | Same instance in Mining and Navigation | Connected |
| Overlap / RES annotations | Shared bookmark fields | Shared annotations participate in local/online overlap and RES filtering |
| Hotspot journal import | Scan / SAA handling | Bootstrap and live events connected |
| Historical journal import | Selected journal files, active-commander filtering | Existing parser; bootstrap suppresses auto-start |
| Online hotspot / ring-type-only search | Shared network client, Spansh bodies search | Live endpoint verified; ring-level filtering tested |
| Radius, ring type, mineral, minimum count, paging | Hotspot search controls | Connected |
| Local / online combined search and auto-search | Reference/local/Spansh merge, optional automatic refresh | Newer observations take precedence |
| Save online ring locally | Search result context action | Connected |
| Ring density and reserve enrichment | Reserve displayed and refreshed from online results | Obsolete numeric reference reserve values are not presented as reserve levels |
| Bundled historical hotspot / overlap / RES database | Pinned installation database plus overlap/RES CSVs | 43,221 observations, 16,402 consolidated rings; see mining-reference-data.md |
| Commodity buy/sell, nearby/galaxy-wide | Ardent, Spansh fallback on HTTP failure | Live Ardent price response verified |
| Commodity categories / selection | Mining and trade categories plus free-form entry | Reference commodity names retained |
| Pad, carrier, station type, freshness filters | Search controls and result checks | Client checks price age, supply/demand and requested station constraints |
| EDDN incoming market/power cache | Opt-in receive-only NetMQ worker, bounded persistent cache | Live receiver observed 797 commodity records; direction/unknown-coordinate/expiry tests pass |
| System filters / material traders | Shared network Spansh search extension | Live queries returned systems and raw material trader stations |
| Inara / EDSM / Spansh context links | Inara and EDSM system links, Spansh search, copy | Right-click actions use external-effect gate |
| Fleet Carrier status/cargo/finances/services/history | Fleet Carrier workspace below Overview; existing profile VM and carrier view | Same live profile; no duplicate monitor |
| Distance / home / carrier shortcuts | Travel → Distance; shared resolver and current/home/carrier shortcuts | Unknown origins report unavailability |
| Ring / mineral / RES reference | Mining → Reference | Ring compositions and RES guidance |
| Full backup / restore | Commander mining ZIP, shared bookmarks and screenshot assets | Portable attachments and recovery of edited bookmarks tested; previous files retained |
| Theme-aware workspace and overlays | Raven resources, shared presentation | All application themes rendered; populated Monochrome dark and Blue light inspected |
| VoiceAttack, Discord, PNG cards, ship presets | Explicitly excluded | No implementations added |
| Existing Rhino overlays | Protected; unchanged | New overlays require main-ship status |

## Module boundaries

Session accounting, mission allocation, and persistence live in Core. Desktop consumes the existing journal updates, cargo projection, and commander context. Searches receive the existing external-network client and shared system resolver. Navigation owns the shared location catalog. New ship overlays use the same presentation in live and editor windows through `OverlayPresentationSession`.

## Validation

Individual red/green tests cover session accounting, mission delivery/cargo allocation, bookmark persistence/import, backup recovery, replay idempotence, report encoding, ring-level filtering, and ship-only overlay gating. Focused Core and Desktop checks pass. The two-axis review and the corrections it produced are recorded in [the review report](mining-workspace-review.md).

## Runtime verification limits

Spansh systems/traders, Ardent prices and the receive-only EDDN adapter were checked against live public endpoints. Workspace/theme and shared overlay rendering used headless Avalonia; journal transitions and persistence used fixtures. A complete in-game ship-mining session and audible Windows speech have not yet been exercised. The offline HTML fixture was generated and chart data was tested, but browser policy blocked opening its local file URL for visual inspection. Existing Rhino overlay source files were not modified.

Final full solution validation: **3,453 passed, 0 failed, 0 skipped** — Core 1,447; Desktop 1,993; ReplayController 13. The initial run identified two inventory updates and the review added functional regression coverage; the final run includes those corrections. Release builds compile with warnings treated as errors. Both independent review axes report no unresolved findings. The navigation follow-up and startup fixes are recorded in [the navigation review](mining-workspace-navigation-review.md); the Desktop suite was rerun after those changes, while the unchanged Core and ReplayController results remain from this task's full solution run.

The existing Rhino/surface-mining overlay files have no diff against `e46fb9a3`. The new notifications and Firegroups panels are independent ship-only panels.
