# SrvSurvey-XP main-window UX redesign plan

Status: product direction confirmed; interactive prototype validated with corrections; production comparison build under test.

## 1. Product model

SrvSurvey-XP is an overlay-first Elite Dangerous companion. Its overlays provide the primary live gameplay feedback. The main window is opened briefly to configure the application, run searches, use tools, inspect status, or launch a related workspace before focus returns to the game.

The redesign must not turn the main window into a gameplay dashboard, recommendation engine, or replacement for overlay presentation.

## 2. Recommended visual direction

Use the prototype's **Balanced workspace** variant as the production base:

- Preserve the Raven sidebar, commander card, accent bars, badges, cards, Inter typography, and theme resources.
- Use the balanced card hierarchy on Overview.
- Borrow the denser, unframed row treatment from **Dense utility** for Settings and Diagnostics where repeated controls or diagnostic facts benefit from compact alignment.
- Do not use the persistent second content column from **Split workbench** as a general layout; it consumes too much width in the floating-window use case.

The prototype is a visual primary source, not production code. Rewrite selected behavior with normal Avalonia architecture, tests, localization, and accessibility.

## 3. Non-negotiable constraints

1. Category-specific overlay configuration stays behind the existing per-category sidebar shortcut.
2. The existing overlapping-windows shortcut glyph remains. Improve its button states and alignment without replacing the icon.
3. Global Settings must not become a massive catalog containing every category-specific overlay option.
4. Route Workspace, Journey, System Notes, Biology Predictions, Codex, Codex Bingo, Boxel tools, and comparable dense tools remain detached windows.
5. Separate import and export-format actions remain separate unless a future combined control explicitly asks the user to choose a format.
6. Existing tool launchers remain beside workflow-relevant controls or forms when proximity is part of the workflow.
7. Overlay panels and their in-game layout are outside this redesign.
8. Every existing command, field, status panel, click-to-copy target, deep link, search-provider integration, and detached-tool launcher remains available. The prototype is not a functional inventory.
9. Sphere Limit retains its current-limit summary, current-system context, EDSM suggestions with fallback behavior, resolved-center details, exact field labels, and enable/disable actions.
10. The hand-crafted Boxel workspace and Multiple Commanders card retain every current option and command.

## 4. Main navigation

Keep the sidebar at 248px and divide it into four stable areas.

### Permanent destination

- Overview

Overview remains visible above all groups.

### Collapsible workspace groups

**Survey**

- Exploration
- Exobiology
- Boxel

**Navigation**

- Travel
- Search

**Activities**

- Guardian
- Quests
- Colonization

### Fixed utility footer

- Settings
- Theme
- Guides
- Diagnostics

The utility footer stays outside the workspace scroller so those destinations are always reachable.

### Accordion behavior

- Only one workspace group is expanded at a time.
- Remember the last expanded group.
- Selecting a destination programmatically expands its owning group and collapses the others.
- Overview does not force a group open.
- Use a 120–160ms height/opacity transition without a disclosure glyph.
- Honor reduced-motion preferences by disabling the transition.
- Support keyboard expansion/collapse and normal directional navigation.

### Visual treatment

- Group headings are borderless text buttons with subtle hover and focus treatment. Do not show chevrons or other disclosure glyphs.
- Do not use boxed legacy tree nodes, connector lines, beveled headers, or nested card chrome.
- Child rows use one restrained fixed indentation.
- Keep three fixed columns: indentation/glyph, flexible label, overlay shortcut.
- Trim long labels with a tooltip; never expand the sidebar width.
- Keep the overlay shortcut independently clickable so it does not select the workspace row.
- Give the shortcut a consistent hit area, hover, pressed, focus, and tooltip state while retaining the current glyph.

### Branding and help

- Display **SrvSurvey-XP** beside the real SrvSurvey logo.
- Use the current black-and-blue split remastered artwork uniformly for the sidebar, window icon, executable, tray icon, and Linux package metadata.
- Display `CMDR'S COMPANION` as the brand subtitle.
- Replace the old descriptive footer with: `Ask questions at Guardian Science Corps on Discord`.
- Link only `Guardian Science Corps` to `https://discord.com/invite/GJjTFa9fsz`.

## 5. Global scrollbar standard

Scrollbar visibility and containment are application-wide requirements, not page polish.

### Appearance

