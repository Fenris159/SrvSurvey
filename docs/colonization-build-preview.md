# Raven build preview

In Colonization → **My build projects**, select a project name to open its live,
read-only preview. Multiple builds can have their own windows open. Selecting the
same name again activates its existing window. Opening a preview leaves **Show**,
the primary project, and delivery tracking unchanged.

The window refreshes every 30 seconds and provides an immediate **Refresh** button.
Closing it stops its refreshes and cancels pending reads. Disabling Raven access or
changing the active commander closes all build previews.

Cargo rows show remaining requirements, each linked carrier's stock, and the signed
**FC Diff**. A positive value is surplus and a negative value is a shortage. Surplus
of one material never offsets another material's deficit. Carrier totals count each
commodity once even when Raven returns both its journal and display names. Missing
carrier data is shown as unknown. A failed refresh retains the last successful
snapshot; its status and update time make that clear.

Large and medium ship estimates use Raven's default preview capacities of 794 and
400 tonnes, shown beside the estimates. The current ship's capacity and trip count
also appear when known. Delivery progress, carrier readiness, commander totals,
and hourly delivery history use Raven's public build data.

**Export CSV** saves the snapshot displayed when the button was clicked. The CSV
contains project details, commodities, carrier totals, system effects, and delivery
history. Its rectangular table uses a `Record` column to distinguish those sections;
filter to `Commodity` for a cargo-only spreadsheet. Numbers use invariant formatting
and the file includes a UTF-8 BOM for Excel. Public names and notes are escaped to
prevent spreadsheet formula execution.

The preview uses only the existing public GET endpoints:

- `api/project/{buildId}`
- `api/project/{buildId}/fc/`
- `api/project/{buildId}/stats`

No Raven API key is sent and no edit or delivery controls are available. Commodity
names and categories and system effects are bundled reference data taken from
[Raven Colonial's public frontend](https://ravencolonial.com/static/js/main.ce7b2225.js)
on 2026-10-09. System effects describe the build type; other sites and links determine
the eventual system-wide totals. Build cargo and progress are always fetched live.
