# Mining search and workflow review

Reference: [EliteMining at 75245800](https://github.com/Viper-Dude/EliteMining/tree/7524580059d23753f3583a6c03821983083740e2), particularly README, `main.py`, `marketplace_api.py`, and `system_finder_api.py`. This follow-up reviews the changes after `b8e3a27d` for PR 108.

## Problems and changes

- Oversized station responses raised `InvalidDataException` outside the search error boundary. Expected provider and malformed-data failures now become inline status messages. Trader requests use the requested radius and 20-station pages while retaining the 8 MB response limit. Cancellation and commander changes cannot start an obsolete replacement search or clear a newer profile.
- Markets and system planning were hidden beneath Hotspots. Mining now has **Find**, with **Rings**, **Markets**, **Traders**, and **Powerplay** destinations. Local discoveries and journal import remain accessible beside search destinations. Traders have their own results and selection instead of replacing market results.
- Wide, equal-width columns and nested vertical scrolling made location and purpose hard to follow. Search results now use wrapping location/detail cards, natural page height, and actions above results. Expanded filters grow in the same page; one workspace scrollbar handles filters and results. Other page-oriented mining lists let their page own vertical scrolling.
- Buying versus selling was ambiguous. Markets explicitly offers **Sell mined cargo** or **Buy supplies**, showing usable price, demand or stock, freshness, distance, station type, and pad information where reported. Ordinary ring-to-market navigation searches nearby stations.
- Powerplay starts with an objective and pledged power. Reinforce/Undermine require a known matching/opposing owner; unknown ownership is not treated as an enemy. Expansion is exclusively an acquisition candidate. Expansion/Contested observations come from the local cache because Spansh does not index them.
- Selecting a system preserves planning context. A ring search can remain in that system, or acquisition planning can search elsewhere for mining while retaining the acquisition sale destination. Powerplay selling is scoped to the selected destination **before provider pagination**. Clear plan returns to ordinary nearby searches.

Planning identifies candidate locations. It does not certify merit rewards, acquisition eligibility, or the current game's transaction rules. The in-app Guides describe the workflow and this limit.

## Review and validation

The standards and specification reviews both rechecked their findings against the corrected source and reported no remaining concrete findings within their bounded review. Fixes include exact-system pagination, acquisition destination retention, Expansion classification, and two cancellation races reproduced by regression tests.

A full solution run passed 3,514 tests (Core 1,461; Desktop 2,040; ReplayController 13). Focused follow-up tests cover the later review fixes, search query payloads, commander cancellation, recorded Rhino bar detection, and populated narrow layouts. The updated narrow presentation test fills all four search destinations with long names at 660 by 640 logical pixels, expands filters, and checks natural content growth, absence of internal scrolling/horizontal overflow, and actual wheel input over a result scrolling the workspace. The earlier bounded-viewport test missed the clipped layout reported during live testing and was replaced. Existing theme tests render the mining tabs across the application's theme catalog.

Live read-only Spansh checks verified a bounded trader response and exact-system station filtering. Search failures, session accounting, persistence, and workflow transitions use controlled fixtures. UI evidence is headless Avalonia rendering; a complete in-game mining/Powerplay session and audible speech have not been verified in this follow-up. Local tests do not substitute for the remote SonarCloud quality gate, which is checked after pushing.

## Quality corrections

Sonar findings are addressed through smaller parsing/detection methods, cached bound collections, deterministic disposal, cancellation ownership, and regression coverage. Calibration identity still compares exact stored values; detector thresholds, bar-state policy, and the three-second removal rule are unchanged. Release notes retain the RC43/44/44.5 history under RC45.

The Sonar follow-up retains narrowly documented S1244 exceptions on calibration identity and the exact reference transform: approximate equality would reuse stale analysis after explicit adjustments. Regression tests cover a one-representable-step calibration change. Service links use the shared URI-path builder; no provider URLs become player-facing configuration. The local follow-up passed 60 Core and 215 Desktop tests.
