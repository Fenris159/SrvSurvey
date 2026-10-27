# SrvSurvey-XP 2.1.3.0-rc.74

This update fixes Fleet Carrier manifest saves and duplicate inventory entries,
adds a carrier-by-carrier readiness breakdown to Raven build previews, and makes
version details and installed release notes easier to find in Diagnostics.

## What's changed

- **Carrier manifest saving:** Changing a carrier's system and saving its
  manifest now sends the identifier Raven requires, fixing the reported
  **carrier operation failed** HTTP 400 error. An empty or unavailable Raven
  manifest can also be refreshed without crashing inventory conversion.
- **Consistent carrier inventory:** Journal identifiers, display names, and
  historical aliases now appear as one commodity across the manifest editor,
  commodities planner and overlay, shopping lists, build reports, and Nexus.
  Duplicate entries such as Liquid oxygen, Fruit and Vegetables, and Ceramic
  Composites are counted once. The independent **Local** column remains
  available for comparison. Saving or applying authoritative stock updates
  clears the relevant old aliases, and Frontier imports retain stable
  commodity identifiers across languages.
- **Cargo transfers and recovery:** Journal transfers update existing Raven
  inventory aliases with relative changes, preserving cargo movements made by
  other users of a shared carrier. Pending changes retain their original
  inventory keys across restarts. Recovery recognizes acknowledged transfers
  even when Raven changes a commodity's name, avoiding duplicate updates after
  a lost response. Keys that differ only by case are handled safely.
- **Carrier readiness breakdown:** Individual build previews, Combined Build
  Reports, and Nexus previews show **Ready on fleet carriers** as a segmented
  bar with a wrapping carrier-name and tonnage legend. Each share represents
  cargo usable against the remaining requirements; surplus of one commodity
  cannot cover another. Stable generated colors support more than ten carriers
  and adapt to light and dark themes. Missing cargo remains unknown.
- **Meta-Alloys shopping:** Historical **metalloys** names now match the same
  commodity in Ardent and Spansh results, so available stock appears correctly
  in shopping searches.
- **Copy bug-report details:** In **Diagnostics > Updates**, **Copy details**
  beside the installed version copies the exact app version, release channel,
  operating system, OS and app architectures, and .NET runtime.
- **Revisit installed release notes:** **Installed release notes** remains
  available after updating and works offline. **Available release notes**
  separately opens the notes for a newer update when one is found.
