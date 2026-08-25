# SrvSurvey-XP 2.1.3.0-rc.38

This release candidate adds optional direct EDSM account synchronization to the
privacy and sharing workflow completed in `2.1.3.0-rc.37`.

## EDSM account synchronization

- Adds a dedicated EDSM opt-in card immediately below Inara under Settings >
  Privacy & sharing, with the active Commander, independently editable
  EDSM-registered Commander name, masked personal API key, direct settings-page
  link, save/enable action, and confirmed disable action.
- Stores the EDSM credential pair only in the active local Commander profile.
  Switching profiles loads separate credentials; clearing them cancels active
  delivery and removes pending in-memory events.
- Sends supported new Live journal events directly to EDSM's authenticated
  Journal API in ordered, bounded batches with the required application, game,
  build, and Commander metadata.
- Maintains EDSM's sanctioned system, station, market, coordinate, and ship
  context so journal events that omit those values can still be interpreted.

## Privacy and delivery safety

- Downloads and validates EDSM's current discarded-event policy before any
  journal event can leave the application. Chat, screenshot paths,
  `Status.json`, and currently unsupported companion-file events are excluded.
- Suppresses startup history, Legacy, alpha/beta, diagnostic replay, multicrew,
  and multi-window activity so only attributable future Live events are
  eligible.
- Uses a bounded memory-only queue, one in-flight request, bounded payloads and
  responses, rate-limit-aware delayed retries, and a credential-failure pause
  instead of a durable raw-journal outbox or immediate retry loop.
- Keeps EDSM independent from EDDN and Inara. Each service retains its own
  explicit opt-in, state, failure isolation, and duplicate-uploader warning.

## Packaging

- Version: `2.1.3.0-rc.38`
- Tag: `xp-v2.1.3.0-rc.38`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.38-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.38-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.38-x86_64.AppImage`

The Windows and Linux packages are self-contained. AppImages must be updated
manually; the application links directly to the selected XP release.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
