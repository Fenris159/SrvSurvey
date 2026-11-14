# SrvSurvey-XP 2.1.3.0-rc.77.16

RC77.16 adds customizable Discord carrier announcements with live departure
countdowns, your own carrier screenshots, and a pop-out message editor with a
formatted preview.

## What's changed

- **Customize carrier announcements:** Open **Edit announcement messages…** under
  **Settings > Application > Discord carrier announcements**. Give scheduled,
  cancelled, and completed jumps their own titles and message templates. Insert
  fields for your carrier, itinerary, destination body, departure, arrival, and
  route, or arrange those details yourself instead of using the standard fields.
- **Preview Discord formatting:** See a sample journey with your carrier's
  identity and the selected image while editing. The toolbar supports bold,
  italic, underline, strikethrough, inline code, quotes, and bullet lists,
  including nested bold and italic. Unfinished formatting stays visible while
  you type.
- **Show a live departure countdown:** Scheduled jumps with a reported departure
  include Discord's updating relative time. Absolute timestamps display in each
  reader's local time. Custom templates can place the countdown with
  `{countdown}`; cancellations and arrivals leave that field empty.
- **Frame your carrier screenshot:** Choose a PNG or JPEG up to 8 MB separately
  for your personal and squadron carriers. Pan and zoom beneath the fixed 16:9
  crop frame, watch the announcement preview follow your selection, and reset
  the crop when needed. Discord receives the selected area as a 1280 × 720 image
  attachment; your original file stays unchanged. Reselecting an updated file
  refreshes the preview even when its filename is unchanged.
- **Apply changes when ready:** **Apply to settings** transfers the editor's
  drafts, then **Save changes** activates them. **Cancel** discards the editor's
  changes. Templates, image paths, and crops remain local with the Discord
  settings and are excluded from cloud profile sync.
- **Keep announcements reliable:** Missing, unreadable, invalid, or oversized
  images no longer interrupt subsequent announcements. The message is delivered
  without an image and the status asks you to choose it again. A failed crop
  never uploads the uncropped original. Saving disabled announcements or a
  changed webhook prevents older messages still being prepared or awaiting a
  retry from being sent with the previous settings.
