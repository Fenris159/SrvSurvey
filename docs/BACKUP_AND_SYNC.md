# Backup and Google Drive sync

Open **Settings → Data & migration → Backup & sync with Google Drive**. Local file export and restore work without a Google account. Google linking becomes available when the application includes a configured publisher client, or when a development build imports that client JSON.

## Using more than one computer

1. Link the same Google account on each computer. SrvSurvey opens your browser only when you choose **Link Google Drive**. You can cancel while it waits for authorization.
2. Leave **Sync portable data on startup and back up on shutdown** enabled. SrvSurvey checks for cloud changes before loading its workspaces and saves a backup after its producers stop on orderly shutdown.
3. Choose **Backup now** before switching computers. **Sync now** uploads current changes and checks the other computers while the application is open; downloaded changes apply after restart.
4. If different computers changed the same value, choose which conflicting values to keep. Independent changes are combined. Arrays, such as a bookmark collection, are treated as a single value; a conflicting collection needs a choice rather than an automatic merge.
5. Close all SrvSurvey instances before applying a restore. Multiple Commander instances share this machine's backup identity and Google authorization. A second running instance defers startup restoration so cached settings cannot overwrite restored data.

Offline work remains local. A failed upload retains a local recovery snapshot and its change counters, so a later sync can retry. A forced process termination, power loss or logout that kills the process cannot guarantee a shutdown upload; use **Backup now** when needed.

## What follows you

| Data | Handling |
|---|---|
| Themes, colors, typography, portable overlay visibility/behavior, cockpit-view options, shortcuts | Synchronized |
| Last selected workspace and Mining, Surface Mining, Guardian and Fleet Carrier tabs | Synchronized |
| Saved mining search filters/results, routes, navigation and mining bookmarks, Firegroups data, system/Guardian surveys, journeys, Codex records, saved Boxel searches | Synchronized |
| Monitor choice, window/overlay positions and sizes, capture calibration, Wayland options, keyboard source, controller device, VR settings, sharing consent, local folders/voice choices | Backed up separately; restored only on the originating machine and operating system |
| OAuth tokens, integration credentials, journals, live Commander state, queued API submissions, screenshot attachments, provider caches and logs | Excluded |

Machine identities use a persisted installation ID associated with a hash of the OS machine identifier. Backups include that hash so the same computer can recover its hardware settings after an application reinstall, even when its local sync identity was lost. Raw OS identifiers are not sent to Google. Copying the settings folder to a different OS machine identifier creates a new identity rather than taking over the original machine's settings. Syncing Windows and Linux never restores the other platform's machine section. Reinstalling the OS may change that identifier and prevent hardware restoration. Without an OS identifier, only the originating installation can restore hardware settings. Portable data remains restorable on any computer.

The backup format is versioned and limited to **32 MiB**. Profiles exceeding that limit report an error and keep local files unchanged. Search presentations are the intentional exception to the cache exclusion, because they contain saved search choices. Local screenshot attachments are not transferred. Existing screenshot links stay attached to matching local bookmark and mining records during a portable restore.

## Recovery and history

SrvSurvey retains ten recent local recovery snapshots and ten cloud backups per machine. Expand **Restore a Google Drive backup** and refresh history to select a snapshot. The optional hardware restore is limited to backups created on this machine. The same hardware option also applies to imported local backups; leave it off when moving between computers.

Restores requested while the app is open are staged for restart. The current profile is backed up first. Each replaced file has a rollback copy under the local `profile-sync/restore-backups` folder; an interrupted file restore is recovered before workspaces load. Restoring an older backup creates new sync changes, including deletions, so other machines can recognize the deliberate restore.

Cloud backups live in Drive's private application-data folder, rather than your ordinary visible Drive files. SrvSurvey requests only `drive.appdata`; it cannot use that grant to browse your personal documents. [Google's application-data documentation](https://developers.google.com/workspace/drive/api/guides/appdata).

The restore history shows the backup count and total file size across machines. These files count toward your Google storage. Choose **Download selected backup** to save the original cloud file somewhere visible, without restoring it. **Delete selected backup** removes one file. **Clear all SrvSurvey cloud backups** removes the history shown for all computers on the linked account. Both deletion actions require confirmation and permanently remove those cloud files; local settings, local backups and Google linking stay intact. Backups uploaded after confirmation are retained. Refresh history to see the latest list. Automatic backups on any linked computer can create cloud files again, so turn off automatic sync on those computers if you want the folder to stay empty.

Disconnecting removes this machine's authorization and disables automatic sync. It retains cloud history and does not revoke another computer's authorization. Tokens are protected with Windows DPAPI on Windows; Linux uses a file restricted to the current user's read/write permissions. Backup files themselves are not encrypted, so keep exported backups somewhere private.

## One-time publisher setup

The publisher must create a Google Cloud OAuth client before distributing Google-enabled builds. End users should not have to create their own Google project.

1. Create/select the SrvSurvey Google Cloud project and enable the **Google Drive API**.
2. Configure **Google Auth Platform** branding, audience, support contacts and consent. Request only `https://www.googleapis.com/auth/drive.appdata`. Add test users while the application is in Testing.
3. Create an OAuth client of type **Desktop app** and download its client JSON. SrvSurvey uses the supported loopback callback on `127.0.0.1` with a dynamically selected port, PKCE and a random state. Its bounded TCP callback listener requires no HTTP.sys URL reservation or administrator setup on Windows. A Web client or service-account credential is unsuitable. [Google's native-application OAuth guide](https://developers.google.com/identity/protocols/oauth2/native-app).
4. For local development, choose **Import Google client setup** in Data & migration, then link Google Drive. This configuration remains local and is excluded from backups. Disconnect before replacing client setup.
5. For packaged releases, set repository Actions secrets `GOOGLE_DRIVE_CLIENT_ID` and `GOOGLE_DRIVE_CLIENT_SECRET` from the Desktop client. The build workflow passes these through MSBuild properties `GoogleDriveClientId` and `GoogleDriveClientSecret` into application metadata. Desktop OAuth clients are public clients; embedding that client setup does not embed user authorization tokens.
6. Before public release, move the consent application to Production and satisfy any requirements shown in Google Auth Platform. Testing-mode renewable authorization can expire, so it is unsuitable for a persistent public sync feature. [Google's OAuth policies](https://developers.google.com/identity/protocols/oauth2/policies).

Changing the Google project/client can change the application-data namespace, so preserve the published client for continuity. The HTTP integration uses resumable uploads for both small and larger backups, as recommended for larger transfers by [Google's upload documentation](https://developers.google.com/workspace/drive/api/guides/manage-uploads).

## Validation scope

Automated tests use isolated profile folders, an HTTP test handler and a real loopback OAuth callback. They cover consent/state/PKCE handling, cancellation, token renewal, download bounds, upload metadata/history, conflicts, deletion, older backup restoration, hardware isolation, malformed backups, and multiple running profile readers. Live Google account linking and cross-platform desktop testing require the publisher client and remain separate acceptance checks.
