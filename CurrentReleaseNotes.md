# SrvSurvey-XP 2.1.3.0-rc.77.20

RC77.20 prevents structured route data from appearing as system names and improves
Inara response handling and failure diagnostics.

## What's changed

- **System-name imports:** JSON route data is rejected with guidance to use
  **Spansh URL from clipboard**. Rejected imports preserve the current route and
  unsaved edits. Plain-text lists still accept one system name per line.
- **Accurate system matching:** Autocomplete suggestions resolve a waypoint only
  when the system name matches exactly, ignoring capitalization. Unresolved
  systems retain their original names.
- **Inara recovery:** Malformed status fields retain the upload batch for retry.
  Unexpected optional error text no longer prevents valid upload results from
  being processed. API-level errors are handled even when event results are
  absent. Ambiguous status-400 errors stay queued for retry, including after
  restarting the app, while independently accepted reports are acknowledged.
- **Inara diagnostics:** Failed uploads record their stage, HTTP status, response
  field types, and a bounded stack trace. Diagnostics redact the API key and omit
  response bodies and arbitrary exception messages.

Routes that already contain JSON fragments need to be imported again using the
Spansh results-page URL. This update prevents new malformed imports; it does not
rewrite existing routes.
