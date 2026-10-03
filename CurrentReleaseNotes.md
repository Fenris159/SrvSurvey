# SrvSurvey-XP 2.1.3.0-rc.58.19

This patch improves Raven Colonial colonization and Fleet Carrier cargo sync,
adds recovery controls for unconfirmed construction deliveries, and keeps
commander and site changes from applying outdated data.

## Construction delivery recovery

- Construction deliveries are retained until Raven acknowledges them. Pending
  deliveries stay with their originating commander and survive restarting
  SrvSurvey.
- An **Unconfirmed construction deliveries** panel appears near the top of
  Colonization projects when a connection failure leaves Raven's response
  uncertain. Each delivery has an initially unchecked selection box.
- Check the delivery's credit on Raven, then select the records to resolve.
  **Retry selected deliveries** sends only those verified as missing;
  **Dismiss selected deliveries** clears local recovery records for those
  already credited. Unselected deliveries remain pending.
- Recovery buttons disable when nothing is selected or an upload is active.
  Checkbox choices reset after restarting or switching commander profiles.
  Another uncertain retry requires checking Raven and selecting the delivery
  again before resending it.
- Rejected deliveries can retry automatically, while deliveries with uncertain
  credit wait for your verification to avoid duplicate credit.

## Raven Colonial fixes

- Construction completion is now reported to Raven even when an earlier depot
  update already reduced the remaining cargo requirements to zero. Completed
  projects no longer miss their completion update because no cargo changed.
- If Raven cannot accept a completion update, SrvSurvey keeps the project
  incomplete locally and can retry on a later live completion event.
- Docking site-repair warnings clear after a successful lookup or repair, when
  switching systems or commanders, or when disabling Raven access. A delayed
  failure from an earlier context no longer brings an old warning back.
- Current site-repair warnings name the affected system. Recovery preserves
  unrelated Raven status messages.
- Project and system editors discard delayed results after their commander,
  system, or docking context changes. An old load, review, or import no longer
  restores data from the previous context.
- Site confirmation rechecks Raven's latest data and your editing permission.
  Newly conflicting changes stop publication, and a replacement site with the
  same name is kept separate from the original site.
- Successful project creation remains visible if a follow-up step fails, with
  the remaining problem reported separately. Primary-project restoration also
  recognizes success on its final check.
- Docking repairs can update missing body details even when the faction already
  matches. Site records missing the identity needed for repair now report the
  unresolved problem.
- Improved compatibility with older Raven site records and empty optional
  responses.

## Fleet Carrier cargo sync

- Failed cargo updates are retained in order across restarts. SrvSurvey checks
  Raven's cargo state before retrying uncertain changes and can reconcile them
  against a fresh market snapshot.
- Purchases, sales, and transfers use the carrier where the commander was
  docked when each event occurred, including when undocking happens in the
  same journal update.
- Cargo transactions arriving during carrier publication or a market refresh
  are applied after that update, preserving their effect on the final totals.

## Connection and commander handling

- Slow or interrupted Raven responses are bounded and reported as recoverable
  failures so they do not indefinitely hold up live journal processing.
- Switching commanders during a refresh loads the replacement commander's
  workspace once the earlier request finishes. API-key validation also keeps
  the saved key with the profile that initiated it.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.19
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.19`
- Tag: `xp2-v2.1.3.0-rc.58.19`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.19-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.19-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.19-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.19-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
