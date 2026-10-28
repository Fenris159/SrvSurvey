# Stealth Mode

Enable **Settings → Privacy & sharing → Stealth Mode** to delay enabled EDDN, Inara, EDAstro and EDSM uploads during an expedition. The checkbox does not turn any sharing service on. Raven API traffic and read-only lookups continue normally.

Reports are captured in the existing private durable queues. Closing SrvSurvey, restarting the game, entering the main menu, changing journals, or replaying startup history does not release them. A live, attributable `Docked` journal event releases only that commander's reports recorded through that docking. New discoveries afterward wait for another docking. Any docking counts, including a fleet-carrier docking; the application does not wait for a sale of exploration data.

Turning Stealth Mode off resumes normal delivery of retained reports. Existing provider consent, API credentials, supported-event policies, pacing and retry rules still apply. Each service keeps independent acknowledgement receipts, so one service failing does not clear the others' backlog. Enabling Stealth Mode cancels active requests where possible; a request already accepted by a service cannot be recalled.

The saved docking watermarks are separate from journal history. They are written atomically before any waiting data becomes eligible. If release evidence cannot be read or saved, new data remains held until a live docking can be saved successfully.

Expedition retention supports up to 131,072 pending reports and 512 MiB of durable storage per provider. Reaching a storage limit reports an error and preserves the existing backlog. The queue format is unchanged, but older application builds have smaller retry limits; drain a large expedition backlog before downgrading.
