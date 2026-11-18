# SrvSurvey-XP 2.1.3.0-rc.77.22

RC77.22 improves long-route workspace responsiveness and fixes Mining session
selection warnings.

## Before upgrading

Exploration history uses schema 6, introduced in RC77.21. RC77.20 cannot open
that catalog. Keep a pre-upgrade copy of your data folder if you may need to
roll back to RC77.20 or earlier. If journal ordering cannot be verified,
automatic exploration value tracking pauses until history recovery or a new
explicit reset.

## What's changed

- **Faster route workspaces:** Regular and Fleet Carrier route windows create
  visible waypoint rows on demand instead of building the entire route table
  before opening. All waypoints remain available when scrolling, including
  their progress, copy, deletion, and biology controls.
- **Quiet Mining session details:** Opening reports without a selected session,
  or clearing the selection, no longer produces the four null-binding warnings.
  Selecting another session refreshes its system, ring, date, and ship details
  while preserving the existing date display.
