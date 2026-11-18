# SrvSurvey-XP 2.1.3.0-rc.77.21

RC77.21 adds granular survey resets and exploration sale receipts.

## Before upgrading

This release upgrades the exploration history catalog to schema 6. RC77.20 cannot
open the upgraded catalog. Keep a pre-upgrade copy of your data folder if you may
need to roll back. Missing original journals can pause automatic value tracking
until history recovery finishes.

## What's changed

- **Granular survey resets:** Exploration > Survey totals now has three actions.
  **Reset Everything Above** clears all six totals. **Reset Odometer** clears
  hyperspace jumps and distance travelled. **Reset Exp Value** clears estimated
  credits and scanned, DSS, and landed counts. Each action has its own confirmation.
- **A fresh value starting point:** Resetting estimated value keeps automatic
  tracking enabled and starts a new survey period. Your full exploration history
  and unsold balance remain available in Exploration history. The starting point
  survives restarts and importing older journals.
- **Paid exploration receipts:** Selling exploration data adds a filterable
  **Paid** row alongside the collected data. The row shows the sale date, actual
  credited payout in the Estimate column, travel since the previous sale, and
  scanned, DSS, and landed totals for identified sold systems. Manual survey
  resets do not change receipt travel totals. Select a receipt for its systems
  and original sale record. Date filters use collection dates for data rows and
  sale dates for Paid receipts.
- **Reliable history:** Duplicate journal delivery does not duplicate receipts.
  Older sales are backfilled from available journals; missing payout or travel
  details are marked unavailable. Resetting totals preserves the scan details
  needed for later DSS events, and shutdown waits for in-progress confirmed resets before closing.

- **Journey report exports:** Input image files are closed before the completed
  report folder is published, improving export reliability on Windows.
