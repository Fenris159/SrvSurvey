# Raven build preview

In Colonization → **My build projects**, select a project name to open its live,
read-only preview. Multiple builds can have their own windows open. Selecting the
same name again activates its existing window. Opening a preview leaves **Show**,
the project focus, and delivery tracking unchanged.

**Combined Build Report**, before **Open Raven builds**, opens the same window for
all workspace projects, including hidden projects. It sums cargo requirements and
delivery progress, counts shared fleet carrier stock once, and lists the included
builds, linked commanders and carriers, and system effects grouped by system.

The system selector to the left of **Refresh** starts at **All Systems**. Choose a
system to combine only its builds across every report section and the CSV export.
Switching systems uses the last complete snapshot without making extra API calls.
Refresh keeps the selected system while it has projects, otherwise returning to
**All Systems**. The filter leaves workspace **Show** and focus choices intact.

The window refreshes every 30 seconds and provides an immediate **Refresh** button.
Closing it stops its refreshes and cancels pending reads. Disabling Raven access or
changing the active commander closes all build previews and the combined report.
Combined reports read projects sequentially and retain the last complete snapshot
if any project cannot be refreshed.

Cargo rows show remaining requirements, each linked carrier's stock, and the signed
**FC Diff**. A positive value is surplus and a negative value is a shortage. Surplus
of one material never offsets another material's deficit. Carrier totals count each
commodity once even when Raven returns both its journal and display names. Missing
carrier data is shown as unknown. A failed refresh retains the last successful
snapshot; its status and update time make that clear.

**Ready on fleet carriers** uses a segmented bar with a wrapping carrier legend
in individual build previews, combined reports, and Nexus reports. Each legend
entry gives the carrier name and its usable tonnes toward the current report.
Shares are capped per commodity at the remaining requirement and divided
proportionally between carriers holding that commodity; surplus never inflates
the total. Whole-tonne rounding preserves the exact ready total. Missing cargo
data stays unknown rather than producing a misleading colored share.

Carrier colors are generated from their market IDs, with no ten-carrier limit.
The same carrier keeps its hue across windows, refreshes, reordered results, and
system filters. Swatches adjust brightness for light and dark application themes,
and names and quantities remain available without relying on color alone.

The carrier manifest editor also groups journal and display aliases into one
editable row. When their stored amounts disagree, it initially retains the larger
amount instead of adding the copies; the independent **Local** column remains
available for comparison. Saving writes the edited amount under the journal key
and clears the old aliases, including aliases of removed commodities. CAPI cargo
synchronization uses Frontier's language-independent commodity identifier rather
than its localized display label, so future CAPI imports and journal transactions
use the same keys.

Carrier identity and observation source remain separate from commodity identity.
The manifest editor, commodities planner and overlay, shopping difference, Nexus
readiness, build reports, and synchronization compare the same commodity keys.
Each physical carrier contributes stock once, and ship cargo stays separate.
Raven and local observations retain their own quantities and source keys, so an
inventory action can target Raven without confusing its manifest with local cargo.
An authoritative market update clears aliases only for the commodities it actually
observed; it does not erase unlisted carrier stock. Relative journal transactions
still add or subtract their individual quantities, rather than being treated as
duplicate stock snapshots. Each transfer applies the same relative change to
the existing source aliases in one Raven request, preserving concurrent cargo
changes instead of replacing them with a cached count. Authoritative refreshes
and explicit manifest saves clear aliases. Retained retries keep the logical transaction and exact
source baseline, so a lost acknowledgement does not apply the transfer twice.

Large and medium ship estimates use Raven's default preview capacities of 794 and
400 tonnes, shown beside the estimates. The current ship's capacity and trip count
also appear when known. Delivery progress, carrier readiness, commander totals,
and hourly delivery history use Raven's public build data.

**Export CSV** saves the snapshot displayed when the button was clicked. The CSV
contains project details, commodities, carrier totals, system effects, and delivery
history in compact labelled tables with their own headers. Empty tables are omitted.
Combined exports replace project details with the report summary and included build
list. Numbers use invariant formatting and the file includes a UTF-8 BOM for Excel.
Public names and notes are escaped to prevent spreadsheet formula execution.

The **Show** checkbox controls the shopping plan and overlay. Opted-in ship-cargo
publishing continues when every project is hidden or no projects are loaded. Raven
access, credentials, commander and ship readiness, and protection against ambiguous
cargo from multiple game windows are still required.

The normal workspace list follows projects and system plans affiliated with the
commander through Raven's API. **Show Unaffiliated Projects in Current System**,
after **Open Raven builds**, is off by default. Enabling it browses the current
system and selects that system with **All Projects**. The preference is remembered
separately for each commander. Browsed projects start with **Show** unchecked and
are marked **Unaffiliated**.

Unselected browsing results expire when leaving the system or disabling the
option. Selecting **Show** or **Make Focus** explicitly retains a local project
across travel and restarts. Local focus does not change Raven's primary-project
setting or membership. Tracked live builds still report actual journal deliveries,
refresh remaining needs, and report completion through the normal Raven workflow.
Clearing both Show and focus releases local retention.
If the retained-project cache cannot be saved, focusing keeps the save warning
visible. **Refresh projects** retries retention without losing the in-memory
focus or its project snapshot.

Docking at any construction site still reveals its needs and temporarily makes
the docked project available for focus, independently of the browsing checkbox.
Undocking removes that temporary entry unless it has been explicitly retained.

The first successful commander API refresh automatically removes unrelated plans
accumulated by older versions, including when using **Refresh projects**. Existing
local focus, saved market-search selections, and local planning drafts are
preserved. A default checked Show box in an older cache cannot establish an
explicit tracking choice. Failed or unavailable planning API reads preserve cached
data until affiliation can be verified. Temporary browsing results are kept
separate from affiliated plans and deliberately retained projects.

The preview uses only the existing public GET endpoints:

- `api/project/{buildId}`
- `api/project/{buildId}/fc/`
- `api/project/{buildId}/stats`

No Raven API key is sent and no edit or delivery controls are available. Commodity
names and categories and system effects are bundled reference data taken from
[Raven Colonial's public frontend](https://ravencolonial.com/static/js/main.ce7b2225.js)
on 2026-10-09. System effects describe the build type; other sites and links determine
the eventual system-wide totals. Build cargo and progress are always fetched live.
