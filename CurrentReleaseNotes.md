# SrvSurvey-XP 2.1.3.0-rc.77.8

RC77.8 adds bulk Fleet Carrier linking and checkbox carrier selection to
Colonization. Choose the carriers to include, then link or unlink them across
selected Building projects in one operation. The same selection also controls
carrier cargo calculations and Market Search.

## What's changed

- **Choose several carriers:** Use Carrier now opens a checkbox list with
  Select all and Clear selection controls. The list stays open while you choose,
  and a note below the field shows how many carriers are selected. Keyboard
  selection and Escape are supported.
- **Bulk project links:** Filter My build projects to Building, check the
  projects' Show boxes, and use Link Carrier To Selected Projects or Unlink
  Carrier From Selected Projects. The action applies the checked carriers to
  shown projects matching the current filters. Planning projects are always
  skipped, and an empty carrier selection disables the actions.
- **Link during build creation:** The new-build form and planned-site workflow
  offer an optional Link carriers to this build checkbox and carrier picker.
  Review includes the selected carrier count. The build is created first;
  carrier linking starts only after Raven returns its build ID.
- **Consistent cargo calculations:** Checked carriers drive Loaded percentages,
  carrier cargo totals and Market Search needs. Use Carrier selections are saved
  per commander and remain synchronized with Market Search through project tag
  changes and restarts. Renaming or reordering carriers preserves the selection,
  and clearing every checkbox stays empty rather than reverting to all carriers.
- **Recover from partial failures:** Individual carrier-link failures leave the
  created build and successful links intact. Bulk operations continue with the
  remaining projects and carriers, and report failures for retry.
