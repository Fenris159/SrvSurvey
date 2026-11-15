# SrvSurvey-XP 2.1.3.0-rc.77.17

RC77.17 improves Discord carrier announcement layout and adds delivery
diagnostics for investigating posts that differ from the message editor preview.

## What's changed

- **Keep time fields on their own rows:** Countdown and arrival confirmation
  fields now start separate rows in Discord, matching the editor's layout.
- **Check Discord's confirmation:** After Discord accepts an announcement,
  SrvSurvey checks the returned title and carrier image. A changed title,
  missing image, or unreadable confirmation produces a warning in Discord
  settings. Accepted messages are not posted again because of a warning.
  Malformed response text is handled without interrupting later announcements.
- **Trace settings through delivery:** Correlated log entries follow the editor's
  Apply action, saved settings, queued event, prepared image, and Discord
  response. They identify the carrier type, settings revision, template and
  image fingerprints, image byte count, HTTP status, and Discord message ID.
  These entries omit webhook URLs and tokens, message text, image paths, and
  CDN links.
- **Explain upload rejections:** Size, invalid-file, and message-format errors
  from Discord now have specific status messages. The outgoing image is also
  checked against the 8 MiB limit after cropping; a rejected local image uses
  the existing text-only fallback.

If a live post still differs from the preview, open **Diagnostics > Logs**, use
**Copy logs**, and include the `[Discord carrier]` entries for the affected send
in the bug report. **Send test message** remains a connection-only check;
an actual carrier event exercises customized titles and images.
