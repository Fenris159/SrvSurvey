# Spansh route searches

The **Spansh Search** button in the Route Workspace header opens a dedicated
search window. Choose a planner, fill its inputs, and select **Search**. SrvSurvey
submits the form, waits for the Spansh job, and automatically loads the result
into the navigation itinerary through the existing route importer. The search
window closes on success. Results are an active on-screen draft; **Save As**
keeps the route, just as with a Spansh URL imported from the clipboard.

The ship workspace offers Neutron Plotter, Road to Riches, Tourist Planner,
Galaxy Plotter, Expressway to Exomastery, and Colonisation Plotter. Ammonia,
Earth-like, rocky/high-metal-content, and trade searches are excluded.

The FC Route Workspace has its own header button and search window, offering
Fleet Carrier Router exclusively. Results retain the carrier logistics columns
and cannot be loaded into the ship itinerary. Carrier inputs include player
or squadron type, ordered destinations, used capacity, automatic starting-fuel
calculation, refuelling destinations, and manual tank/market Tritium when
automatic fuel calculation is off. Refuelling destinations must be included
among the route destinations. Capacity validation follows the selected type.

Carrier results read the API's `fuel_in_tank` column and numeric `0`/`1`
restock flags, while retaining support for older `fuel_remaining` and boolean
fields. Required restocks show **YES** and the recommended tonnage beside it.
Stops without a restock leave both restock cells blank; missing or zero
recommendations are stored as null. A genuine zero fuel balance still shows
**0**. Clipboard imports and searches share this parser and are tested against
the same captured 46-stop Sol → Colonia route, including its 953 t and 937 t
restocks.

Fleet-carrier route displays also show **ETR** at 20 minutes per remaining
jump, formatted as days, hours, and minutes with zero-valued units omitted
(for example, `ETR: 14h 40min` for 44 jumps or `ETR: 1d` for 72 jumps).
The estimate follows the overlay's remaining-jump count and
refreshes when route progress or the route itself changes. It stays fixed
between jumps, including while the separate departure/cooldown timer ticks.
It appears after the overlay's jumps-left text with the same typography, below
progress in the FC Route Workspace, and below the filename as the fourth line
of the main window's current FC route card. The position-editor preview uses
the same calculation. Paused routes retain their estimate. When no jumps
remain, all three displays hide the ETR, including its separator in the overlay.
Empty workspaces also hide the estimate.

Source fields start with the current system. Individual system fields and the
reusable waypoint entry use SrvSurvey's existing `SystemNameEntry` autocomplete.
Choose or type a system, select **+ Add**, and it appears in the ordered list
below; the entry clears for the next destination. Each row offers edit, remove,
move up, and move down. Edit loads the name into the same autocomplete entry;
**Save** or Enter commits it, and **Cancel edit** keeps the original. A pending
entry must be added or saved before submitting the search.

Fleet-carrier destinations include per-row **Refuel** checkboxes while automatic
starting-fuel calculation is enabled. Removing or renaming a destination also
updates its refuelling selection. Carrier type and neutron supercharge options
use radio buttons; the website's boolean options retain their checkbox defaults.
Numeric inputs have a compact width, system entries fill the available width,
and long destination names wrap in their rows. The search footer remains visible
while the parameter area scrolls. Forms are tested at 560×500 and 720×820.

Galaxy and carrier submissions resolve exact system names to Spansh ID64s;
other planners submit names, following the website's current contract.

## Galaxy Plotter and the equipped ship

**Use current equipped ship** is enabled by default. The Ship Build field
automatically follows the current ship's EDSY SLEF export, including actual
journal engineering modifiers. It becomes available after a complete Loadout
for the identified commander's current main ship. Refitting, changing ships,
or leaving the main ship disables this automatic build until the journal
confirms a usable snapshot. Uncheck the option to paste an EDSY SLEF manually.

Spansh receives the original SLEF plus its required jump inputs: EDSY FSD fuel
coefficients, optimal mass, maximum fuel per jump, Guardian range boost, and
the journal's unladen mass and main/reserve fuel capacity. Actual engineering
values override base FSD values. The Mk II SCO drive uses Spansh's special
supercharge multiplier. Engineering mass changes update the cached unladen
mass by the difference between actual old/new module masses; an unverifiable
change waits for another complete Loadout.
Missing drive, mass, or fuel information produces an
error instead of guessed jump calculations.

Cargo, reserved fuel, the six supercharge/injection/refuelling checkboxes,
and all five routing algorithms remain editable. Searches follow Spansh's
anonymous form: Galaxy computation time is 60 seconds. Spansh account login,
saved website ship profiles, and account-only calculation-time controls are
not integrated.

## Cancellation and verification

**Cancel search** or closing the search window cancels submission/result polling.
Commander changes also cancel the operation. Failed, empty, or cancelled
searches preserve the existing itinerary. Search inputs remain available for
correction. Nothing overwrites a saved route automatically.

Request tests cover all seven planner endpoints, repeated waypoint fields,
checkbox/default parameters, exact ID64 resolution, ship calculations,
validation, cancellation, and workspace isolation. Avalonia interaction tests
exercise the header launcher, all forms, success handoff, and window lifecycle.
Two short live jobs verified Galaxy SLEF and carrier submission/polling on
2026-10-14, using synthetic data for Sol → Alpha Centauri. Game interaction
was not exercised.

Final validation passed `pwsh ./tools/Test-ChangedCodeQuality.ps1 -BaseRef HEAD`:
2,359 Core tests and 3,810 Desktop tests passed (three platform-specific tests
skipped), the 41-case journal-upload matrix passed, and new-code line/branch
coverage was 97.2% (1,786/1,837 points). The Sonar-enabled Release build completed
with zero warnings/errors. Formatting, localization freshness, and the local
Sonar profile also passed.

For the current website contracts and primary sources, see
[Spansh planner search contract](research/spansh-planner-search-contract.md).
The existing SLEF/data refresh workflow is described in
[Ship module data](ship-module-data.md).
