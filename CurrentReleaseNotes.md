# SrvSurvey-XP 2.1.3.0-rc.75

This patch fixes biology overlays remaining hidden after an interrupted jump or
game-session recovery, even when their display settings are enabled.

## What's changed

- **Biology overlay recovery:** Starting a new game journal, logging back in, or
  receiving a restored location clears stale jump suppression. System biology
  can appear again during exploration in space, and body biology and sample
  status can return on foot when their normal visibility conditions are met.
- **Jump visibility remains intact:** Biology overlays stay hidden while live
  game status still reports a hyperspace jump. Normal completed jumps continue
  to restore the appropriate panels, without changing saved overlay settings,
  categories, or positions.
