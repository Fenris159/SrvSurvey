# SrvSurvey-XP 2.1.3.0-rc.67

RC67 reorganizes Colonization into a tabbed workspace, brings planned projects
into supply planning, and makes carrier coverage and ShoppingList controls easier
to use. Overlay panels also retain unchanged content during routine refreshes
to prevent subtle shifting while flying.

## What's changed

- **Navigate Colonization by task.** The workspace now opens on **Projects**,
  followed by **Market Search**, **System Editor**, and **Settings**. Project
  creation sits above **My build projects** and **Live Depot**. Consent and
  carrier-sync settings have a dedicated page with their controls always visible.
- **Plan supplies before construction begins.** Planned sites appear beside
  active builds with **(Plan)** appended to their names. Plans from the commander's
  Raven Colonial systems and locally saved systems can be listed independently
  of the player's current location. **All Systems** and **All Projects** filters
  narrow the list by system or by **Building** and **Planning** status.
- **Choose a project to focus.** **FOCUS** and **Make Focus** replace the primary
  project labels. Planned projects can also receive focus so their commodity
  requirements can guide carrier loading before a live depot exists.
- **Select projects inside Market Search.** Project tags replace the **Shop**
  column and combine the needs of the selected projects. Both selected and
  unselected tags retain a button appearance. **Show** remains an independent
  control for the colonization overlay.
- **Search markets within the workspace.** Market Search now occupies its tab
  instead of a separate window. Results use the available workspace width,
  expanded commodity tags wrap, and scrolling begins after space for at least
  20 compact result rows. Column headers stay aligned, scrollbars sit outside
  the result content, and expanding a station does not add a selection highlight.
- **Build a ShoppingList with commodity tags.** Wishlist is now called
  **ShoppingList** throughout the interface. Station commodity tags remain
  visible after selection, use a secondary highlight when allocated, and can
  be clicked again to remove the allocation. Brief instructions explain this
  interaction above the results. The remaining-tonnes summary and **Open
  ShoppingList** button sit together above the table; **Copy needs** uses the
  same button style as the other search actions.
- **Keep planning changes reliable.** Editor updates preserve ShoppingList
  allocations, and delayed refreshes keep newer local edits and removals.
  Removed plans leave Market Search, legacy sites without an explicit planning
  status stay out of the project list, and planned sites without Raven IDs keep
  distinct supply requirements. Mixed live and planned projects retain linked
  carrier cargo coverage. Rapid site edits coalesce local cache writes; pending
  edits are saved before switching commander or closing the application.
  Toggling Raven access preserves saved planning selections and ShoppingList
  allocations when planning data is temporarily unavailable. Travel refreshes
  fetch only the current system and cancel superseded planning reads.
- **Keep carrier comparisons consistent.** **Use Carrier** persists between
  sessions and stays synchronized with **FC Diff** in both directions. **LOADED**
  recalculates from the selected carrier's cargo against remaining project needs,
  including after deliveries, and displays decimal precision. Project headings
  and values use distinct, aligned columns.
- **Edit system sites without cramped fields.** System Editor shows its sites
  directly in a single-line table with usable field widths and horizontal
  scrolling when the table exceeds the workspace. **Prepare new project** turns
  into **Cancel** during review so the form can be cleared and restarted.
- **Control cargo-group expansion from the keyboard.** **Ctrl+Alt+Shift+X**
  toggles **Collapse cargo groups when enough on FCs**. Assign it under
  **Panel availability and shortcuts** in Colonization overlay settings or in
  the master input shortcut list. **Hide collapsed groups** remains a separate
  display preference.
- **Show the colonization overlay when docked.** Auto-hide-until-docked now
  reveals selected projects while docked even when the always-available
  Colonization commodities panel is disabled. Enabled **Auto Refresh** also
  keeps readable text contrast in the monochrome theme.
- **Keep overlay content steady during refreshes.** Unchanged rows are retained
  in Colonization, Biology and survey panels, mining cargo and notices, Guardian,
  exploration and galaxy-map panels, quests, combat missions, notifications,
  and ShoppingList. Real changes still update the displayed information and
  available actions.

## Update channel and packages

RC67 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.67`
- Tag: `xp2-v2.1.3.0-rc.67`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.67-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.67-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.67-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.67-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Market stock and carrier comparisons use
available observations and can lag in-game changes. Refresh project and market
data when planning purchases. Carrier manifest edits update Raven Colonial's
records and do not transfer physical cargo.
