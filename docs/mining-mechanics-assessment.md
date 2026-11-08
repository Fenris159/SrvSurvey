# Mining mechanics and workspace assessment

Research checked **2026-11-08 UTC** (2026-11-07 in America/Chicago). This is an evidence review and source audit, not an implemented feature change. Workspace source inspected at `9e75bc2aa032db6c4eff37ab966819793548ef83`, merged through PR #269 as `abc110953c78ca9cae5c9e118a3bd6486c768ad3`.

## Evidence and currency

The linked [EliteMiners PSA](https://www.reddit.com/r/EliteMiners/comments/os2ldn/psa_the_current_state_of_mining_and_other_useful/) was created in 2021 but explicitly reports **last revision 2026-09-07**. Its current text includes Powerplay 2.0, Type-11 Prospector, Rhino surface mining, and warnings about obsolete asteroid maps. Treat it as a maintained community index, not an untouched 2021 guide. Its linked experiments provide stronger support for individual mechanics than the index alone.

Evidence below is distinguished as **official** (Frontier's publisher announcements), **community measurements** (the original experimenters and catalog maintainers), or **inference/recommendation**. Frontier's website/forum returned access errors during this check; Frontier's own Steam publisher news was readable. No current server-side mining formula is publicly established by the sources checked.

## Mechanics that should guide the workspace

### 1. Method, ring, and demonstrated yield matter more than hotspot count

**Community measurements.** Metallic and Metal Rich rings are different mining targets. In particular, a Platinum hotspot in a Metal Rich ring is not evidence that Platinum can be laser-mined there. The [original location guide](https://www.reddit.com/r/EliteMiners/comments/mz3m0g/psa_how_to_find_a_good_mining_spot/) recommends pristine Metallic rings for conventional valuable laser mining and Rocky rings for valuable cores. Reserve quality should therefore be method-sensitive rather than a universal prerequisite.

The PSA's linked [commodity availability table](https://www.reddit.com/r/EliteMiners/wiki/mininglocations/) explicitly lists Platinum and Painite as core targets in Metallic/Metal-Rich rings and laser targets in Metallic rings; Low-Temperature Diamonds and Bromellite support both core and laser in Icy rings. Osmium supports laser mining in both Metallic and Metal-Rich rings. Model availability as **commodity × ring × method**, not mutually exclusive core/non-core commodity categories. The wiki's strategy introduction contains old overlap advice; the availability table is the specific evidence used here.

Avoid replacing the old overlap obsession with the equally absolute claim that overlaps do nothing. **Official:** Frontier's [2020 Tritium efficiency announcement](https://store.steampowered.com/news/posts/?appids=359320&enddate=1597755664&feed=steam_community_announcements) says single hotspots provide a substantial frequency bonus and overlaps remain more lucrative at reduced levels. **Community measurements:** [ED_Churly's comparison](https://www.reddit.com/r/EliteMiners/comments/n1jioz/revisiting_hotspot_performance/) found 55 minutes for one triple versus 60 minutes for a single in that ring, but 54 minutes in another ring's good single. That experiment supports giving measured ring performance more importance than overlap count; it does not establish universal bonuses for every overlap.

**Recommendation:** include good single hotspots; display observed average content, sample size, location/method, provenance, and observation age. Keep the existence of several hotspots distinct from measured geographic overlap. Present insufficient evidence as unknown rather than inventing a yield estimate.

### 2. RES is a local, method-specific opportunity with a risk cost

**Community measurements.** The original [RES experiment](https://www.reddit.com/r/EliteMiners/comments/if859b/mining_in_a_high_res_inside_painite_hotspot/) distinguishes increased fragment counts from increased fragment content. Its later author replies describe the 20 km RES radius and explain that higher tons per asteroid does not guarantee higher tons per hour. The [Platinum RES survey](https://www.reddit.com/r/EliteMiners/comments/wi5c5n/platinum_hotspots_and_res_combinations_survey/) reports approximately +100% Hazardous, +75% High, +45% Regular, and +20% Low yield for the traditional laser-mining setup. These are historical empirical averages, not guaranteed universal multipliers for every new mining module.

**Recommendation:** distinguish RES grades and hazard/security tradeoffs. A RES annotation somewhere in a ring should not imply that the player is inside its bonus radius. Explain the local benefit and keep a user-confirmed site/method context unless reliable position evidence exists. Do not apply the laser bonus to core mining automatically.

### 3. Prospecting statistics need explicit denominators

**Community research.** The [Platinum Hotspot Survey](https://www.reddit.com/r/EliteMiners/comments/o1khyi/platinum_hotspot_survey/) asks for average/max Platinum content together with the number of prospected asteroids, calls for roughly 100–150+ prospects, discusses sampling variation, and warns about duplicate prospects. Its later notes say duplicate handling changed in Mining Analyzer. The [original RES experiment](https://www.reddit.com/r/EliteMiners/comments/if859b/mining_in_a_high_res_inside_painite_hotspot/) reports both 61 prospects and 26 Painite-containing rocks and proposes refined tons per 100 prospects as a useful comparison alongside personal tons/hour.

**Recommendation:** label conditional mean content separately from mean across all prospects. Also show target hit rate, zero-hit count, sample size, distribution/threshold hit rate, and duplicate policy. A good average among the few successful hits is not sufficient evidence of a productive random-prospecting location. Account for cherry-picked mapped runs separately from blind prospecting.

### 4. Cores, asteroid maps, and reset claims require separate treatment

**Community evidence.** The [original core-hotspot explanation](https://www.reddit.com/r/EliteMiners/comments/lkom61/psa_core_mining_and_hotspots/) says hotspots change the mix of core commodities, not the number of cores; reserve level does not provide the laser-style advantage. Its discussion describes cores as shared across instances. The maintained [map index](https://www.reddit.com/r/EliteMiners/comments/lczq67/crowdsourcing_request_mining_maps/) marks old Omicron Capricorni B routes defunct after Update 17 while also listing a rebuilt route. RES presence and an actual asteroid-by-asteroid map are separate columns in that catalog.

**Recommendation:** distinguish cores prospected from cores cracked/mined. Store map author, route link, Live/Legacy/platform applicability, last verification, and known invalidation. Do not mark a ring as mapped solely because it has RES metadata, and do not reject the whole ring because one old route stopped working. Treat exact replenishment predictions as method-specific estimates with provenance, not a generic fixed timer.

### 5. New ships/modules make universal legacy formulas unsafe

**Official.** Frontier's [29 September 2025 Type-11 update](https://store.steampowered.com/news/posts/?appgroupname=Elite%3A+Dangerous&appids=359320&enddate=1763037221&feed=steam_community_announcements) introduced a new repeater with higher-density chunks and a faster, larger-capacity mining multi-limpet controller. Frontier's [20 October 2026 publisher notes](https://store.steampowered.com/news/posts/?appids=359320) add an **Anaconda Mk II with Mk II Mining Volley Repeater**. Consequently the PSA's September wording about a T-11-exclusive repeater is no longer a sufficient description of the current equipment landscape.

**Community guidance.** The PSA cautions against using C-rated mining multi-controller prospectors for conventional laser yield, and against mixing ordinary mining lasers with the T-11 repeater. Its linked [prospector facts](https://www.reddit.com/r/EliteMiners/comments/jordsf/basic_mining_facts_prospector_limpet_information/) describe the traditional A-rated yield multiplier. Preserve this useful warning while scoping exact numbers and mixed-weapon claims to the tested controller/repeater version.

**Recommendation:** contextual outfitting advice, with detected equipment and model/version shown where available. Do not hardcode “all repeaters are T-11 only” or reuse a conventional laser estimate for Mk II equipment without validation. Favor actual refined output as the outcome metric.

### 6. Surface mining remains relevant and benefits from persistent local survey records

**Official:** Frontier's [2 September 2026 Rhino notes](https://store.steampowered.com/news/posts/?appids=359320) add DSS Planetary Mining Locations, deposit capacity/replenishment/weighting, and balance changes to the Rhino's extraction, density, recharge, and temperature behavior. That section describes the new surface loop; it is not evidence of a universal rebalance to ring mining. The October notes also fix missing FSS resource information and a Rhino cargo-jettison disconnect.

**Current original field evidence:** [Dunn Actual's 24 October guide introduction](https://inara.cz/elite/logbook/98900/) distinguishes remaining Amount, Density, measured footprint, tested rig fit, and practical service routes. Six rigs are not automatically better than five if extra driving leaves the rest idle. It distinguishes collection-phase production from full-trip earnings, and reports replenishment without establishing a fixed weekly reset or that a return to High Amount means complete restoration. It explicitly includes SrvSurvey among mapping tools. The [linked original field-guide thread](https://www.reddit.com/r/EliteMiners/comments/1wa8nca/rhino_planetary_mining_field_guide_what_weve/) records uncertainty rather than a single authoritative refill timer.

**Recommendation:** keep survey geometry, deposit bookmarks, tested rig positions, collection cadence, depletion/revisit history, and actual production useful. Show “last observed” and uncertainty; do not turn Density into guaranteed rig count/tonnage or a fixed Thursday reset. Include refinery carry when interpreting short production samples.

### 7. Price, demand, freshness, and origin all matter

**Maintained community guidance:** the PSA still warns that cargo large relative to station demand can reduce a high-value mineral's offered price. It gives a rough 5–10% range, not a public exact universal price formula. Static headline prices or mining-only tons/hour do not establish realized credits/hour.

**Primary tool documentation:** [Ardent's API](https://github.com/iaincollins/ardent-api/blob/main/README.md) supplies buy/sell orders with volume, price, Fleet Carrier selection, and age filters; its import/export default is 30 days. [Ardent's own About page](https://ardent-insight.com/about) identifies EDDN as its live player-driven source. [Spansh's author](https://www.patreon.com/cw/spansh) describes body/signal searches, stations, market data, and nightly dumps. [EDTools](https://edtools.cc/hotspot) exposes a curated hotspot finder. These are complementary sources, not interchangeable evidence that a site has good measured yield or a verified asteroid route.

**Powerplay primary tool author:** [Merit Miner's update](https://www.reddit.com/r/EliteMiners/comments/1imkvog/meritminercc_update_2_new_search_options_3d_map/) and [current search UI](https://meritminer.cc/) distinguish Acquire/Reinforce/Undermine, Powers, mining type, commodity, and market age/demand. The author warns that some map snapshots may lag details. The current Rhino guide separately warns that a profitable sale can earn no merits if origin/destination rules fail.

**Recommendation:** show cargo-to-reported-demand risk and timestamp, refresh sale choices, distinguish quoted gross value from recorded actual sales, and report full-trip time. Preserve optional Powerplay-specific routing and explain each condition; do not infer merit eligibility from price alone. Keeping Spansh/Ardent searches and contributing to EDDN does not require restoring local EDDN reception/storage.

## Limits

- Prices, Powers, ship modules, and API data change; this note is dated evidence, not a permanent live-data guarantee.
- Traditional RES/prospector formulas are original community measurements. The sources checked do not validate those exact coefficients for every current Mk II module.
- Community map and hotspot samples have varying dates, selection effects, and sample sizes. Do not rank an unknown observation as verified.
- Some official website links and the full Google Docs export were inaccessible. The publisher's own Steam copy and the field-guide author's dated public summary were read directly; no unseen guide chapter is treated as verified.

## Audit of the actual workspace

These are source observations, independent of the external mechanics evidence above. No application behavior was changed or exercised in-game for this assessment.

| Existing capability | Assessment | Recommended refinement |
| --- | --- | --- |
| Session controls, confirmed cargo, active t/hour, refinery carryover | Keep. These measure outcomes better than a mineral's advertised selling price. | Label active mining time distinctly from whole-trip time; preserve the existing confirmed/estimated yield distinction. |
| Prospector overlay, thresholds, core announcements, audio | Keep. These support quick decisions during mining. | Use method-aware presets; retain core alerts independently of laser percentage thresholds. Clarify that engineering material content is separate from commodity concentration. |
| Rings and Platinum searches | Keep, but correct ranking and extraction-method classification first. | Include good single hotspots, expose evidence and sample age, and distinguish multiple hotspots from geographic overlap. |
| Ardent/Spansh markets | Already satisfies the core selling-search recommendation: price, demand and quote age are displayed; pad, station and distance filters exist. | Optional cargo-to-demand context only. Actual sale attribution is a separate, larger enhancement. |
| Powerplay and mission planning | Keep as optional objectives. Existing Powerplay already has mining-type, pledge, objective, and destination controls. | Explain origin/destination eligibility and track confirmed outcomes where evidence permits. A plan is not proof that a particular cargo batch will earn merits. |
| Bookmarks, map notes, imported history and reports | Keep. Local records of the commander's own activity are useful and distinct from the removed EDDN reception cache. | Record provenance, method, map links/version, verification date, and observed site performance. |
| Rhino Surface Hunt, survey geometry, saved deposits, rig planning | Keep. These address the current surface-mining loop. | Separate Amount, Density, observed production, and estimated/tested rig fit; date deposit observations and make revisit reminders explicitly uncertain. |
| Reference and ancillary trader/system tools | Keep reference accessible from decisions; secondary searches need less prominence. | Prefer contextual guidance over more top-level controls. Static or catalog-wide prices should not imply a currently available sale. |

### Accuracy changes with the highest value

1. **Replace binary mineral-method classification.** `MiningSearchViewModel.MatchesMiningType` (`:1484`) treats a mineral as either core or non-core using `CoreMinerals` (`:1524`). That set omits Platinum, Painite and Low Temperature Diamonds, despite `MiningRingReferenceViewModel.All` (`MiningWorkspaceViewModel.cs:1867`) listing them in both applicable columns. Bromellite is in the core set even though it is also laser-minable. `MatchesSelectedMiningType` (`:1500`) also distinguishes only core from other non-planetary types, although the UI offers laser, surface-deposit and subsurface-deposit choices. A shared, evidence-backed **commodity × ring × extraction method** matrix should drive both reference and filtering. Reference availability is not evidence that a particular ring has already been surveyed for a deposit.

2. **Rework Platinum Spots++ ranking.** `PlatinumSpotRowViewModel.Create` (`MiningSearchViewModel.cs:3422`) uses `10 × hotspot count + 30 if any RES + 20 if any overlap + 10 if Pristine`. The default mode (`:1219`) excludes rings with a single hotspot unless a RES or overlap annotation exists. This can exclude a productive single and rewards additional hotspots even when they are geographically separate. Prefer explicit options such as nearby compatible sites, measured high-yield sites, and RES sites; show an explainable rank rather than a universal productivity score. `MiningMappedSpotCatalog` already contains structured `YieldPercent` data, but production use only exposes its descriptive note through `PowerplayMeritRank`; the numeric measurements do not drive the Platinum score. Treat historical measurements as dated evidence, not live guarantees.

3. **Separate RES presence from a verified route.** The “RES mapped” result mode currently checks only `HasRes`, which means nonempty `ResourceExtractionSites` text. It does not verify an asteroid map, its validity, or the player's location inside the RES. Rename that result set to RES sites, and represent an actual map using its route link, author, version and verification status. Add useful RES-grade guidance without making an automatic claim about an unmeasured player's position.

4. **Make reporting denominators visible.** `MiningSession.Summarize` (`MiningSessionTracker.cs:85`) averages only reported occurrences of each mineral; absent-mineral prospects are excluded. Keep that useful conditional average but label it “average in matching rocks,” and add target-hit rate, overall prospect mean, threshold-hit rate and sample count. For example, two 50% hits among ten prospects produce a 50% matching-rock mean, a 20% hit rate, and a 10% all-prospect mean. These are different descriptions of the same sample. `CoreHits` (`:77`) counts prospector core detections, not confirmed extraction. `Asteroids` counts recorded prospects, so report tons per prospect with an explicit duplicate policy rather than implying tons per confirmed mined rock. Mapped and random prospecting samples should remain distinguishable.

5. **Add whole-trip economics without guessing cargo provenance.** `MiningSession.TonsPerHour` (`MiningSessionTracker.cs:79`) uses active duration and excludes paused time. Session travel handling can end the run before the sale, and session/report models do not currently attribute `MarketSell` receipts. Add optional trip-level elapsed time and sale receipts, with expenses and mixed/previously owned cargo handled explicitly. Reuse the existing market demand/age filters (`MiningMarketQuery`) to show risk for the intended commodity quantity. Keep a forecast visibly separate from confirmed credits and merits.

### Complementary improvements

- Contextual equipment checks could use the existing Loadout stream: for traditional laser mining, warn about missing or lower-rated prospectors; show repeater-specific advice only for detected, verified equipment. `MiningWorkspaceViewModel.ApplyContext` currently extracts ship identity/cargo capacity but does not implement this mining equipment check. This does not require a ship-build editor or ship presets.
- Add map/deposit evidence fields to existing bookmarks and survey records. `MineMapMarker` already has Amount, Density, RigCount and CreatedAt; survey-level UpdatedAt is not equivalent to a separately observed refill or tested rig-capacity history.
- Group guidance around choosing a location, prospecting/extracting, and selling/reviewing. The current tabs contain the useful pieces; more tabs are unnecessary for these refinements. Preserve the shared theme resources and responsive layouts.
- Link to the maintained PSA, original measurement guides, current surface field guide, and source-specific results. Keep outbound EDDN contribution and the existing provider searches; none of these recommendations requires reinstating local EDDN reception.

The recommendations above describe possible improvements, not a list of entirely absent features. The narrower follow-up scope below supersedes that initial implementation proposal. No new PR, release change or production edit was made by this assessment.

## Recheck: changes that fit the current implementation

Rechecked **2026-11-07 America/Chicago** after the user identified that market search already covers price and demand. `git fetch origin SrvSurvey-Avalonia` confirmed the base at merged commit `abc11095`; `git diff HEAD origin/SrvSurvey-Avalonia` was empty, so this audit covers the current remote application's source. Effort classifications describe implementation scope, not elapsed-time promises. Existing tests were inspected, not rerun: this recheck changes documentation only.

### Already provided; do not rebuild

- **Markets:** price, demand, quote age, nearby stations, volume/freshness/pad/station filters, plus actions from a selected Platinum spot into buyer search. The user is correct that the main requirement is met.
- **Platinum search:** an **All platinum** mode already includes single hotspots; results are sortable and already have a **Why it ranks** column. The gap concerns the default exclusion and score, not missing search or explanatory controls.
- **Session/Reports:** Finds, Quality hits, Average, Best, refined tons, core detections, active time, comparison charts and estimated/confirmed yield information exist. The HTML report already explains the conditional average in a tooltip. The UI explicitly labels Active time. Keep the current calculation and improve consistent wording; the existing average is not mathematically wrong.
- **Announcements:** named presets already save thresholds and core/non-core choices. Core alerts can remain visible despite percentage filtering. No additional preset system is required.
- **Bookmarks:** Average yield, Last mined, Hotspot name, RES, overlap, notes, ratings and screenshots already exist (`BookmarkCatalog.cs`, `BookmarksView.axaml`). They can hold practical evidence today.
- **Surface maps:** separate Amount, Density and manually entered RigCount are already editable and visible. Survey UpdatedAt is displayed. Do not duplicate these fields or describe their absence as a gap.
- **Ship data:** the application already owns a complete, invalidation-aware `ShipLoadoutState` and an offline `ShipModuleCatalog` with module ratings, mining controllers and newer repeater support. A mining equipment hint should consume that data, not introduce another reader or outfitting model.

### Recommended bounded implementation

| Change | Existing integration point | Scope and limits |
| --- | --- | --- |
| Correct laser/core compatibility | Shared factual catalog used by the existing Reference and mining-method filters | **Small–medium.** Put commodity/ring/laser/core eligibility in Core and consume it from the current views. No new provider or storage is required. A complete validated surface/subsurface matrix is a separate extension: present controls alone do not supply that evidence. |
| Refine default Platinum results | `MatchesPlatinumMode`, `PlatinumSpotRowViewModel.Create`, existing result-set picker and explanation column | **Small.** Include compatible singles in the normal result set, keep user-requested overlap/minimum-count filters, avoid presenting hotspot count as demonstrated yield, and rename RES mapped to RES sites. All platinum already supplies the broader result set. |
| Clarify report statistics | Existing Session cards, prospect summary, overlay text and HTML report | **Small.** Use Core finds, Prospected asteroids and Average in matching rocks (or a compact heading with tooltip). Keep existing average/CSV property names and active-time accounting. One optional hit-rate statistic can be computed from recorded prospects; do not infer missing observations for summary-only imports or use manually adjusted totals as a sampling denominator. No schema migration is needed for a derived display value. |
| Extend RES guidance | Existing Resource Extraction Sites expander in Reference | **Small.** Add the method/local-radius yield context beside the current risk explanation. No position tracker, bonus predictor or additional panel is required. |
| Add optional equipment hints | Existing complete ship loadout and offline module catalog; existing Session/Reference surface | **Small–medium.** Show factual equipped-controller/rating information and scoped guidance. Preserve unknown status for missing/stale loadouts. Equipped modules do not prove which controller the player fired, so avoid an unconditional claim that a lower-rated module is being used. |
| Refine surface observation context | Existing marker editor, survey list and bookmark notes | **Optional small–medium.** Tooltips can explain the existing fields immediately. A future per-deposit Last observed or Estimated/tested rig-count flag is additive metadata; an old creation/survey-edit timestamp must not be relabeled as an actual observation. Existing fields and dates cover the basic need. |

The strictest low-impact first pass is **laser/core filter correction + Platinum default/RES wording + report label/tooltip consistency + a short RES reference addition**. A percentage hit-rate display is a modest optional extension using existing journal observations. Detailed historic yield ranking should wait until observation provenance/sample comparability are sufficient; the bundled YieldPercent values lack a universal current-confidence measure.

### Defer beyond this first pass

- Whole-trip profit and sale/cargo attribution across multiple sessions or carrier transfers.
- A new map-verification registry, comprehensive deposit observation history or automatic replenishment timers.
- Automatic RES-boundary or guaranteed per-asteroid yield prediction.
- A replacement mining workspace, additional top-level tabs, new public-data cache or EDDN reception.

These are optional larger features, not prerequisites for the accuracy fixes. Retain the existing shared theme resources and responsive controls for all UI refinements.

## First-pass design and implementation brief

Authorized scope: laser/core eligibility, Platinum inclusion/order and RES wording,
statistics wording, and RES guidance. The existing tab and panel layout is retained.

Design tokens use the active theme, with Monochrome values as a reference:
window `#0A0A0A`, panel `#141414`, raised panel `#1C1C1C`, text `#EDEDED`,
secondary text `#A3A3A3`, accent `#E6D59A`. Use existing DynamicResource brushes,
never these reference values directly. Retain Rajdhani headings and the current
Inter body/table typography and numeric alignment.

```text
Session                       Reference > Resource Extraction Sites
[existing active readouts]    [existing wrapping risk explanation]
Prospecting yields            Laser yield       Confirm the location
[short muted definition]      [wrapping text]    [wrapping text]
[existing sortable table]     [columns stack in compact windows]
```

The informational emphasis is the relationship between a prospect and its content,
not another decorative card. Definitions sit with the values they explain; RES
instructions stay inside the existing disclosure. Review against the brief removed
new charts, extra tabs and a new scoring badge: these would add visual structure
without enough new evidence. New wording uses sentence case, and the existing
controls retain keyboard focus and all active-theme colors.

## Implemented first pass — 2026-11-07

Implemented on `mining-accuracy-refinements`, based on merged mainline `abc11095`.
The shared `RingMiningCatalog` now supplies the existing Reference display and
laser/core search eligibility, including selected-commodity intersections for
Powerplay. Existing ring and commodity aliases are normalized. Deposit-method
availability is outside this factual laser/core table.

Platinum results include compatible single hotspots. Spots++ orders pristine
reserves first, then system/arrival distance; All platinum uses distance ordering.
Hotspot count no longer contributes an invented productivity score. Explicit
minimum-count and overlap filters remain available. RES sites replaces RES mapped,
with saved old selections normalized automatically. The result column is Site
notes, with the sorting policy explained above the current controls.

The current Session layout, overlay and reports clarify prospected asteroids,
core finds, active-hour rates and matching-rock percentage averages. Calculations,
persisted session fields and CSV column names remain unchanged. The existing RES
expander retains its wrapping introductory description and adds two guidance
blocks that stack in compact windows. All colors use the existing theme resources.
The localization extractor now includes the shared catalog, preserving translated
mineral names.

Release validation for RC77.10 (combined mining, commander and station-service changes):

- Strict Sonar-profile build, CSharpier, changed-line style and localization
  freshness checks passed.
- Full Core suite: **3,663 passed**. Final Desktop suite: **6,089 passed**, six
  platform skips and no failures. The required overall quality script passed.
  Earlier runs encountered intermittent failures in unchanged Linux voice timing
  and Exploration history teardown tests; both passed in the final full run
  without changing those implementations or weakening their assertions.
- The full quality script measured **97.7% (472/483)** new-code coverage using its
  changed-line and branch-point policy, above the 80% threshold.
- The retained results pass all **66 mandatory journal upload scenarios**;
  all **19 localization catalog checks** pass. The source catalog is current
  across **10,607 strings and seven languages**.
- Focused healthy checks: 28 Core and 15 workspace checks. Deliberate incorrect
  unknown-ring/report behavior caused two assertion failures; incorrect RES/core
  wording caused seven UI failures. Every mutated source was restored byte-for-byte.
- All six application themes were rendered at compact/wide reference widths;
  the new guidance wraps, stacks and uses the expected theme foregrounds.
  Session and light/dark Reference screenshots were visually inspected.

The first-pass implementation was followed by the authorized commander and Vista
Genomics changes. RC77.10 and the current release notes collect that work for
review; publishing a release or replacing the local installation is separate
from opening the PR.