- Show a recognizable track and thumb whenever content overflows; do not reduce the idle scrollbar to a line.
- Reserve a 14–16px gutter outside the content bounds.
- Target an approximately 8px idle thumb that expands within the reserved gutter on hover or drag.
- Use a minimum thumb length of roughly 32px so long pages remain discoverable.
- Use a rounded track and fully rounded thumb without line-arrow buttons or other retro scrollbar chrome.
- Apply theme-aware track, idle, hover, active, and focus colors.
- Apply equivalent behavior to horizontal scrollbars.
- When both axes are present, keep the scrollbar intersection transparent so the reserved corner blends into the containing surface instead of appearing as a square artifact.
- Do not shift or cover content when the scrollbar changes state.

### Containment

- Prefer one primary page scroller.
- Add nested scrollers only for genuinely independent bounded regions such as logs, tables, result lists, and inspectors.
- A bounded table or result viewport owns both of its scrollbars at the viewport boundary; do not place a vertical list scrollbar inside a separate horizontal scroller where it can overlap or become detached from the table edge.
- Ordinary cards grow with the page instead of acquiring their own scrollbar.
- Preserve disabled scroll chaining.
- Preserve list-boundary `RequestBringIntoView` containment so nested selection scrolls its own list without moving the surrounding page.

### Implementation direction

Create an application-wide Avalonia `ScrollViewer`/`ScrollBar` theme whose template reserves a real grid row or column for the scrollbar. Avoid relying on per-page negative margins or overlay expansion. Route Workspace's reserved-gutter layout is the existing reference behavior, but the global template should remove the need for repeated margin workarounds.

### Expandable sections

- Use a clean pill-shaped header surface with a restrained border and rounded hover state.
- Keep the expanded content visually subordinate to the header instead of placing a square dropdown inside a rounded outer card.
- Apply the same treatment to theme color groups, profile sections, Colonization sections, and comparable compact expanders without altering their commands or content.

### Dense result and guide presentation

- Boxel Stats browser rows keep the prefix on a dedicated non-wrapping title line and place the count/suffix and helium facts on a secondary line. Metadata must never squeeze a prefix into vertical letters.
- Guides catalogue entries show titles only; ordering numbers are not part of the visible catalogue.
- Guide procedures use concise text blocks with a restrained accent edge. Remove decorative circled chevrons and bullet glyph columns that add noise without meaning.
- Runtime validation must include a clean binding-error check so presentation refactors do not leave stale visual-ancestor bindings.

## 6. Overview

Overview is the fresh-launch landing page. Returning to a backgrounded window preserves the currently selected workspace.

### Content order

1. **Journal and companion health** — concise status, last update, and actionable error state.
2. **Commander context** — commander identity, location/body, game mode, and overlay availability.
3. **Commander data** — one compact row containing:
   - `Trip since reset`, linked to Exploration.
   - `Unclaimed biology`, linked to Exobiology.
4. **Multiple commanders** — compact current profile, focus-next-window, and launch-instance controls.

### Preservation requirements

- Preserve the current wide commander card composition: accent edge, identity and Frontier ID, session and game-version context, and the Location / Body / Mode row.
- Preserve the complete Multiple Commanders card: Refresh profiles, current commander, Focus next Elite window, commander selector, Launch instance, and status message.
- Do not replace either card with a simplified prototype summary.

### Remove

- `COMMANDER CONSOLE` wording.
- Mode-dependent suggestions or “Open next” recommendations.
- Detached-tool launchers.
- Large duplicate Exploration and Exobiology cards.
- Decorative `LIVE` badges.
- Primary hero treatment for Refresh.

Manual Refresh remains a secondary labeled control only because it refreshes additional caches and journal-derived state beyond the normal monitor loop.

## 7. Theme

Theme is a fixed utility workspace between Settings and Guides. It owns the complete appearance editor formerly contained in the Settings dropdown.

- Keep the three existing tabs: Application theme, In-game overlay appearance, and Overlay Settings.
- Preserve every preset, saved state, action, color editor, typography control, and overlay setting.
- Present General through Guardian color families as rounded collapsible panels.
- The color-family panels behave as an accordion: expanding one closes the previously open family, with General open initially.
- Label Typography as **Experimental** without disabling or removing it.

## 8. Settings

Replace the single long page with a searchable category workspace.

### Categories

