# SrvSurvey-XP 2.1.3.0-rc.72

This update covers [PR #250](https://github.com/Fenris159/SrvSurvey/pull/250):
preserving overlay settings during profile imports, adding mining mission
tracking and session prospecting yields, and improving combat, colonization,
and overlay presentation.

## What's changed

- **Overlay settings during profile import:** Imports preserve the current
  installation's overlay visibility, opacity, scale, layouts, shortcuts, and
  integration settings. Settings that have never been saved retain XP defaults.
  XP mining imports also preserve mining overlay preferences while importing
  history, missions, and portable preferences. Verified source backups remain
  available for recovery.
- **Mining mission cards:** Mining → Missions uses themed cards with cargo
  aboard, cumulative deliveries, expiry, rewards, and hotspot search. Click the
  destination to copy its system name. Each mission has a tracking checkbox;
  new missions are tracked by default and choices are saved per commander.
- **Mining Missions overlay:** Selected active missions show cargo and delivery
  progress during collection and return trips, including outside a mining
  session. Unchecked, expired, and finished missions are excluded. Idle updates
  refresh expiry times without replacing an unchanged mission list, preserving
  the overlay's scroll position.
- **Mission overlay sizing and placement:** Mining Missions and Massacre
  Missions expand and contract with their content. Long lists scroll within
  the available game or screen height at the current display and overlay scale.
  Saved widths and left/right growth choices are respected; saved heights no
  longer force empty space. Both panels default to the top-right corner with
  8-pixel margins, while saved custom positions are preserved.
- **Mining Session overlay:** The new Session panel shows the active or paused
  session's location, duration, activity, limpets, and prospecting yields. The
  per-mineral breakdown includes finds, quality hits, average yield, and best
  yield. It sizes to its content, preserves the selected growth edge, and scrolls
  longer breakdowns within the available height.
- **Mining session controls:** Start, Pause / Resume, and End Session shortcuts
  share their saved bindings with Settings → Input. The default-on
  **Stop sessions automatically after FSD jump or docking** option ends active
  and paused sessions on live travel events. Historical journal recovery does
  not trigger automatic stops.
- **Mining surface settings:** Surface panels and calibration have a separate
  **Mining (surface)** category in the overlay editor.
- **On-foot suit suppression:** Dominator and Maverick suit switches hide
  non-combat overlays while keeping ground combat and massacre mission panels
  eligible to appear. Both suit choices are grouped together on the left.
- **Combat and colonization priority:** The combat suppression option applies
  while the colonization commodities panel is visible. Hiding the panel or
  clearing its focused content restores eligible combat overlays. Mission
  tracking continues while the combat panel is hidden.
- **Massacre mission progress:** Rows show completed kills out of the required
  total, such as **0/6** through **6/6**. Completed objectives use the same green
  checkmark as colonization commodity rows.
- **Carrier manifests:** Guardian salvage aliases match local inventory.
  Local CAPI, carrier-market stock, and journal transfers supply comparison
  totals, including locally held goods missing from Raven. All goods are
  visible by default; the colony-only filter remains available. Partial or
  delayed market snapshots cannot restore quantities superseded by a newer
  accepted CAPI inventory.
- **Colonization overlays and market search:** Commodity panels contract as
  rows disappear, including with a saved panel size. The outdated-local-markets
  option appears beside Ardent, and carrier, surface-port, and full-supply
  filters remain visible above the ShoppingList instructions after a search.
- **Overlay text customization:** Each text role can select a brush from the
  overlay appearance swatches beside its size slider. Overrides follow theme
  changes, save per panel, and reset with the panel's text settings. In Next
  Jump, the subtitle uses Caption, destination detail labels use Title, and
  the route legend uses Header.
