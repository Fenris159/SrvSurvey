# SrvSurvey-XP 2.1.3.0-rc.73

This update adds carrier next-jump tracking without a planned route, keeps
unaffiliated colonization projects separate from your own projects, and improves
Spansh searches and mission overlay sizing.

## What's changed

- **Carrier next-jump information:** The Fleet Carrier Route overlay can show
  your carrier's next destination without an active route. It follows the
  scheduled departure, jump initiation, transit, confirmed arrival, cooldown,
  and cancellation. Travel overlay settings include **Show Carrier Next-Jump
  Information**, enabled by default, and the editor has a **Carrier next jump**
  preview. An actual jump that differs from a planned route temporarily shows
  its real destination without changing the plan.
- **Carrier fuel and destination details:** Available journal fuel and stored
  tritium appear alongside jump distance, fuel cost, and projected fuel after
  arrival. Calculated values are marked as estimates; unavailable details are
  omitted. Passive tracking shows **LOW TRITIUM** when the projected depot
  fuel falls below **200 t**. Spansh destination data adds **ICY RING**,
  **PRISTINE ICY RING**, and **ICY BODY** tags when those resources are known.
  The fuel warning icon is larger, centered with its text, and follows the
  overlay theme. Repositioning within the same system deducts jump fuel once;
  later journal fuel observations replace that estimate.
- **Next Jump neutron badge:** Class-N destinations show the **NEUTRON** pill
  beside the star class even without a pre-planned route or with details hidden.
  Existing route guidance continues to supply the badge when applicable. Long
  system names trim before the badges instead of overlapping them.
- **Mining session commodity names:** Prospecting yield summaries use consistent
  catalog casing, including **Gold**, **Silver**, and **Palladium**. Existing
  session history gains the same display names without rewriting journal data.
- **Mining session travel:** Successfully entering supercruise pauses the current
  session. Launching a prospector or collector, prospecting, or refining resumes
  the same session, retaining its totals and excluding paused time. The travel
  pause survives a restart and can resume even with automatic session creation
  disabled. Hyperspace arrival and docking still end the session when travel
  automation is enabled. Charging, initiating, or cancelling a jump does not
  pause or end it. Historical replay alone does not trigger travel automation,
  and repeated travel updates cannot interrupt an already resumed session.
- **Colonization project visibility:** Your workspace primarily shows projects
  affiliated with your commander through Raven. The new **Show Unaffiliated
  Projects in Current System** checkbox is off by default. Enabling it loads
  the current system and selects **All Projects**; while enabled, traveling
  replaces temporary results with the next system's projects. Explicit Show
  and Focus choices retain individual projects separately from affiliated
  projects. Tracking and delivering to an unaffiliated project still reports
  contributions to Raven and refreshes its requirements and completion. If a
  focused project's retained data cannot be saved, the warning remains visible
  and **Refresh projects** can retry the save. Cancelling auto-refresh preserves
  unsaved Show choices while retained projects are being fetched.
- **Existing colonization data cleanup:** A successful project refresh removes
  previously discovered unaffiliated projects that you have not explicitly
  chosen to retain. Focused projects, shopping selections, and local drafts
  are preserved. Failed refreshes leave saved data intact. Docking at a
  construction site continues to show its needs temporarily, independently
  of the browsing checkbox, with manual focus available for continued tracking.
  Re-enabling Raven restores current-system browsing, and temporary docked
  projects still expire if the initial affiliation request was unavailable.
- **Current-ship range in Spansh searches:** Neutron, Road to Riches, Tourist,
  and Exobiology searches autofill jump range from the current ship's journal
  `MaxJumpRange`. Reopening the search after changing ships refreshes the
  value. Fields remain editable; unavailable values or ranges outside the
  search's supported limits remain blank. This value is the ship's maximum
  range, rather than its current laden range.
- **Content-sized mission overlays:** Mining Missions and Massacre Missions
  now expand and contract in width as well as height to fit their visible
  content. Long text wraps at the available game width, and longer mission
  lists scroll within the available height. Saved left/right growth choices
  and custom positions remain effective. Saved fixed sizes no longer leave
  unnecessary empty space, wide displays retain a readable maximum panel width,
  and the editor preview uses the same sizing rules.
