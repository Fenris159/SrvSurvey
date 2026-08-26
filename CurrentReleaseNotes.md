# SrvSurvey-XP 2.1.3.0-rc.41

This release candidate retains the optional direct EDSM account synchronization
and its RC39 journal compatibility fix, promotes EDDN from validation schemas to
production sharing, reduces routine upload log noise, and completes SDL3
controller shortcut support on Windows and Linux.

## Controller input and shortcut editing

- Uses SDL3 gamepad events for standard controllers on Windows and Linux,
  mapping buttons, D-pad directions, and triggers into the existing global
  shortcut system. Joysticks and HOTAS devices retain their SDL polling
  fallback.
- Allows controller chords to be assigned directly from the existing shortcut
  fields. D-pad updates are coalesced so diagonals remain assignable, while
  disconnects clear partial input without triggering an action.
- Makes shortcut editing transactional and easier to leave: clicking outside
  accepts a completed binding, Escape restores the previous binding before a
  second Escape releases focus, and outside clicks cancel incomplete input.
  Clear instructions now appear above the input configuration controls.

## Network upload reporting

- Replaces per-event EDSM and EDDN success messages with one aggregate count
  after each 15-minute activity window, keeping the logs useful during long
  survey sessions without recording every accepted event.
- Removes EDSM summaries for events intentionally excluded by its current
  Journal API discard policy. Hard failures, rejections, retries, pauses, and
  possible data-loss warnings remain immediate.

## EDDN production sharing

- Sends eligible opted-in Live journal and companion-file data through EDDN's
  Live gateway using production schema references rather than `/test` schemas.
- Normalizes both new and restored durable messages to remove any legacy
  `/test` suffix before delivery. Existing consent, attribution, multicrew,
  multi-window, retry, and duplicate-uploader protections remain unchanged.

## EDSM journal compatibility

- Fixes EDSM processing of the object-valued `Multicrew` statistics section in
  Elite's `Statistics` journal event. It is no longer misread as the Boolean
  active-crew flag, preventing an isolated cast error after EDSM is enabled.
- Preserves normal multicrew suppression for Boolean session flags and explicit
  crew join, role-change, and exit events.

## EDSM account synchronization

- Adds a dedicated EDSM opt-in card immediately below Inara under Settings >
  Privacy & sharing, with the active Commander, masked personal API key, direct
  settings-page link, save/enable action, and confirmed disable action. The
  active Commander name is used automatically and must match the EDSM account.
- Stores the EDSM API key only in the active local Commander profile.
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

- Version: `2.1.3.0-rc.41`
- Tag: `xp-v2.1.3.0-rc.41`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.41-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.41-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.41-x86_64.AppImage`

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