**Application**

- Language

**Desktop**

- Monitor and window scale
- Focus-game behavior
- Tray behavior
- Preferred commander

**Global overlays**

- Cross-category overlay appearance and typography
- Scale and interaction behavior
- Notifications
- Pulse, stream, and VR integration
- Other controls that genuinely apply beyond one gameplay category

This category must state that activity-specific overlay options are opened from the sidebar shortcuts. It must not embed every category editor.

**Input**

- Keyboard hook
- Bindings
- Controller options

**Privacy & sharing**

- EDDN
- Inara
- Green Gas Giant sharing
- Settlement geometry
- Spansh timestamps
- System nicknames

**Screenshots**

- Screenshot processor
- Banner and naming options

**Data & migration**

- Journal folder
- Legacy import
- Codex image cache
- Dock-to-dock log

### Search behavior

- Keep the category list visible while space permits.
- Search results appear in the content pane and are grouped by category.
- Arrow keys move through results; Enter selects; Escape clears the query.
- Selecting a result opens its category, focuses the actual control, and briefly highlights its containing row.
- Search does not automatically jump on every keystroke.
- Define explicit keywords and aliases in a searchable settings catalog/view-model rather than scraping XAML labels.
- Remove the special top-level `Import SrvSurvey User Data` jump only after search and category navigation provide an equivalent direct path.

### Responsive behavior

- Use the two-pane Guides-style layout while the Settings content area has enough usable width.
- When the content area falls below approximately 620px, replace the category column with a category selector above the content.
- Do not compress descriptions into narrow columns.
- Do not introduce page-level horizontal scrolling.

## 9. Search

Use two stable tabs:

- Sphere Limit
- Nearby Biology

Only one workflow is visible at a time. Preserve entered state when switching tabs.

The Sphere Limit tab must preserve the complete existing contract, including the current-limit panel, current location and distance, system/id64 entry, EDSM-backed suggestions with Ardent fallback, resolved center, radius, Enable limit, Disable limit, and status text. Labels must remain source-faithful; visual mockup labels are not substitutes.

The Codex Bingo deep link must:

1. Select Search in the main navigation.
2. Expand the Navigation group.
3. Select the Nearby Biology tab.
4. Run the intended query.

## 10. Diagnostics

Use five stable tabs.

### Source

- Active monitored journal
- Effective journal path
- Candidate paths and source resolution
- Read-only source health

Journal-folder configuration remains in Settings → Data & migration.

### Updates

- Current release/update status
- Release details and update actions

Clicking the application update notification must bring the main window forward, select Diagnostics, and select Updates directly. Do not retain scroll-to-element navigation.

### Processing

- Historical processor
- Codex ledger
- Visited Stars cache

### Inspector

- Parsed journal state
- Live `Status.json` state

### Logs

- Application logs and log actions

Error-report “show logs” actions must select Diagnostics → Logs directly.

## 11. Page headers and detached-tool launchers

### Header pattern

- Eyebrow only when it adds meaningful scope.
- Title.
- One-line purpose or current status.
- Stable right-aligned action row.
- The real page task receives primary emphasis.
- Refresh is secondary and appears only where manual refresh has real behavior.
- Use an overflow menu only when more than three header actions cannot fit cleanly.

### Launcher locality rule

Workflow locality outranks global consistency.

- Keep a detached-tool launcher beside the form, result, or control it acts on when proximity explains the workflow.
- Move only launchers that are buried below unrelated content, duplicated, or isolated in a card whose only purpose is opening a window.
- When moved, place the launcher in the page header or an immediate action row.
- Do not move launchers to Overview or change their location based on game mode.

## 12. Copy rules

- Use short user-facing descriptions.
- Move WinForms parity notes, formula explanations, upload internals, and compatibility narratives to Guides.
- Preserve precise action labels such as `Exp Spansh` and `Exp CSV` while those remain separate commands.
- Do not call persisted/resettable exploration totals “this session.” Use `Trip since reset`.
- Use `Unclaimed biology` for unpaid organic scans and rewards.
- Status labels must reflect real state; do not display `LIVE` while the journal session is closed.
- Preserve every existing click-to-copy target across the application. Copy affordances use accent color, pointer cursor, tooltip, and hover/focus treatment without underlining the text.

## 12.1 Update notification behavior

