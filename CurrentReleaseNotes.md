# SrvSurvey-XP 2.1.3.0-rc.76

This release refreshes the application and commander workspaces, makes overlay
configuration clearer, and adds exploration privacy, Discord carrier alerts,
and cargo-confirmed mining totals.

## What's changed

- **Application appearance:** A sleek spaceship-inspired design brings clearer
  typography, spacing and controls to the main window and dialogs while keeping
  all six theme palettes, existing navigation and custom forms.
- **Commander workspace:** Commander, current ship, Fleet Carrier, Market,
  Shipyard and Community Goals views present their information in a clearer
  reading order with fewer enclosing panels. Debt is shown only when nonzero.
- **Overlay configuration:** Theme appearance and workspace overlay settings are
  organized into clearer groups. Five custom brush colors can be selected for
  overlay editor overrides independently of the application theme.
- **Overlay sizing and hints:** The spherical search overlay expands and shrinks
  with its contents while respecting its anchor. Empty hover hints no longer
  produce blank squares.
- **Colonization visibility:** Turning off both automatic hiding and Colonization
  commodities availability keeps the panel hidden, including when starting the
  game docked. Shortcut toggling remains available, and the settings explanation
  makes persistent display clearer.
- **Stealth Mode:** An optional Privacy & sharing setting holds enabled EDDN,
  Inara, EDAstro and EDSM uploads in persistent queues until a live docking.
  Reports survive restarts, and discoveries after docking wait for the next
  docking. Any docking counts, including a Fleet Carrier; Raven API traffic is
  unaffected. Turning Stealth Mode off resumes retained uploads.
- **Discord carrier announcements:** Optional webhook settings announce planned
  jumps, cancellations and confirmed arrivals for personal and squadron Fleet
  Carriers, with itinerary details and separate notification choices.
- **Mining session accounting:** Session totals and rates reconcile refinery
  output with actual ship cargo while excluding limpets, purchases, transfers
  and other cargo movements. Refinery output after a session ends appears as
  separate carryover, and incomplete evidence is clearly marked as estimated.
  Recovery across pauses, journal replay and application restarts preserves
  accurate counts without counting the same physical record twice.
