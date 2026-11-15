# Discord carrier announcements

Open **Settings → Application → Discord carrier announcements**.

1. In the destination Discord channel's settings, open **Integrations → Webhooks**, create a webhook, and copy its URL.
2. Paste the URL into SrvSurvey. **Send test message** posts one connection message to that channel, even before automatic announcements are enabled.
3. Select **Personal fleet carrier**, **Squadron carrier**, or both, and choose which jump events to announce.
4. Enable announcements and select **Save changes**. Changes take effect when saved.

SrvSurvey posts when a jump is scheduled, cancelled, or confirmed to have arrived. Each default message identifies the carrier and includes its itinerary, destination body, and scheduled departure when the game provides them. Scheduled jumps also include a live countdown using Discord's relative timestamp display. Discord updates that display and shows absolute timestamps in each reader's local time. An active matching itinerary from the matching FC Routes or SC Routes workspace is included when available.

Choose **Edit announcement messages…** below the Discord settings to open the pop-out editor. Select a jump event to customize its title and message independently. The toolbar inserts bold, italic, underline, strikethrough, inline code, quotes, and bullet lists around selected text. **Insert field** adds a field at the cursor. The preview uses the same expanded message as delivery, with a sample journey to Lave and your carrier's identity when available; Discord controls final spacing and time display.

Supported fields are `{carrier}`, `{carrier_name}`, `{callsign}`, `{carrier_kind}`, `{origin}`, `{destination}`, `{body}`, `{departure}`, `{countdown}`, `{arrival}`, and `{route}`. `{countdown}` produces Discord's live relative departure time for scheduled jumps with a reported departure; it is empty for cancellations and arrivals. `{arrival}` is only populated for confirmed arrivals. Missing optional body and route fields are empty. Turn off **Include standard itinerary and time fields** to arrange all the journey information in your own message. Titles are limited to 256 characters and messages to 4096; expanded values and details are bounded to Discord's embed limits.

Under **Carrier image**, select your personal or squadron carrier and choose its PNG or JPEG screenshot (up to 8 MB and 16 megapixels, with neither side exceeding 16,384 pixels). Each carrier has its own image and crop, shared by its three event templates. After selecting a file, the image selector shows a fixed 16:9 frame. Drag the image to pan, scroll or use the Zoom slider to magnify it, or use the arrow keys to move the framing. The frame stays filled without stretching the image. **Reset crop** returns to the centered framing. The Discord preview follows the selected area, and delivery exports that area as a 1280 × 720 PNG attachment. The original file is unchanged, and no public image hosting is needed. Keep the selected file available at its saved path. If it becomes unavailable, the announcement is still delivered and the settings status asks you to choose the image again.

**Restore this event's default** resets only the selected event's template. **Apply to settings** copies the editor's drafts into settings; use **Save changes** there to activate and persist them. **Cancel** closes the editor without applying its changes. Templates and image paths stay local with the webhook settings and are excluded from cloud profile sync.

Keep SrvSurvey running with the carrier manager's live journal selected. Personal and squadron carriers are tracked independently by carrier ID. Scheduled and cancelled notifications require the request in that journal; orders placed by a different squadron member cannot be discovered from a journal that does not contain them. A matching `CarrierLocation` at or after departure confirms a tracked request. An explicit `CarrierJump` can also confirm completion for a known owned carrier when its request is unavailable. A login location or countdown alone never triggers an arrival announcement.

Startup journal history restores tracking without posting historical notifications. Passenger jumps on other carriers are ignored. Delivery runs separately from journal processing, respects Discord rate-limit responses, and reports failures in the settings section. There is no offline message backlog. Disabling or changing the saved configuration discards older queued announcements; a request already sent to Discord may finish.

The webhook URL is masked in the editor, stored in this commander's local application settings, and excluded from cloud profile sync. Delivery status messages do not contain the URL. No Discord bot or FCMS account is required.

The announcement information flow uses [Fuel Rats FCMS](https://github.com/FuelRats/FCMS/blob/master/FCMS/utils/webhooks.py) as a behavioral reference. The C# journal tracking and Discord delivery implementation are native to SrvSurvey.