- Keep the existing update notification that rises from the bottom edge when a release is available.
- Do not add a persistent update-available button to the shell or header.
- Activating the notification still routes to the Updates section in Diagnostics.

## 13. Implementation architecture

### Navigation

- Add explicit group metadata rather than inferring groups from item order.
- Keep stable destination keys for existing deep links.
- Model group expansion separately from selected destination.
- Preserve selected page/view-model instances; do not reconstruct views when groups collapse.

### Settings

- Introduce a settings catalog/view-model containing category, label, description, keywords, control target, and focus/highlight routing.
- Keep storage ownership in existing settings view-models and stores.
- Category navigation changes presentation, not settings persistence.

### Tabs and deep links

- Represent Search and Diagnostics tab selection in view-model state.
- Replace scroll-based deep links with explicit destination-plus-tab commands.
- Preserve hidden-page state because the main window keeps all pages instantiated and toggles visibility.

### Scrollbars

- Implement the global theme before adding page-specific gutter exceptions.
- Audit intentional nested scrollers after the global template is active.
- Keep page-level and nested scrolling behavior independently testable.

## 14. Regression coverage

Add or update focused coverage for:

- Navigation group inventory and item ownership.
- Accordion exclusivity and remembered expansion.
- Programmatic selection expanding the owning group.
- Overlay shortcut clicks opening the filtered window without selecting the row.
- Retention of the existing overlay shortcut glyph resource.
- Fixed utility-footer destinations.
- Settings category inventory and ownership.
- Settings search aliases, result ordering, keyboard navigation, focus, and highlight routing.
- Responsive Settings category-selector mode.
- Scrollbar theme dimensions, reserved gutter, and theme resources.
- Nested-list selection containment and disabled scroll chaining.
- Search tab state and Codex Bingo deep linking.
- Diagnostics tab inventory.
- Update notification → Diagnostics → Updates.
- Error report → Diagnostics → Logs.
- Overview labels and removal of decorative `LIVE` text.
- Complete Sphere Limit control and EDSM suggestion inventory.
- Complete Multiple Commanders control inventory.
- Complete Boxel command and option inventory.
- Click-to-copy inventory plus the global no-underline visual rule.
- Bottom-edge update notification with no persistent shell action.
- Footer text and Discord URI.
- SrvSurvey-XP brand text and real logo asset.

Retain or migrate existing presentation tests that currently assert scroll-to-Updates behavior; their replacement must assert selected-tab state instead.

## 15. Implementation order

Before implementation, update the working branch from its remote base and re-audit changed UI surfaces; the checkout used for this plan was 61 commits behind `origin/SrvSurvey-Avalonia`.

1. Global scrollbar theme and containment regressions.
2. Navigation grouping, accordion behavior, fixed footer, branding, and help link.
3. Settings catalog, categories, search, focus routing, and responsive layout.
4. Overview hierarchy and truthful labels.
5. Search and Diagnostics tabs plus all deep-link migrations.
6. Header/action cleanup and workflow-sensitive launcher audit.
7. Copy cleanup and Guides relocation.
8. Full functional tests.
9. Visual verification at normal width, minimum supported width/height, 100–200% scaling, every Raven theme, keyboard-only navigation, and reduced motion.

Commit each numbered slice separately for review.

## 16. Completion criteria

The redesign is complete only when:

- Every existing main-window feature remains reachable.
- Every pre-redesign interactive control remains present and bound to the same behavior unless a separately approved functional change replaces it.
- Every category overlay shortcut still opens the correct lean configuration window.
- No combined all-category overlay settings page has been introduced.
- Settings search reaches every migrated setting.
- Update and log notifications navigate to the correct Diagnostics tab.
- Scrollbars are discoverable and never cover content across audited desktop surfaces.
- Detached-tool launcher moves have been reviewed individually for workflow locality.
- No responsive width produces horizontal page scrolling or unreadably compressed descriptions.
- Functional tests pass before final visual verification.
- Runtime behavior is checked on the supported desktop platforms in addition to headless presentation tests.

## 17. Out of scope

- In-game overlay layout, stacking, click-through, and presentation timing.
- Replacing detached tools with in-page workspaces.
- New gameplay recommendations or automated “next action” guidance.
- Feature removal.
- A visual restyle that replaces the Raven identity.
- Combining distinct import/export actions without an explicit format choice.
