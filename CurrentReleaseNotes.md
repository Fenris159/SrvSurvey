# SrvSurvey-XP 2.1.3.0-rc.64.5

RC.64.5 improves notification overlay stability and lets separate SrvSurvey
instances share journal data safely while multiple commanders are playing.
It also reduces repeated Raven Colonial warnings and clarifies a server-side
quest error.

## What's changed

- **Keep notification overlays stable.** The message-expiry bar no longer
  participates in panel sizing, preventing layout failures when a long
  notification is followed by a shorter one. Existing message limits,
  formatting and expiry behavior are preserved.
- **Share journal data from multiple commanders.** Separate SrvSurvey instances
  can report to Inara, EDSM, EDDN, EDAstro and VoxStellar while multiple Elite
  clients are running, provided every detected client has a distinct commander
  identity and a live binding to a separate physical journal folder. Shared
  folders, aliases to the same folder and uncertain ownership continue to block
  data that cannot be attributed safely. Each provider's sharing consent and
  filtering still apply.
- **Track Raven Colonial cargo for separate commanders.** Commanders using
  separate journal folders can alternate deliveries to the same construction
  project. Each instance reads cargo from its own verified journal folder and
  tracks its commander's deliveries without mixing the other commander's cargo.
  Cargo files remain excluded whenever their owner cannot be verified.
- **Keep EDDN queues separate.** Each commander has an independent persistent
  upload queue, so one instance cannot block another commander's reports.
  Pending reports recover after a restart, and older queued reports retain
  their original uploader identity.
- **Prevent stale or foreign inventory reports.** Inara companion-file reads
  reject snapshots older than the journal event that requested them. Inara,
  EDSM and VoxStellar stop processing a batch at an unexpected commander change,
  preventing later events from being assigned to the previous commander.
- **Reduce repeated Raven warnings.** Persistent quest-loading and unverified
  cargo warnings are logged once until recovery rather than repeating with
  every update. Current warnings remain visible. When Raven returns a malformed
  Azure commander-name filter, the diagnostic identifies the server-side
  escaping problem instead of suggesting changes to the commander name or key.

- **Update security dependencies.** The desktop packages use patched .NET XML
  and cryptography libraries.

## Update channel and packages

RC.64.5 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64.5`
- Tag: `xp2-v2.1.3.0-rc.64.5`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64.5-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64.5-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64.5-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64.5-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Multi-commander sharing requires separate
physical journal folders and a verified live commander binding for each client;
Steam or Epic folder names alone do not establish ownership. Shared UI inventory
continues to use its existing multi-client safeguards. Raven's Azure filter
failure still requires a correction by the Raven Colonial server operator.
