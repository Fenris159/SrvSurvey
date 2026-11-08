# SrvSurvey-XP 2.1.3.0-rc.77.10

RC77.10 improves mining search accuracy, adds commander rank progress, includes
Vista Genomics in the Station information overlay panel, and restores tracking
for conflict-zone massacre missions.

## What's changed

### Mining searches and reference

- **Match mining methods correctly:** Laser and core filters use a shared
  commodity and ring compatibility catalog, including minerals available through
  both methods. Selected minerals also constrain Powerplay mining results.
- **Keep local observations searchable:** Journal hotspot identities remain
  independent of language, and previously saved translated hotspot names remain
  compatible with mineral and extraction-method filters.
- **Include single Platinum hotspots:** Normal Platinum results include
  compatible singles. Spots++ orders pristine reserves first, then system and
  arrival distance; All platinum orders by distance. Explicit minimum-count and
  overlap filters remain available.
- **Explain results clearly:** Site notes replace Why it ranks. Hotspot count
  no longer acts as a productivity score, and the current sorting policy is
  explained beside the existing controls.
- **Clarify RES guidance:** RES sites replaces RES mapped, with existing saved
  selections carried forward. Reference guidance explains the local laser-mining
  yield context, the approximately 20 km radius, and security considerations.
- **Clarify statistics:** Session, overlay, and report labels distinguish
  prospected asteroids, core finds, and tons per active hour. Average percentages
  describe matching rocks. Existing calculations and CSV columns are retained.

### Commander ranks and reputation

- **Show rank progress:** Pilot rank cards show a progress bar beneath the rank
  name, aligned within the text column. Federation and Empire navy ranks and
  their progress appear beneath their respective reputation values.
- **Use reported commander data:** CAPI rank levels are combined with journal
  progress and promotions. Percentages use the last recorded journal report and
  may differ from the live game panel. Missing progress is identified explicitly,
  highest ranks are marked, and commander changes keep observations separate.
- **Retain available ranks:** Known navy ranks remain visible when reputation
  is unavailable, and attributed journals fill categories absent from CAPI.
  Rank reports keep their own freshness; CQC correctly ends at Elite.
- **Group the three superpowers:** Reputation is ordered Federation, Empire,
  Alliance. Alliance shows reputation only. Powerplay retains its rank and merits
  display. The layout adapts to compact windows and all application themes.

### Station information overlay panel

- **Include Vista Genomics:** The relevant services list now includes Vista
  Genomics beside Universal Cartographics when the imported station data reports
  it as available. Existing overlay styling and theme colors are retained.
- **Keep station details accessible:** Matching provider records can fill missing
  service data. The panel retains its saved width and grows with its contents,
  keeping services and station details visible.

### Massacre mission overlay

- **Recognize massacre variants:** Mission tracking accepts journal names that
  begin with `Mission_Massacre`, including War, Civil War, wing, and additional
  suffixes. Valid mission IDs, givers, and target factions are still required.
- **Track the right kills:** Conflict-zone ship kills use combat bonds while
  other massacre missions retain bounty tracking. Credit remains limited to one
  active mission per giver for each kill. On-foot and SRV combat bonds do not
  advance ship missions, and the mission type is retained when saving progress.
- **Exclude surface combat:** Combat bonds earned at a surface settlement do
  not advance space conflict-zone missions, even when the commander stays in a
  ship. Leaving the planet or entering supercruise restores space-bond tracking.
- **Recover journal progress correctly:** Ship, foot, and SRV transitions are
  applied at the time of each recorded kill. Suit starts and Nomad launches retain
  their ground-combat identity. Empty combat profiles recover
  previously ignored missions without recounting already-saved mission progress.

### Reliability

- Keep journal monitoring active when system data contains repeated body names.
  This prevents survey display errors from stopping later Raven Colonial carrier
  transfer updates, and also fixes the same error during external body-data loads.
  Completed ring maps remain recognized when body records share a name. Bodies
  named after their system keep a readable label instead of a blank name.
  First-footfall commands and destination highlights retain the intended body
  identity when names are shared.
- Handle unresolvable boxel-sector names without stopping journal monitoring.
  Unsupported geometry keeps its recorded identity without inventing a system
  address, allowing later journal updates to continue normally.
- Handle journal changes during history reads without failing application
  shutdown, while retaining recovery checks and unexpected-error reporting.
