# Discord carrier announcements

Open **Settings → Application → Discord carrier announcements**.

1. In the destination Discord channel's settings, open **Integrations → Webhooks**, create a webhook, and copy its URL.
2. Paste the URL into SrvSurvey. **Send test message** posts one connection message to that channel, even before automatic announcements are enabled.
3. Select **Personal fleet carrier**, **Squadron carrier**, or both, and choose which jump events to announce.
4. Enable announcements and select **Save changes**. Changes take effect when saved.

SrvSurvey posts when a jump is scheduled, cancelled, or confirmed to have arrived. Each message identifies the carrier and includes its itinerary, destination body, and scheduled departure when the game provides them. Discord displays timestamps in each reader's local time. An active matching itinerary from the Fleet Carrier route workspace is included when available.

Keep SrvSurvey running with the carrier manager's live journal selected. Personal and squadron carriers are tracked independently by carrier ID. Scheduled and cancelled notifications require the request in that journal; orders placed by a different squadron member cannot be discovered from a journal that does not contain them. A matching `CarrierLocation` at or after departure confirms a tracked request. An explicit `CarrierJump` can also confirm completion for a known owned carrier when its request is unavailable. A login location or countdown alone never triggers an arrival announcement.

Startup journal history restores tracking without posting historical notifications. Passenger jumps on other carriers are ignored. Delivery runs separately from journal processing, respects Discord rate-limit responses, and reports failures in the settings section. There is no offline message backlog. Disabling or changing the saved configuration discards older queued announcements; a request already sent to Discord may finish.

The webhook URL is masked in the editor, stored in this commander's local application settings, and excluded from cloud profile sync. Delivery status messages do not contain the URL. No Discord bot or FCMS account is required.

The announcement information flow uses [Fuel Rats FCMS](https://github.com/FuelRats/FCMS/blob/master/FCMS/utils/webhooks.py) as a behavioral reference. The C# journal tracking and Discord delivery implementation are native to SrvSurvey.
