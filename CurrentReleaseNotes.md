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
  overlay theme.
- **Colonization project visibility:** Your workspace primarily shows projects
  affiliated with your commander through Raven. The new **Show Unaffiliated
  Projects in Current System** checkbox is off by default. Enabling it loads
  the current system and selects **All Projects**; while enabled, traveling
  replaces temporary results with the next system's projects. Explicit Show
  and Focus choices retain individual projects separately from affiliated
  projects. Tracking and delivering to an unaffiliated project still reports
  contributions to Raven and refreshes its requirements and completion.
- **Existing colonization data cleanup:** A successful project refresh removes
  previously discovered unaffiliated projects that you have not explicitly
  chosen to retain. Focused projects, shopping selections, and local drafts
  are preserved. Failed refreshes leave saved data intact. Docking at a
  construction site continues to show its needs temporarily, independently
  of the browsing checkbox, with manual focus available for continued tracking.
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
  unnecessary empty space, and the editor preview uses the same sizing rules.
