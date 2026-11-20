# SrvSurvey-XP 2.1.3.0-rc.77.24

RC77.24 fixes an Exploration issue that could leave body counts stuck after
resetting survey totals.

## Before upgrading

Exploration history uses schema 6, introduced in RC77.21. RC77.20 cannot open
that catalog. Keep a pre-upgrade copy of your data folder if you may need to
roll back to RC77.20 or earlier. If journal ordering cannot be verified,
automatic exploration value tracking pauses until history recovery or a new
explicit reset.

## What's changed

- **Reset Everything Above** and **Reset Exp Value** now clear scanned, DSS,
  and landed counts when the journal index is complete, even if a missing
  scan prevents valuation of a mapping record. Paid receipts stay in
  Exploration History. Automatic value tracking resumes from the new starting
  point when valuation can continue.
- **Recovery guidance** in Survey totals explains how to refresh Exploration
  History, resume recovery when prompted, and retry the reset.
