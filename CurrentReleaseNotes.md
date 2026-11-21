# SrvSurvey-XP 2.1.3.0-rc.77.25

RC77.25 fixes automatic screenshot conversion so Steam captures are only
included when **Process Steam screenshots** is enabled.

## Before upgrading

Exploration history uses schema 6, introduced in RC77.21. RC77.20 cannot open
that catalog. Keep a pre-upgrade copy of your data folder if you may need to
roll back to RC77.20 or earlier. If journal ordering cannot be verified,
automatic exploration value tracking pauses until history recovery or a new
explicit reset.

## What's changed

- **Process Steam screenshots** now strictly controls whether Steam JPEG
  captures are admitted or drained by the screenshot processor. Turning it off
  pauses any pending or restart-recovered Steam captures while **Enable
  screenshot processing** continues converting Elite BMP and HighRes captures.
- **In-flight Steam scans** now evaluate your current screenshot settings
  before queuing captures, preventing a capture from converting if you disable
  processing while a scan is running.
- **Screenshot settings controls** disable the Steam inclusion checkbox and
  folder buttons when **Enable screenshot processing** is off, and update the
  Steam folder description to reflect both toggles accurately.
