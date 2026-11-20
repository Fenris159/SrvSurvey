# SrvSurvey-XP 2.1.3.0-rc.77.23

RC77.23 keeps manual fuel inputs in carrier route forecasts and fixes delayed
typing on GNOME XWayland desktops that use standard keyboard layouts.

## Before upgrading

Exploration history uses schema 6, introduced in RC77.21. RC77.20 cannot open
that catalog. Keep a pre-upgrade copy of your data folder if you may need to
roll back to RC77.20 or earlier. If journal ordering cannot be verified,
automatic exploration value tracking pauses until history recovery or a new
explicit reset.

## What's changed

- **Carrier route fuel forecasts** use the tank fuel, stored tritium, and used
  capacity submitted in Spansh Search. The waypoint table and shortfall warning
  use the same starting values. Select **Refresh** to recalculate the remaining
  route from the live carrier readings at your current waypoint. Undoing a
  replacement search restores the saved route and its live forecast.
- **Responsive typing on GNOME XWayland** applies on normal startup when the
  desktop uses standard keyboard layouts. SrvSurvey reads those keystrokes
  directly to avoid IBus delays. Configured input methods remain available,
  and global shortcuts continue to use their separate keyboard listeners.
