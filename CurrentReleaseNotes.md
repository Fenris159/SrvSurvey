# SrvSurvey-XP 2.1.3.0-rc.77.10

RC77.10 improves mining search accuracy, adds commander rank progress, and
includes Vista Genomics in the Station information overlay panel.

## What's changed

### Mining searches and reference

- **Match mining methods correctly:** Laser and core filters use a shared
  commodity and ring compatibility catalog, including minerals available through
  both methods. Selected minerals also constrain Powerplay mining results.
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
- **Group the three superpowers:** Reputation is ordered Federation, Empire,
  Alliance. Alliance shows reputation only. Powerplay retains its rank and merits
  display. The layout adapts to compact windows and all application themes.

### Station information overlay panel

- **Include Vista Genomics:** The relevant services list now includes Vista
  Genomics beside Universal Cartographics when the imported station data reports
  it as available. Existing overlay styling and theme colors are retained.
